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
    // with prepare/compare/commit and Last-Known-Good semantics. File watching is therefore a small generation-bound layer around
    // this provider's own candidate pipeline rather than a mutation of FileConfigurationSource.Path or private watcher state.
    internal sealed class SwitchableJsonConfigurationProvider : ConfigurationProvider, ISwitchableJsonConfiguration, IDisposable
    {
        private static readonly StringComparer ConfigurationKeyComparer = StringComparer.OrdinalIgnoreCase;

        // One gate deliberately protects both provider reads/writes and every source-state transition. This is the smallest
        // correctness model for V1: a manual switch, watcher reload, IConfiguration Set, and provider read cannot observe or
        // publish an in-between dictionary/source pair. The trade-off is that provider-local reads can briefly wait while a
        // candidate file is being read. If candidate IO ever becomes expensive, the natural next design is a separate operation
        // gate plus an immutable/replace-only state snapshot; that complexity is intentionally not paid for local JSON files now.
        private readonly object _switchGate = new();
        private readonly string _contentRootPath;
        private readonly bool _optionalInitialSource;
        private readonly bool _reloadOnChange;
        private readonly int _reloadDelayMilliseconds;
        private readonly SwitchableJsonRuntimeFailurePolicy _runtimeFailurePolicy;
        private string _currentSourcePath;

        // Generation identifies the watcher that is allowed to affect the active provider state. It advances only when an
        // initial load/switch commits a watcher identity (and once more on Dispose). A callback from an old watcher may already
        // be queued after that watcher is disposed; generation comparison turns such callbacks into harmless no-ops.
        private long _generation;
        private ActiveSourceWatcher? _activeWatcher;
        private bool _disposed;

        public SwitchableJsonConfigurationProvider(
            string name,
            string contentRootPath,
            string initialSourcePath,
            bool optionalInitialSource,
            bool reloadOnChange,
            int reloadDelayMilliseconds,
            SwitchableJsonRuntimeFailurePolicy runtimeFailurePolicy)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(initialSourcePath);

            if (reloadDelayMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(reloadDelayMilliseconds));
            }

            if (!Enum.IsDefined(runtimeFailurePolicy))
            {
                throw new ArgumentOutOfRangeException(nameof(runtimeFailurePolicy));
            }

            Name = name;
            _contentRootPath = Path.GetFullPath(contentRootPath);
            _currentSourcePath = NormalizeSourcePath(initialSourcePath);
            _optionalInitialSource = optionalInitialSource;
            _reloadOnChange = reloadOnChange;
            _reloadDelayMilliseconds = reloadDelayMilliseconds;
            _runtimeFailurePolicy = runtimeFailurePolicy;
        }

        public string Name { get; }

        public string CurrentSourcePath
        {
            get
            {
                lock (_switchGate)
                {
                    ThrowIfDisposed();
                    return _currentSourcePath;
                }
            }
        }

        public event EventHandler<SwitchableJsonConfigurationEventArgs>? LifecycleChanged;

        public override bool TryGet(string key, out string? value)
        {
            lock (_switchGate)
            {
                ThrowIfDisposed();
                return base.TryGet(key, out value);
            }
        }

        public override void Set(string key, string? value)
        {
            lock (_switchGate)
            {
                ThrowIfDisposed();
                base.Set(key, value);
            }
        }

        public override IEnumerable<string> GetChildKeys(
            IEnumerable<string> earlierKeys,
            string? parentPath)
        {
            lock (_switchGate)
            {
                ThrowIfDisposed();
                return new List<string>(base.GetChildKeys(earlierKeys, parentPath));
            }
        }

        public override void Load()
        {
            lock (_switchGate)
            {
                ThrowIfDisposed();

                long nextGeneration = _generation + 1;
                ActiveSourceWatcher? preparedWatcher = CreateWatcher(_currentSourcePath, nextGeneration);

                try
                {
                    Data = LoadInitialData(_currentSourcePath);
                }
                catch
                {
                    preparedWatcher?.Dispose();
                    throw;
                }

                // Initial Load follows the same watcher ownership rule as a runtime switch: only after the JSON snapshot has
                // loaded successfully do we publish the prepared watcher/generation. If loading failed, the watcher was disposed
                // above and the provider never exposes a partially initialized active source.
                ActiveSourceWatcher? previousWatcher = _activeWatcher;
                _generation = nextGeneration;
                _activeWatcher = preparedWatcher;
                previousWatcher?.Dispose();
            }
        }

        public SwitchableJsonSwitchResult TrySwitch(string sourcePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

            string requestedSourcePath = NormalizeSourcePath(sourcePath);
            SwitchableJsonSwitchResult result;
            bool raiseConfigurationReload = false;

            // Candidate IO, watcher preparation, comparison, and commit remain serialized under one gate. The watcher for a
            // candidate is prepared before the candidate read and carries the next generation. Any notification that arrives
            // during the read blocks on this gate; after commit it belongs to the new generation, while a rejected candidate's
            // watcher is disposed. This avoids a load-then-watch race without exposing a public prepare/commit transaction API.
            lock (_switchGate)
            {
                ThrowIfDisposed();
                string previousSourcePath = _currentSourcePath;

                // A switch to the already-active normalized path is deliberately a source-level no-op. It does not force a
                // synchronous file reread and does not replace/rearm the watcher. With reloadOnChange enabled, physical changes
                // are handled by the watcher; without it, callers should request a genuinely different source rather than use
                // TrySwitch(A) as an implicit Reload API.
                if (SourcePathsEqual(previousSourcePath, requestedSourcePath))
                {
                    result = CreateSwitchResult(
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
                    long nextGeneration = _generation + 1;
                    ActiveSourceWatcher? preparedWatcher = null;

                    try
                    {
                        preparedWatcher = CreateWatcher(requestedSourcePath, nextGeneration);
                        IDictionary<string, string?> candidateData = JsonConfigurationSnapshotLoader.Load(requestedSourcePath);
                        bool configurationChanged = !ConfigurationDataEquals(Data, candidateData);

                        ActiveSourceWatcher? previousWatcher = _activeWatcher;

                        // Source identity and effective configuration are intentionally separate lifecycles. When B publishes the
                        // same normalized key/value set as A, B still becomes CurrentSource and receives the watcher, but the live
                        // Data reference is left untouched and no IConfiguration reload token is fired. Formatting, property order,
                        // timestamps, and source path therefore cannot create a false configuration change.
                        if (configurationChanged)
                        {
                            Data = candidateData;
                        }

                        _currentSourcePath = requestedSourcePath;
                        _generation = nextGeneration;
                        _activeWatcher = preparedWatcher;
                        preparedWatcher = null;
                        previousWatcher?.Dispose();
                        raiseConfigurationReload = configurationChanged;

                        result = CreateSwitchResult(
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
                        preparedWatcher?.Dispose();
                        result = CreateSwitchResult(
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

                // Keep IConfiguration notification inside the serialized operation so a later switch/watcher reload cannot commit
                // another snapshot before consumers receive this snapshot's reload signal. Configuration change-token callbacks
                // are synchronous; Monitor locks are re-entrant for same-thread reads. A future split operation/state gate could
                // avoid holding this gate across callbacks, but would require additional ordering machinery between commits.
                // Provider lifecycle callbacks are deliberately different and run outside the gate below.
                if (raiseConfigurationReload)
                {
                    OnReload();
                }
            }

            // Lifecycle observers are invoked after leaving the provider-state gate so arbitrary consumer code never runs while
            // the provider lock is held. A concurrent later switch can therefore commit before an earlier observer callback gets
            // CPU time; the immutable event payload describes the completed operation and is authoritative for that observation.
            // A future serialized notification dispatcher could provide total callback ordering if a concrete consumer requires it.
            PublishLifecycle(CreateLifecycleEvent(result));

            if (result.Status == SwitchableJsonSwitchStatus.Rejected &&
                _runtimeFailurePolicy == SwitchableJsonRuntimeFailurePolicy.Throw &&
                result.Exception is not null)
            {
                ExceptionDispatchInfo.Capture(result.Exception).Throw();
            }

            return result;
        }

        public void Dispose()
        {
            ActiveSourceWatcher? watcher;

            lock (_switchGate)
            {
                if (_disposed)
                {
                    return;
                }

                // Invalidate generation before detaching the watcher. A Timer callback that was already queued can still enter the
                // provider after Dispose starts, but it will see _disposed (and a generation mismatch) and return without touching
                // Data or emitting lifecycle notifications.
                _disposed = true;
                _generation++;
                watcher = _activeWatcher;
                _activeWatcher = null;
            }

            watcher?.Dispose();
        }

        private void HandleActiveSourceChanged(long watcherGeneration, string watcherSourcePath)
        {
            SwitchableJsonConfigurationEventArgs? lifecycleEvent = null;

            lock (_switchGate)
            {
                if (_disposed ||
                    watcherGeneration != _generation ||
                    !SourcePathsEqual(watcherSourcePath, _currentSourcePath))
                {
                    return;
                }

                string currentSourcePath = _currentSourcePath;

                try
                {
                    IDictionary<string, string?> candidateData = JsonConfigurationSnapshotLoader.Load(currentSourcePath);
                    bool configurationChanged = !ConfigurationDataEquals(Data, candidateData);

                    // A physical file notification is only evidence that the source should be re-evaluated; it is not itself a
                    // configuration change. Publish a new Data snapshot and IConfiguration reload only when the parsed key/value
                    // set differs. Even a logically identical rewrite still produces the lifecycle event below for audit/metrics.
                    if (configurationChanged)
                    {
                        Data = candidateData;
                        OnReload();
                    }

                    lifecycleEvent = CreateLifecycleEvent(
                        SwitchableJsonConfigurationEventKind.ActiveSourceReloaded,
                        currentSourcePath,
                        currentSourcePath,
                        currentSourcePath,
                        sourceChanged: false,
                        configurationChanged,
                        SwitchableJsonFailureKind.None,
                        exception: null);
                }
                catch (Exception exception) when (IsCandidateLoadFailure(exception))
                {
                    // File-watcher reloads have no synchronous caller to receive Throw-policy exceptions. Last-known-good data
                    // therefore remains active and the failure is surfaced exclusively through the provider lifecycle channel.
                    lifecycleEvent = CreateLifecycleEvent(
                        SwitchableJsonConfigurationEventKind.ActiveSourceReloadRejected,
                        currentSourcePath,
                        currentSourcePath,
                        currentSourcePath,
                        sourceChanged: false,
                        configurationChanged: false,
                        ClassifyFailure(exception),
                        exception);
                }
            }

            if (lifecycleEvent is not null)
            {
                PublishLifecycle(lifecycleEvent);
            }
        }

        private IDictionary<string, string?> LoadInitialData(string sourcePath)
        {
            try
            {
                return JsonConfigurationSnapshotLoader.Load(sourcePath);
            }
            catch (Exception exception) when (
                _optionalInitialSource && IsSourceNotFound(exception))
            {
                return new Dictionary<string, string?>(ConfigurationKeyComparer);
            }
        }

        private ActiveSourceWatcher? CreateWatcher(string sourcePath, long generation)
        {
            return _reloadOnChange
                ? ActiveSourceWatcher.Create(
                    sourcePath,
                    generation,
                    _reloadDelayMilliseconds,
                    HandleActiveSourceChanged)
                : null;
        }

        private string NormalizeSourcePath(string sourcePath)
        {
            return Path.IsPathFullyQualified(sourcePath)
                ? Path.GetFullPath(sourcePath)
                : Path.GetFullPath(Path.Combine(_contentRootPath, sourcePath));
        }

        private static bool SourcePathsEqual(string left, string right)
        {
            // Source identity is the normalized path string, not physical-file identity. Symlinks/hard links that reach the same
            // file through different paths are therefore distinct source identities. Windows path casing is folded for its normal
            // file-system semantics; Unix-like platforms remain ordinal because distinct case-sensitive paths can coexist.
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return string.Equals(left, right, comparison);
        }

        private static bool ConfigurationDataEquals(
            IDictionary<string, string?> current,
            IDictionary<string, string?> candidate)
        {
            // JsonConfigurationProvider already flattens JSON into configuration keys. Comparing those published dictionaries is
            // therefore the semantic comparison we want: property order/whitespace disappear, keys follow IConfiguration's
            // case-insensitive behavior, while values remain ordinal strings (including null versus non-null).
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

        private SwitchableJsonSwitchResult CreateSwitchResult(
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

        private SwitchableJsonConfigurationEventArgs CreateLifecycleEvent(SwitchableJsonSwitchResult result)
        {
            SwitchableJsonConfigurationEventKind kind = result.Status switch
            {
                SwitchableJsonSwitchStatus.Succeeded => SwitchableJsonConfigurationEventKind.SwitchSucceeded,
                SwitchableJsonSwitchStatus.AlreadyCurrent => SwitchableJsonConfigurationEventKind.SwitchAlreadyCurrent,
                SwitchableJsonSwitchStatus.Rejected => SwitchableJsonConfigurationEventKind.SwitchRejected,
                _ => throw new InvalidOperationException($"Unsupported switch status '{result.Status}'."),
            };

            return new SwitchableJsonConfigurationEventArgs(
                kind,
                result.Name,
                result.PreviousSourcePath,
                result.RequestedSourcePath,
                result.CurrentSourcePath,
                result.SourceChanged,
                result.ConfigurationChanged,
                result.FailureKind,
                result.Exception,
                result.Timestamp);
        }

        private SwitchableJsonConfigurationEventArgs CreateLifecycleEvent(
            SwitchableJsonConfigurationEventKind kind,
            string previousSourcePath,
            string requestedSourcePath,
            string currentSourcePath,
            bool sourceChanged,
            bool configurationChanged,
            SwitchableJsonFailureKind failureKind,
            Exception? exception)
        {
            return new SwitchableJsonConfigurationEventArgs(
                kind,
                Name,
                previousSourcePath,
                requestedSourcePath,
                currentSourcePath,
                sourceChanged,
                configurationChanged,
                failureKind,
                exception,
                DateTimeOffset.UtcNow);
        }

        private void PublishLifecycle(SwitchableJsonConfigurationEventArgs eventArgs)
        {
            EventHandler<SwitchableJsonConfigurationEventArgs>? handlers = LifecycleChanged;
            if (handlers is null)
            {
                return;
            }

            // Lifecycle notifications are observations, not veto points. The provider state has already been committed (or a
            // rejection has already been classified) before this method runs. Letting one consumer exception escape would make
            // a successful manual switch appear to fail; on the Timer/ThreadPool watcher path it could also become an unhandled
            // background exception. Invoke subscribers independently so one broken logger/metrics/audit consumer neither changes
            // provider semantics nor prevents later observers from seeing the same outcome. There is deliberately no internal
            // logging dependency here; observers that need error reporting are responsible for handling their own exceptions.
            foreach (Delegate subscriber in handlers.GetInvocationList())
            {
                try
                {
                    ((EventHandler<SwitchableJsonConfigurationEventArgs>)subscriber)(this, eventArgs);
                }
                catch (Exception)
                {
                    // Intentionally isolated; see the lifecycle contract and comment above.
                }
            }
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
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
                // Read the candidate independently from the active provider. FileShare.ReadWrite|Delete tolerates common atomic
                // replace/save patterns; if a writer exposes malformed intermediate JSON, parsing fails and the caller's LKG path
                // keeps the previously published snapshot. Manual switches intentionally do not add watcher debounce before this IO.
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
                    // Reuse Microsoft's JsonConfigurationProvider parser in an isolated temporary provider instead of maintaining
                    // a second JSON-to-IConfiguration flattening implementation. This temporary provider has no file watcher and
                    // is never attached to IConfiguration; only its completed Data dictionary becomes a prepared candidate.
                    base.Load(stream);
                    return new Dictionary<string, string?>(Data, ConfigurationKeyComparer);
                }
            }
        }
    }
}
