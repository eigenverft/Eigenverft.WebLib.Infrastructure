using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson
{
    // This provider deliberately derives from ConfigurationProvider instead of FileConfigurationProvider/JsonConfigurationProvider.
    // The standard file provider binds reload watching to its source path and reloads directly into active Data, which conflicts
    // with prepare/compare/commit and Last-Known-Good semantics. Stage 2 should add a small generation-bound watcher around this
    // same candidate-loading pipeline rather than mutate FileConfigurationSource.Path or private watcher state.
    internal sealed class SwitchableJsonConfigurationProvider : ConfigurationProvider, ISwitchableJsonConfiguration
    {
        private static readonly StringComparer ConfigurationKeyComparer = StringComparer.OrdinalIgnoreCase;
        private readonly object _switchGate = new();
        private readonly string _contentRootPath;
        private readonly bool _optionalInitialSource;
        private readonly SwitchableJsonRuntimeFailurePolicy _runtimeFailurePolicy;
        private string _currentSourcePath;

        public SwitchableJsonConfigurationProvider(
            string name,
            string contentRootPath,
            string initialSourcePath,
            bool optionalInitialSource,
            SwitchableJsonRuntimeFailurePolicy runtimeFailurePolicy)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(initialSourcePath);

            if (!Enum.IsDefined(runtimeFailurePolicy))
            {
                throw new ArgumentOutOfRangeException(nameof(runtimeFailurePolicy));
            }

            Name = name;
            _contentRootPath = Path.GetFullPath(contentRootPath);
            _currentSourcePath = NormalizeSourcePath(initialSourcePath);
            _optionalInitialSource = optionalInitialSource;
            _runtimeFailurePolicy = runtimeFailurePolicy;
        }

        public string Name { get; }

        public string CurrentSourcePath
        {
            get
            {
                lock (_switchGate)
                {
                    return _currentSourcePath;
                }
            }
        }

        public event EventHandler<SwitchableJsonConfigurationEventArgs>? LifecycleChanged;

        public override bool TryGet(string key, out string? value)
        {
            lock (_switchGate)
            {
                return base.TryGet(key, out value);
            }
        }

        public override void Set(string key, string? value)
        {
            lock (_switchGate)
            {
                base.Set(key, value);
            }
        }

        public override IEnumerable<string> GetChildKeys(
            IEnumerable<string> earlierKeys,
            string? parentPath)
        {
            lock (_switchGate)
            {
                return new List<string>(base.GetChildKeys(earlierKeys, parentPath));
            }
        }

        public override void Load()
        {
            lock (_switchGate)
            {
                try
                {
                    Data = JsonConfigurationSnapshotLoader.Load(_currentSourcePath);
                }
                catch (Exception exception) when (
                    _optionalInitialSource && IsSourceNotFound(exception))
                {
                    Data = new Dictionary<string, string?>(ConfigurationKeyComparer);
                }
            }
        }

        public SwitchableJsonSwitchResult TrySwitch(string sourcePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

            string requestedSourcePath = NormalizeSourcePath(sourcePath);
            SwitchableJsonSwitchResult result;
            bool raiseConfigurationReload = false;

            // V1 intentionally serializes candidate IO, comparison, and commit under one gate. This avoids stale prepared
            // candidates without introducing generations or a public prepare/commit transaction model. A future version can
            // prepare concurrently and bind candidates to a generation if expensive or remote sources make that worthwhile.
            lock (_switchGate)
            {
                string previousSourcePath = _currentSourcePath;

                if (SourcePathsEqual(previousSourcePath, requestedSourcePath))
                {
                    result = CreateResult(
                        SwitchableJsonSwitchStatus.AlreadyCurrent,
                        previousSourcePath,
                        requestedSourcePath,
                        previousSourcePath,
                        sourceChanged: false,
                        configurationChanged: false,
                        SwitchableJsonFailureKind.None,
                        exception: null);
                }
                else
                {
                    try
                    {
                        IDictionary<string, string?> candidateData = JsonConfigurationSnapshotLoader.Load(requestedSourcePath);
                        bool configurationChanged = !ConfigurationDataEquals(Data, candidateData);

                        // Publish a complete dictionary reference rather than mutating the active dictionary in place. Individual
                        // IConfiguration reads therefore observe a complete old or new provider snapshot. IConfiguration itself
                        // does not provide a transaction spanning multiple independent key reads by a consumer.
                        Data = candidateData;
                        _currentSourcePath = requestedSourcePath;
                        raiseConfigurationReload = configurationChanged;

                        result = CreateResult(
                            SwitchableJsonSwitchStatus.Succeeded,
                            previousSourcePath,
                            requestedSourcePath,
                            requestedSourcePath,
                            sourceChanged: true,
                            configurationChanged,
                            SwitchableJsonFailureKind.None,
                            exception: null);
                    }
                    catch (Exception exception) when (IsCandidateLoadFailure(exception))
                    {
                        result = CreateResult(
                            SwitchableJsonSwitchStatus.Rejected,
                            previousSourcePath,
                            requestedSourcePath,
                            previousSourcePath,
                            sourceChanged: false,
                            configurationChanged: false,
                            ClassifyFailure(exception),
                            exception);
                    }
                }

                // Keep the IConfiguration notification within the serialized switch operation so a later switch cannot publish
                // another snapshot before consumers receive this snapshot's reload signal. Lifecycle callbacks are raised outside
                // the gate to avoid holding provider synchronization while arbitrary application code executes.
                if (raiseConfigurationReload)
                {
                    OnReload();
                }
            }

            PublishLifecycle(result);

            if (result.Status == SwitchableJsonSwitchStatus.Rejected &&
                _runtimeFailurePolicy == SwitchableJsonRuntimeFailurePolicy.Throw &&
                result.Exception is not null)
            {
                ExceptionDispatchInfo.Capture(result.Exception).Throw();
            }

            return result;
        }

        private string NormalizeSourcePath(string sourcePath)
        {
            return Path.IsPathFullyQualified(sourcePath)
                ? Path.GetFullPath(sourcePath)
                : Path.GetFullPath(Path.Combine(_contentRootPath, sourcePath));
        }

        private static bool SourcePathsEqual(string left, string right)
        {
            // Windows paths are case-insensitive for the normal supported file-system model. Other platforms use ordinal
            // comparison because case sensitivity is file-system dependent and silently folding case could identify distinct files.
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return string.Equals(left, right, comparison);
        }

        private static bool ConfigurationDataEquals(
            IDictionary<string, string?> current,
            IDictionary<string, string?> candidate)
        {
            if (current.Count != candidate.Count)
            {
                return false;
            }

            foreach (KeyValuePair<string, string?> pair in current)
            {
                if (!candidate.TryGetValue(pair.Key, out string? candidateValue) ||
                    !string.Equals(pair.Value, candidateValue, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private SwitchableJsonSwitchResult CreateResult(
            SwitchableJsonSwitchStatus status,
            string previousSourcePath,
            string requestedSourcePath,
            string currentSourcePath,
            bool sourceChanged,
            bool configurationChanged,
            SwitchableJsonFailureKind failureKind,
            Exception? exception)
        {
            return new SwitchableJsonSwitchResult(
                Name,
                status,
                previousSourcePath,
                requestedSourcePath,
                currentSourcePath,
                sourceChanged,
                configurationChanged,
                failureKind,
                exception,
                DateTimeOffset.UtcNow);
        }

        private void PublishLifecycle(SwitchableJsonSwitchResult result)
        {
            SwitchableJsonConfigurationEventKind kind = result.Status switch
            {
                SwitchableJsonSwitchStatus.Succeeded => SwitchableJsonConfigurationEventKind.SwitchSucceeded,
                SwitchableJsonSwitchStatus.AlreadyCurrent => SwitchableJsonConfigurationEventKind.SwitchAlreadyCurrent,
                SwitchableJsonSwitchStatus.Rejected => SwitchableJsonConfigurationEventKind.SwitchRejected,
                _ => throw new InvalidOperationException($"Unsupported switch status '{result.Status}'."),
            };

            LifecycleChanged?.Invoke(this, new SwitchableJsonConfigurationEventArgs(kind, result));
        }

        private static bool IsCandidateLoadFailure(Exception exception)
        {
            return exception is FileNotFoundException or
                DirectoryNotFoundException or
                FormatException or
                UnauthorizedAccessException or
                IOException;
        }

        private static bool IsSourceNotFound(Exception exception)
        {
            return exception is FileNotFoundException or DirectoryNotFoundException;
        }

        private static SwitchableJsonFailureKind ClassifyFailure(Exception exception)
        {
            return exception switch
            {
                FileNotFoundException => SwitchableJsonFailureKind.SourceNotFound,
                DirectoryNotFoundException => SwitchableJsonFailureKind.SourceNotFound,
                FormatException => SwitchableJsonFailureKind.InvalidJson,
                UnauthorizedAccessException => SwitchableJsonFailureKind.AccessDenied,
                IOException => SwitchableJsonFailureKind.IoError,
                _ => throw new ArgumentOutOfRangeException(nameof(exception)),
            };
        }

        private static class JsonConfigurationSnapshotLoader
        {
            public static IDictionary<string, string?> Load(string sourcePath)
            {
                using FileStream stream = new(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                var parser = new SnapshotJsonConfigurationProvider();
                return parser.Parse(stream);
            }

            private sealed class SnapshotJsonConfigurationProvider : JsonConfigurationProvider
            {
                public SnapshotJsonConfigurationProvider()
                    : base(new JsonConfigurationSource
                    {
                        Path = "switchable-candidate.json",
                        Optional = false,
                        ReloadOnChange = false,
                    })
                {
                }

                public IDictionary<string, string?> Parse(Stream stream)
                {
                    base.Load(stream);
                    return new Dictionary<string, string?>(Data, ConfigurationKeyComparer);
                }
            }
        }
    }
}
