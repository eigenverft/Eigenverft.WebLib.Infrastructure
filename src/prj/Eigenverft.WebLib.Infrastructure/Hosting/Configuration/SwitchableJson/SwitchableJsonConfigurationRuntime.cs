using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;

using Microsoft.Extensions.Configuration.Json;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson
{
    /// <summary>
    /// Stable runtime handle shared by every concrete provider instance built for one switchable configuration source.
    /// </summary>
    /// <remarks>
    /// ConfigurationManager may rebuild provider instances when its Sources collection changes. This object deliberately owns
    /// everything that must survive such a rebuild: current source identity, watcher generation, lifecycle observers and the
    /// DI-facing runtime API. A concrete provider owns only the Data snapshot and IConfiguration reload token for its current
    /// framework lifetime.
    /// </remarks>
    internal sealed class SwitchableJsonConfigurationRuntime : ISwitchableJsonConfiguration, IDisposable
    {
        internal static readonly StringComparer ConfigurationKeyComparer = StringComparer.OrdinalIgnoreCase;

        // Source operations are serialized here, but IConfiguration reads are not. Candidate IO can therefore take place while
        // consumers continue reading the previously published provider snapshot. Only the short provider Data replacement is
        // synchronized by the provider's own data gate. This avoids running arbitrary change-token consumer code under this lock.
        private readonly object _operationGate = new();
        private readonly string _contentRootPath;
        private readonly bool _optionalInitialSource;
        private readonly bool _reloadOnChange;
        private readonly int _reloadDelayMilliseconds;
        private readonly SwitchableJsonRuntimeFailurePolicy _runtimeFailurePolicy;
        private string _currentSourcePath;
        private long _generation;
        private ActiveSourceWatcher? _activeWatcher;
        private SwitchableJsonConfigurationProvider? _activeProvider;
        private bool _disposed;

        public SwitchableJsonConfigurationRuntime(
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
                lock (_operationGate)
                {
                    ThrowIfDisposed();
                    return _currentSourcePath;
                }
            }
        }

        public event EventHandler<SwitchableJsonConfigurationEventArgs>? LifecycleChanged;

        public SwitchableJsonSwitchResult TrySwitch(string sourcePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

            string requestedSourcePath = NormalizeSourcePath(sourcePath);
            SwitchableJsonSwitchResult result;
            SwitchableJsonConfigurationProvider? providerToReload = null;

            lock (_operationGate)
            {
                ThrowIfDisposed();
                SwitchableJsonConfigurationProvider provider = GetActiveProvider();
                string previousSourcePath = _currentSourcePath;

                // A switch to the already-active normalized path is a source-level no-op. It intentionally does not become an
                // implicit Reload() API. Physical changes are handled by reloadOnChange when enabled.
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
                        // Prepare the new watcher before candidate IO. A notification that arrives while the file is being read
                        // waits on this operation gate. On commit it belongs to nextGeneration; on rejection the watcher is disposed.
                        preparedWatcher = CreateWatcher(requestedSourcePath, nextGeneration);
                        IDictionary<string, string?> candidateData = JsonConfigurationSnapshotLoader.Load(requestedSourcePath);
                        bool configurationChanged = provider.CommitCandidate(candidateData);

                        ActiveSourceWatcher? previousWatcher = _activeWatcher;
                        _currentSourcePath = requestedSourcePath;
                        _generation = nextGeneration;
                        _activeWatcher = preparedWatcher;
                        preparedWatcher = null;
                        previousWatcher?.Dispose();

                        if (configurationChanged)
                        {
                            providerToReload = provider;
                        }

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
            }

            // Source state is fully committed before either notification channel runs. IConfiguration change callbacks and
            // lifecycle observers are consumer code and must never execute under the runtime operation lock.
            if (providerToReload is not null)
            {
                PublishConfigurationReload(providerToReload);
            }

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

            lock (_operationGate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _generation++;
                _activeProvider = null;
                watcher = _activeWatcher;
                _activeWatcher = null;
            }

            watcher?.Dispose();
        }

        internal void LoadProvider(SwitchableJsonConfigurationProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            lock (_operationGate)
            {
                ThrowIfDisposed();

                // A provider rebuild is a framework lifecycle operation, not a source switch. Load the currently selected source
                // into the fresh provider instance and keep source identity/watcher unchanged. This is what makes arbitrary
                // ConfigurationManager.Sources rebuilds safe while preserving the provider's original precedence position.
                ActiveSourceWatcher? preparedWatcher = null;
                bool needsWatcher = _reloadOnChange && _activeWatcher is null;
                long preparedGeneration = _generation;

                if (needsWatcher)
                {
                    preparedGeneration = _generation + 1;
                    preparedWatcher = CreateWatcher(_currentSourcePath, preparedGeneration);
                }

                try
                {
                    IDictionary<string, string?> data = LoadFrameworkData(_currentSourcePath);
                    provider.ReplaceData(data);
                }
                catch
                {
                    preparedWatcher?.Dispose();
                    throw;
                }

                // ConfigurationManager.ReloadSources builds and loads the replacement provider set before disposing the old set.
                // Activating the freshly loaded provider here means disposal of the previous provider later sees that it is stale
                // and cannot tear down the shared runtime/watcher state.
                _activeProvider = provider;

                if (preparedWatcher is not null)
                {
                    _generation = preparedGeneration;
                    _activeWatcher = preparedWatcher;
                }
            }
        }

        internal void DetachProvider(SwitchableJsonConfigurationProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);
            ActiveSourceWatcher? watcher = null;

            lock (_operationGate)
            {
                if (_disposed || !ReferenceEquals(_activeProvider, provider))
                {
                    return;
                }

                // If a replacement provider was loaded during ConfigurationManager.ReloadSources, _activeProvider already points
                // at that fresh instance and disposal of the old provider lands in the no-op branch above. Reaching this branch
                // means this runtime source itself is leaving the active configuration stack (or the host is being disposed).
                _disposed = true;
                _generation++;
                _activeProvider = null;
                watcher = _activeWatcher;
                _activeWatcher = null;
            }

            watcher?.Dispose();
        }

        internal void Set(SwitchableJsonConfigurationProvider provider, string key, string? value)
        {
            ArgumentNullException.ThrowIfNull(provider);

            lock (_operationGate)
            {
                ThrowIfDisposed();
                provider.SetCore(key, value);
            }
        }

        internal static bool ConfigurationDataEquals(
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

        private void HandleActiveSourceChanged(long watcherGeneration, string watcherSourcePath)
        {
            SwitchableJsonConfigurationEventArgs? lifecycleEvent = null;
            SwitchableJsonConfigurationProvider? providerToReload = null;

            lock (_operationGate)
            {
                if (_disposed ||
                    watcherGeneration != _generation ||
                    !SourcePathsEqual(watcherSourcePath, _currentSourcePath) ||
                    _activeProvider is null)
                {
                    return;
                }

                string currentSourcePath = _currentSourcePath;
                SwitchableJsonConfigurationProvider provider = _activeProvider;

                try
                {
                    IDictionary<string, string?> candidateData = JsonConfigurationSnapshotLoader.Load(currentSourcePath);
                    bool configurationChanged = provider.CommitCandidate(candidateData);
                    if (configurationChanged)
                    {
                        providerToReload = provider;
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
                    // Watcher reloads always preserve the last-known-good snapshot. Throw is meaningful only for a synchronous
                    // manual TrySwitch caller; there is no caller on this Timer/ThreadPool path to receive such an exception.
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

            if (providerToReload is not null)
            {
                PublishConfigurationReload(providerToReload);
            }

            if (lifecycleEvent is not null)
            {
                PublishLifecycle(lifecycleEvent);
            }
        }

        private IDictionary<string, string?> LoadFrameworkData(string sourcePath)
        {
            try
            {
                return JsonConfigurationSnapshotLoader.Load(sourcePath);
            }
            catch (Exception exception) when (_optionalInitialSource && IsSourceNotFound(exception))
            {
                // This mirrors normal Optional file-provider semantics for initial Load and explicit IConfigurationRoot.Reload().
                // Runtime watcher failures use LKG instead and therefore do not route through this method.
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

        private SwitchableJsonConfigurationProvider GetActiveProvider()
        {
            return _activeProvider ?? throw new InvalidOperationException(
                $"Switchable JSON configuration '{Name}' is not active in the IConfiguration provider stack.");
        }

        private string NormalizeSourcePath(string sourcePath)
        {
            return Path.IsPathFullyQualified(sourcePath)
                ? Path.GetFullPath(sourcePath)
                : Path.GetFullPath(Path.Combine(_contentRootPath, sourcePath));
        }

        private static bool SourcePathsEqual(string left, string right)
        {
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return string.Equals(left, right, comparison);
        }

        private void PublishConfigurationReload(SwitchableJsonConfigurationProvider provider)
        {
            try
            {
                provider.RaiseReload();
            }
            catch (Exception)
            {
                // IConfiguration change-token observers are notifications, not transaction participants. The provider snapshot
                // and source selection were committed before notification. Allowing a consumer exception to escape would make a
                // successful TrySwitch appear rejected and, on watcher callbacks, could become an unhandled background exception.
                // CancellationToken invokes registered callbacks before surfacing aggregate callback exceptions, so isolating the
                // final exception preserves notification delivery without giving observers veto power over committed state.
            }
        }

        private void PublishLifecycle(SwitchableJsonConfigurationEventArgs eventArgs)
        {
            EventHandler<SwitchableJsonConfigurationEventArgs>? handlers = LifecycleChanged;
            if (handlers is null)
            {
                return;
            }

            // Lifecycle notifications are observations, not veto points. Invoke subscribers independently so one broken
            // logging/metrics/audit consumer neither changes provider semantics nor prevents later observers seeing the outcome.
            foreach (Delegate subscriber in handlers.GetInvocationList())
            {
                try
                {
                    ((EventHandler<SwitchableJsonConfigurationEventArgs>)subscriber)(this, eventArgs);
                }
                catch (Exception)
                {
                    // Intentionally isolated; the provider has no logging-policy dependency of its own.
                }
            }
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
                    // Reuse Microsoft's JSON-to-IConfiguration flattening implementation in an isolated temporary provider.
                    base.Load(stream);
                    return new Dictionary<string, string?>(Data, ConfigurationKeyComparer);
                }
            }
        }
    }
}
