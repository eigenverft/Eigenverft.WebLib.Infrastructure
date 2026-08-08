using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Linq;

using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.JsonSettings;

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
        private readonly IReadOnlyList<IJsonConfigurationSourcePreparation> _sourcePreparations;
        private string _currentSourcePath;

        // Watcher generation protects active callbacks after watcher replacement. State version is broader: it changes whenever
        // the provider/source state against which a prepared candidate was compared changes, including Set, framework Reload,
        // provider rebuild, effective watcher reload and any successful source switch. Prepared commits require the same version.
        private long _watcherGeneration;
        private long _stateVersion;

        // Lifecycle callbacks intentionally execute outside _operationGate, so concurrent operations can finish callback delivery
        // in a different order than they committed. Assign this sequence while still under the gate; consumers can then distinguish
        // the newer outcome without turning notification callbacks back into transaction participants.
        private long _lifecycleSequence;
        private ActiveSourceWatcher? _activeWatcher;
        private SwitchableJsonConfigurationProvider? _activeProvider;
        private string? _sourceSelectionOwner;
        private bool _disposed;

        public SwitchableJsonConfigurationRuntime(
            string name,
            string contentRootPath,
            string initialSourcePath,
            bool optionalInitialSource,
            bool reloadOnChange,
            int reloadDelayMilliseconds,
            SwitchableJsonRuntimeFailurePolicy runtimeFailurePolicy,
            IReadOnlyList<IJsonConfigurationSourcePreparation> sourcePreparations)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(initialSourcePath);
            ArgumentNullException.ThrowIfNull(sourcePreparations);

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
            _sourcePreparations = sourcePreparations.ToArray();
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

        public SwitchableJsonSwitchPreparation PrepareSwitch(string sourcePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
            string requestedSourcePath = NormalizeSourcePath(sourcePath);

            if (_sourcePreparations.Count != 0)
            {
                return PrepareSwitchWithSourcePreparations(requestedSourcePath, ownerName: null);
            }

            lock (_operationGate)
            {
                ThrowIfDisposed();
                return _sourceSelectionOwner is null
                    ? PrepareSwitchLocked(requestedSourcePath)
                    : CreateSourceSelectionOwnedPreparationLocked(requestedSourcePath);
            }
        }

        public SwitchableJsonSwitchResult TrySwitch(string sourcePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
            string requestedSourcePath = NormalizeSourcePath(sourcePath);
            CommitOutcome outcome;

            if (_sourcePreparations.Count == 0)
            {
                // The original no-preparation path deliberately keeps Prepare+Commit under one operation lock so direct manual
                // switches retain their serialized semantics.
                lock (_operationGate)
                {
                    ThrowIfDisposed();

                    if (_sourceSelectionOwner is not null)
                    {
                        outcome = CreateSourceSelectionOwnedOutcomeLocked(requestedSourcePath);
                    }
                    else
                    {
                        SwitchableJsonSwitchPreparation preparation = PrepareSwitchLocked(requestedSourcePath);
                        outcome = CommitDirectPreparationLocked(preparation);
                    }
                }
            }
            else
            {
                // User-provided preparation code must never execute under the runtime state lock. The captured preparation can
                // therefore become stale if another runtime operation commits while the external preparation code is running.
                SwitchableJsonSwitchPreparation preparation =
                    PrepareSwitchWithSourcePreparations(requestedSourcePath, ownerName: null);

                lock (_operationGate)
                {
                    ThrowIfDisposed();
                    outcome = CommitDirectPreparationLocked(preparation);
                }
            }

            PublishCommitOutcome(outcome);

            if (outcome.Result.Status == SwitchableJsonSwitchStatus.Rejected &&
                _runtimeFailurePolicy == SwitchableJsonRuntimeFailurePolicy.Throw &&
                outcome.Result.Exception is not null &&
                IsCandidateLoadFailure(outcome.Result.Exception))
            {
                ExceptionDispatchInfo.Capture(outcome.Result.Exception).Throw();
            }

            return outcome.Result;
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
                _watcherGeneration++;
                _stateVersion++;
                _activeProvider = null;
                watcher = _activeWatcher;
                _activeWatcher = null;
            }

            watcher?.Dispose();
        }

        internal void ClaimSourceSelectionOwnership(string ownerName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(ownerName);

            lock (_operationGate)
            {
                ThrowIfDisposed();

                if (_sourceSelectionOwner is not null)
                {
                    throw new InvalidOperationException(
                        $"Switchable JSON configuration '{Name}' source selection is already owned by '{_sourceSelectionOwner}'.");
                }

                _sourceSelectionOwner = ownerName;
                _stateVersion++;
            }
        }

        internal void ReleaseSourceSelectionOwnership(string ownerName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(ownerName);

            lock (_operationGate)
            {
                ThrowIfDisposed();

                if (!string.Equals(_sourceSelectionOwner, ownerName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Switchable JSON configuration '{Name}' source selection is not owned by '{ownerName}'.");
                }

                _sourceSelectionOwner = null;
                _stateVersion++;
            }
        }

        internal SwitchableJsonSwitchPreparation PrepareSwitchForOwner(string ownerName, string sourcePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(ownerName);
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
            string requestedSourcePath = NormalizeSourcePath(sourcePath);

            if (_sourcePreparations.Count != 0)
            {
                return PrepareSwitchWithSourcePreparations(requestedSourcePath, ownerName);
            }

            lock (_operationGate)
            {
                ThrowIfDisposed();

                if (!string.Equals(_sourceSelectionOwner, ownerName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Switchable JSON configuration '{Name}' source selection is not owned by '{ownerName}'.");
                }

                return PrepareSwitchLocked(requestedSourcePath);
            }
        }

        internal SwitchableJsonSwitchResult CommitPreparation(SwitchableJsonSwitchPreparation preparation)
        {
            SwitchableJsonDeferredCommit deferred = CommitPreparationDeferred(preparation);
            deferred.Publish();
            return deferred.Result;
        }

        internal SwitchableJsonDeferredCommit CommitPreparationDeferred(SwitchableJsonSwitchPreparation preparation)
        {
            ArgumentNullException.ThrowIfNull(preparation);

            if (!ReferenceEquals(preparation.Runtime, this))
            {
                throw new InvalidOperationException("The switch preparation belongs to a different runtime handle.");
            }

            CommitOutcome outcome;

            lock (_operationGate)
            {
                ThrowIfDisposed();
                outcome = CommitPreparationLocked(preparation, allowRejectedPreparation: false);
            }

            // Explicit prepared commits are result-driven for coordinators and therefore never apply Throw failure policy.
            return new SwitchableJsonDeferredCommit(this, outcome.Result, outcome.ProviderToReload);
        }

        internal void PublishDeferredCommit(
            SwitchableJsonSwitchResult result,
            SwitchableJsonConfigurationProvider? providerToReload)
        {
            ArgumentNullException.ThrowIfNull(result);
            PublishCommitOutcome(new CommitOutcome(result, providerToReload));
        }

        internal void AbortPreparationResources(SwitchableJsonSwitchPreparation preparation)
        {
            ArgumentNullException.ThrowIfNull(preparation);
            preparation.WatcherRelay?.Close();
            preparation.WatcherRelay = null;
            preparation.CandidateData = null;
            preparation.TakePreparedWatcher()?.Dispose();
        }

        internal void LoadProvider(SwitchableJsonConfigurationProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            if (_sourcePreparations.Count != 0)
            {
                LoadProviderWithSourcePreparations(provider);
                return;
            }

            lock (_operationGate)
            {
                ThrowIfDisposed();

                // A provider rebuild is a framework lifecycle operation, not a source switch. Load the currently selected source
                // into the fresh provider instance and keep source identity/watcher unchanged. This is what makes arbitrary
                // ConfigurationManager.Sources rebuilds safe while preserving the provider's original precedence position.
                ActiveSourceWatcher? preparedWatcher = null;
                bool needsWatcher = _reloadOnChange && _activeWatcher is null;
                long preparedGeneration = _watcherGeneration;

                if (needsWatcher)
                {
                    preparedGeneration = _watcherGeneration + 1;
                    preparedWatcher = CreateActiveWatcher(_currentSourcePath, preparedGeneration);
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
                // and cannot tear down the shared runtime/watcher state. Provider replacement also invalidates old preparations.
                _activeProvider = provider;
                _stateVersion++;

                if (preparedWatcher is not null)
                {
                    _watcherGeneration = preparedGeneration;
                    _activeWatcher = preparedWatcher;
                }
            }
        }

        private void LoadProviderWithSourcePreparations(SwitchableJsonConfigurationProvider provider)
        {
            while (true)
            {
                string sourcePath;
                long preparedStateVersion;
                bool needsWatcher;
                long preparedGeneration;

                lock (_operationGate)
                {
                    ThrowIfDisposed();
                    sourcePath = _currentSourcePath;
                    preparedStateVersion = _stateVersion;
                    needsWatcher = _reloadOnChange && _activeWatcher is null;
                    preparedGeneration = needsWatcher ? _watcherGeneration + 1 : _watcherGeneration;
                }

                ActiveSourceWatcher? preparedWatcher = null;
                try
                {
                    if (needsWatcher)
                    {
                        preparedWatcher = CreateActiveWatcher(sourcePath, preparedGeneration);
                    }

                    IDictionary<string, string?> data = LoadFrameworkPreparedData(sourcePath);
                    bool retry;

                    lock (_operationGate)
                    {
                        ThrowIfDisposed();
                        retry = preparedStateVersion != _stateVersion ||
                            !SourcePathsEqual(sourcePath, _currentSourcePath);

                        if (!retry)
                        {
                            provider.ReplaceData(data);
                            _activeProvider = provider;
                            _stateVersion++;

                            if (preparedWatcher is not null)
                            {
                                _watcherGeneration = preparedGeneration;
                                _activeWatcher = preparedWatcher;
                                preparedWatcher = null;
                            }
                        }
                    }

                    if (!retry)
                    {
                        return;
                    }
                }
                finally
                {
                    preparedWatcher?.Dispose();
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
                // means this concrete source currently leaves the active provider stack. The stable DI runtime itself stays alive:
                // the same source may later be re-added and Build() will bind a fresh provider to this handle. Host disposal is
                // owned separately by the DI container and calls Dispose() on the runtime handle.
                _watcherGeneration++;
                _stateVersion++;
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

                if (ReferenceEquals(_activeProvider, provider))
                {
                    _stateVersion++;
                }
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

        private SwitchableJsonSwitchPreparation CreateSourceSelectionOwnedPreparationLocked(string requestedSourcePath)
        {
            SwitchableJsonConfigurationProvider provider = GetActiveProvider();
            var exception = new InvalidOperationException(
                $"Switchable JSON configuration '{Name}' source selection is owned by '{_sourceSelectionOwner}'. " +
                "Switch through the owning coordinator instead.");

            return CreatePreparation(
                SwitchableJsonPreparationStatus.Rejected,
                _currentSourcePath,
                requestedSourcePath,
                configurationChanged: false,
                SwitchableJsonFailureKind.SourceSelectionOwned,
                exception,
                _stateVersion,
                _watcherGeneration,
                provider,
                candidateData: null,
                preparedWatcher: null,
                watcherRelay: null);
        }

        private CommitOutcome CreateSourceSelectionOwnedOutcomeLocked(string requestedSourcePath)
        {
            var exception = new InvalidOperationException(
                $"Switchable JSON configuration '{Name}' source selection is owned by '{_sourceSelectionOwner}'. " +
                "Switch through the owning coordinator instead.");

            SwitchableJsonSwitchResult rejected = CreateSwitchResult(
                SwitchableJsonSwitchStatus.Rejected,
                _currentSourcePath,
                requestedSourcePath,
                _currentSourcePath,
                sourceChanged: false,
                configurationChanged: false,
                SwitchableJsonFailureKind.SourceSelectionOwned,
                exception);

            return new CommitOutcome(rejected, ProviderToReload: null);
        }

        private SwitchableJsonSwitchPreparation PrepareSwitchWithSourcePreparations(
            string requestedSourcePath,
            string? ownerName)
        {
            SwitchableJsonConfigurationProvider provider;
            string previousSourcePath;
            long preparedStateVersion;
            long preparedWatcherGeneration;

            lock (_operationGate)
            {
                ThrowIfDisposed();

                if (ownerName is null)
                {
                    if (_sourceSelectionOwner is not null)
                    {
                        return CreateSourceSelectionOwnedPreparationLocked(requestedSourcePath);
                    }
                }
                else if (!string.Equals(_sourceSelectionOwner, ownerName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Switchable JSON configuration '{Name}' source selection is not owned by '{ownerName}'.");
                }

                provider = GetActiveProvider();
                previousSourcePath = _currentSourcePath;
                preparedStateVersion = _stateVersion;
                preparedWatcherGeneration = _watcherGeneration + 1;

                if (SourcePathsEqual(previousSourcePath, requestedSourcePath))
                {
                    return CreatePreparation(
                        SwitchableJsonPreparationStatus.AlreadyCurrent,
                        previousSourcePath,
                        requestedSourcePath,
                        configurationChanged: false,
                        SwitchableJsonFailureKind.None,
                        exception: null,
                        preparedStateVersion,
                        preparedWatcherGeneration,
                        provider,
                        candidateData: null,
                        preparedWatcher: null,
                        watcherRelay: null);
                }
            }

            ActiveSourceWatcher? preparedWatcher = null;
            PreparedSourceWatcherRelay? watcherRelay = null;

            try
            {
                if (_reloadOnChange)
                {
                    watcherRelay = new PreparedSourceWatcherRelay(HandleActiveSourceChanged);
                    preparedWatcher = ActiveSourceWatcher.Create(
                        requestedSourcePath,
                        preparedWatcherGeneration,
                        _reloadDelayMilliseconds,
                        watcherRelay.OnReload,
                        watcherRelay.ObserveSourceChanged);
                }

                IDictionary<string, string?> candidateData = LoadPreparedSnapshot(requestedSourcePath);
                bool stale;
                bool configurationChanged = false;

                lock (_operationGate)
                {
                    ThrowIfDisposed();
                    stale = preparedStateVersion != _stateVersion ||
                        !ReferenceEquals(provider, _activeProvider) ||
                        !SourcePathsEqual(previousSourcePath, _currentSourcePath);

                    if (!stale)
                    {
                        configurationChanged = !provider.IsDataEqual(candidateData);
                    }
                }

                if (stale)
                {
                    watcherRelay?.Close();
                    preparedWatcher?.Dispose();
                    var exception = new InvalidOperationException(
                        "The active provider state changed while external source preparation was running.");
                    return CreatePreparation(
                        SwitchableJsonPreparationStatus.Rejected,
                        previousSourcePath,
                        requestedSourcePath,
                        configurationChanged: false,
                        SwitchableJsonFailureKind.StalePreparation,
                        exception,
                        preparedStateVersion,
                        preparedWatcherGeneration,
                        provider,
                        candidateData: null,
                        preparedWatcher: null,
                        watcherRelay: null);
                }

                return CreatePreparation(
                    SwitchableJsonPreparationStatus.Prepared,
                    previousSourcePath,
                    requestedSourcePath,
                    configurationChanged,
                    SwitchableJsonFailureKind.None,
                    exception: null,
                    preparedStateVersion,
                    preparedWatcherGeneration,
                    provider,
                    candidateData,
                    preparedWatcher,
                    watcherRelay);
            }
            catch (Exception exception) when (IsCandidateLoadFailure(exception))
            {
                watcherRelay?.Close();
                preparedWatcher?.Dispose();

                return CreatePreparation(
                    SwitchableJsonPreparationStatus.Rejected,
                    previousSourcePath,
                    requestedSourcePath,
                    configurationChanged: false,
                    ClassifyFailure(exception),
                    exception,
                    preparedStateVersion,
                    preparedWatcherGeneration,
                    provider,
                    candidateData: null,
                    preparedWatcher: null,
                    watcherRelay: null);
            }
        }

        private CommitOutcome CommitDirectPreparationLocked(SwitchableJsonSwitchPreparation preparation)
        {
            if (!preparation.TryClaimForDirectSwitch())
            {
                throw new InvalidOperationException("Internal switch preparation was unexpectedly already consumed.");
            }

            try
            {
                return CommitPreparationLocked(preparation, allowRejectedPreparation: true);
            }
            catch
            {
                AbortPreparationResources(preparation);
                throw;
            }
        }

        private SwitchableJsonSwitchPreparation PrepareSwitchLocked(string requestedSourcePath)
        {
            SwitchableJsonConfigurationProvider provider = GetActiveProvider();
            string previousSourcePath = _currentSourcePath;
            long preparedStateVersion = _stateVersion;
            long preparedWatcherGeneration = _watcherGeneration + 1;

            if (SourcePathsEqual(previousSourcePath, requestedSourcePath))
            {
                return CreatePreparation(
                    SwitchableJsonPreparationStatus.AlreadyCurrent,
                    previousSourcePath,
                    requestedSourcePath,
                    configurationChanged: false,
                    SwitchableJsonFailureKind.None,
                    exception: null,
                    preparedStateVersion,
                    preparedWatcherGeneration,
                    provider,
                    candidateData: null,
                    preparedWatcher: null,
                    watcherRelay: null);
            }

            ActiveSourceWatcher? preparedWatcher = null;
            PreparedSourceWatcherRelay? watcherRelay = null;

            try
            {
                if (_reloadOnChange)
                {
                    watcherRelay = new PreparedSourceWatcherRelay(HandleActiveSourceChanged);
                    preparedWatcher = ActiveSourceWatcher.Create(
                        requestedSourcePath,
                        preparedWatcherGeneration,
                        _reloadDelayMilliseconds,
                        watcherRelay.OnReload,
                        watcherRelay.ObserveSourceChanged);
                }

                IDictionary<string, string?> candidateData = JsonConfigurationSnapshotLoader.Load(requestedSourcePath);
                bool configurationChanged = !provider.IsDataEqual(candidateData);

                return CreatePreparation(
                    SwitchableJsonPreparationStatus.Prepared,
                    previousSourcePath,
                    requestedSourcePath,
                    configurationChanged,
                    SwitchableJsonFailureKind.None,
                    exception: null,
                    preparedStateVersion,
                    preparedWatcherGeneration,
                    provider,
                    candidateData,
                    preparedWatcher,
                    watcherRelay);
            }
            catch (Exception exception) when (IsCandidateLoadFailure(exception))
            {
                watcherRelay?.Close();
                preparedWatcher?.Dispose();

                return CreatePreparation(
                    SwitchableJsonPreparationStatus.Rejected,
                    previousSourcePath,
                    requestedSourcePath,
                    configurationChanged: false,
                    ClassifyFailure(exception),
                    exception,
                    preparedStateVersion,
                    preparedWatcherGeneration,
                    provider,
                    candidateData: null,
                    preparedWatcher: null,
                    watcherRelay: null);
            }
        }

        private CommitOutcome CommitPreparationLocked(
            SwitchableJsonSwitchPreparation preparation,
            bool allowRejectedPreparation)
        {
            if (preparation.Status == SwitchableJsonPreparationStatus.Rejected)
            {
                if (!allowRejectedPreparation)
                {
                    throw new InvalidOperationException("A rejected switch preparation cannot be committed.");
                }

                SwitchableJsonSwitchResult rejected = CreateSwitchResult(
                    SwitchableJsonSwitchStatus.Rejected,
                    preparation.PreviousSourcePath,
                    preparation.RequestedSourcePath,
                    _currentSourcePath,
                    sourceChanged: false,
                    configurationChanged: false,
                    preparation.FailureKind,
                    preparation.Exception);

                return new CommitOutcome(rejected, ProviderToReload: null);
            }

            SwitchableJsonConfigurationProvider activeProvider = GetActiveProvider();

            if (preparation.PreparedStateVersion != _stateVersion ||
                !ReferenceEquals(preparation.Provider, activeProvider) ||
                !SourcePathsEqual(preparation.PreviousSourcePath, _currentSourcePath))
            {
                return RejectStalePreparationLocked(preparation, "The active provider state changed after the candidate was prepared.");
            }

            if (preparation.Status == SwitchableJsonPreparationStatus.AlreadyCurrent)
            {
                SwitchableJsonSwitchResult alreadyCurrent = CreateSwitchResult(
                    SwitchableJsonSwitchStatus.AlreadyCurrent,
                    _currentSourcePath,
                    preparation.RequestedSourcePath,
                    _currentSourcePath,
                    sourceChanged: false,
                    configurationChanged: false,
                    SwitchableJsonFailureKind.None,
                    exception: null);

                return new CommitOutcome(alreadyCurrent, ProviderToReload: null);
            }

            IDictionary<string, string?> candidateData = preparation.CandidateData ??
                throw new InvalidOperationException("Prepared switch candidate data is missing.");

            bool configurationChanged = false;
            ActiveSourceWatcher? previousWatcher = null;

            void CommitState()
            {
                configurationChanged = activeProvider.CommitCandidate(candidateData);
                preparation.CandidateData = null;
                previousWatcher = _activeWatcher;
                _currentSourcePath = preparation.RequestedSourcePath;
                _watcherGeneration = preparation.PreparedWatcherGeneration;
                _activeWatcher = preparation.TakePreparedWatcher();
                _stateVersion++;
            }

            if (preparation.WatcherRelay is not null)
            {
                PreparedSourceWatcherRelay relay = preparation.WatcherRelay;
                if (!relay.TryActivate(CommitState))
                {
                    return RejectStalePreparationLocked(
                        preparation,
                        "The candidate source changed after it was prepared and before commit.");
                }

                // The active watcher's delegates now own the relay. The consumed public preparation no longer needs to retain it.
                preparation.WatcherRelay = null;
            }
            else
            {
                CommitState();
            }

            previousWatcher?.Dispose();

            SwitchableJsonSwitchResult succeeded = CreateSwitchResult(
                SwitchableJsonSwitchStatus.Succeeded,
                preparation.PreviousSourcePath,
                preparation.RequestedSourcePath,
                preparation.RequestedSourcePath,
                sourceChanged: true,
                configurationChanged,
                SwitchableJsonFailureKind.None,
                exception: null);

            return new CommitOutcome(succeeded, configurationChanged ? activeProvider : null);
        }

        private CommitOutcome RejectStalePreparationLocked(
            SwitchableJsonSwitchPreparation preparation,
            string reason)
        {
            AbortPreparationResources(preparation);
            var exception = new InvalidOperationException(reason);

            SwitchableJsonSwitchResult rejected = CreateSwitchResult(
                SwitchableJsonSwitchStatus.Rejected,
                _currentSourcePath,
                preparation.RequestedSourcePath,
                _currentSourcePath,
                sourceChanged: false,
                configurationChanged: false,
                SwitchableJsonFailureKind.StalePreparation,
                exception);

            return new CommitOutcome(rejected, ProviderToReload: null);
        }

        private void HandleActiveSourceChanged(long watcherGeneration, string watcherSourcePath)
        {
            if (_sourcePreparations.Count != 0)
            {
                HandleActiveSourceChangedWithSourcePreparations(watcherGeneration, watcherSourcePath);
                return;
            }

            SwitchableJsonConfigurationEventArgs? lifecycleEvent = null;
            SwitchableJsonConfigurationProvider? providerToReload = null;

            lock (_operationGate)
            {
                if (_disposed ||
                    watcherGeneration != _watcherGeneration ||
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
                        _stateVersion++;
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
                    // one-step TrySwitch caller; there is no caller on this Timer/ThreadPool path to receive such an exception.
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

        private void HandleActiveSourceChangedWithSourcePreparations(long watcherGeneration, string watcherSourcePath)
        {
            string currentSourcePath;
            SwitchableJsonConfigurationProvider provider;
            long preparedStateVersion;

            lock (_operationGate)
            {
                if (_disposed ||
                    watcherGeneration != _watcherGeneration ||
                    !SourcePathsEqual(watcherSourcePath, _currentSourcePath) ||
                    _activeProvider is null)
                {
                    return;
                }

                currentSourcePath = _currentSourcePath;
                provider = _activeProvider;
                preparedStateVersion = _stateVersion;
            }

            IDictionary<string, string?>? candidateData = null;
            Exception? failure = null;

            try
            {
                candidateData = LoadPreparedSnapshot(currentSourcePath);
            }
            catch (Exception exception) when (IsCandidateLoadFailure(exception))
            {
                failure = exception;
            }

            SwitchableJsonConfigurationEventArgs? lifecycleEvent = null;
            SwitchableJsonConfigurationProvider? providerToReload = null;

            lock (_operationGate)
            {
                if (_disposed ||
                    watcherGeneration != _watcherGeneration ||
                    preparedStateVersion != _stateVersion ||
                    !SourcePathsEqual(currentSourcePath, _currentSourcePath) ||
                    !ReferenceEquals(provider, _activeProvider))
                {
                    return;
                }

                if (failure is null)
                {
                    bool configurationChanged = provider.CommitCandidate(candidateData!);
                    if (configurationChanged)
                    {
                        _stateVersion++;
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
                else
                {
                    lifecycleEvent = CreateLifecycleEvent(
                        SwitchableJsonConfigurationEventKind.ActiveSourceReloadRejected,
                        currentSourcePath,
                        currentSourcePath,
                        currentSourcePath,
                        sourceChanged: false,
                        configurationChanged: false,
                        ClassifyFailure(failure),
                        failure);
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

        private IDictionary<string, string?> LoadFrameworkPreparedData(string sourcePath)
        {
            try
            {
                return LoadPreparedSnapshot(sourcePath);
            }
            catch (Exception exception) when (_optionalInitialSource && IsSourceNotFound(exception))
            {
                var data = new Dictionary<string, string?>(ConfigurationKeyComparer);
                JsonConfigurationSourcePreparationPipeline.Apply(sourcePath, data, _sourcePreparations);
                return data;
            }
        }

        private IDictionary<string, string?> LoadPreparedSnapshot(string sourcePath)
        {
            IDictionary<string, string?> data = JsonConfigurationSnapshotLoader.Load(sourcePath);
            JsonConfigurationSourcePreparationPipeline.Apply(sourcePath, data, _sourcePreparations);
            return new Dictionary<string, string?>(data, ConfigurationKeyComparer);
        }

        private ActiveSourceWatcher? CreateActiveWatcher(string sourcePath, long generation)
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

        private void PublishCommitOutcome(CommitOutcome outcome)
        {
            if (outcome.ProviderToReload is not null)
            {
                PublishConfigurationReload(outcome.ProviderToReload);
            }

            PublishLifecycle(CreateLifecycleEvent(outcome.Result));
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
                // successful switch appear rejected and, on watcher callbacks, could become an unhandled background exception.
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

        private SwitchableJsonSwitchPreparation CreatePreparation(
            SwitchableJsonPreparationStatus status,
            string previousSourcePath,
            string requestedSourcePath,
            bool configurationChanged,
            SwitchableJsonFailureKind failureKind,
            Exception? exception,
            long preparedStateVersion,
            long preparedWatcherGeneration,
            SwitchableJsonConfigurationProvider provider,
            IDictionary<string, string?>? candidateData,
            ActiveSourceWatcher? preparedWatcher,
            PreparedSourceWatcherRelay? watcherRelay)
        {
            return new SwitchableJsonSwitchPreparation(
                this,
                status,
                Name,
                previousSourcePath,
                requestedSourcePath,
                configurationChanged,
                failureKind,
                exception,
                DateTimeOffset.UtcNow,
                preparedStateVersion,
                preparedWatcherGeneration,
                provider,
                candidateData,
                preparedWatcher,
                watcherRelay);
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
                NextLifecycleSequenceLocked(),
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
                result.Sequence,
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
                NextLifecycleSequenceLocked(),
                DateTimeOffset.UtcNow);
        }

        private long NextLifecycleSequenceLocked()
        {
            // Every call site represents a completed lifecycle outcome while _operationGate is held. Keep this independent from
            // _stateVersion: rejected/no-op operations are observable outcomes too, while framework state changes can intentionally
            // invalidate preparations without producing a lifecycle event.
            return ++_lifecycleSequence;
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
                IOException or
                JsonConfigurationSourcePreparationException;
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
                JsonConfigurationSourcePreparationException => SwitchableJsonFailureKind.SourcePreparationFailed,
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

        private readonly record struct CommitOutcome(
            SwitchableJsonSwitchResult Result,
            SwitchableJsonConfigurationProvider? ProviderToReload);
    }
}
