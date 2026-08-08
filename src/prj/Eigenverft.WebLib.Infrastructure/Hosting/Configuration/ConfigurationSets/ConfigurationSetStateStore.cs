using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>Owns one self-describing JSON state file for a fixed set of independent configuration-set coordinators.</summary>
    internal sealed class ConfigurationSetStateStore : IConfigurationSetStateStore, IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = true,
        };

        private readonly object _gate = new();
        private readonly IReadOnlyList<IConfigurationSetCoordinator> _coordinators;
        private readonly IReadOnlyDictionary<string, IConfigurationSetCoordinator> _coordinatorLookup;
        private readonly IReadOnlyDictionary<string, ConfigurationSetApplyMode> _applyModes;
        private readonly Dictionary<string, string> _desiredValues;
        private readonly bool _reloadOnChange;
        private readonly int _reloadDelayMilliseconds;
        private ConfigurationSetStateFileWatcher? _watcher;
        private ConfigurationSetStateApplyResult? _lastApplyResult;
        private string? _pendingInternalWatcherSuppressionContent;
        private long _sequence;
        private bool _disposed;

        public ConfigurationSetStateStore(
            string filePath,
            IReadOnlyList<IConfigurationSetCoordinator> coordinators,
            IReadOnlyDictionary<string, ConfigurationSetApplyMode> applyModes,
            bool reloadOnChange,
            int reloadDelayMilliseconds)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(coordinators);
            ArgumentNullException.ThrowIfNull(applyModes);

            if (coordinators.Count == 0)
            {
                throw new ArgumentException("At least one configuration-set coordinator is required.", nameof(coordinators));
            }

            if (reloadDelayMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(reloadDelayMilliseconds));
            }

            FilePath = Path.GetFullPath(filePath);
            _coordinators = coordinators;
            _reloadOnChange = reloadOnChange;
            _reloadDelayMilliseconds = reloadDelayMilliseconds;

            var lookup = new Dictionary<string, IConfigurationSetCoordinator>(StringComparer.Ordinal);
            var modes = new Dictionary<string, ConfigurationSetApplyMode>(StringComparer.Ordinal);
            _desiredValues = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (IConfigurationSetCoordinator coordinator in coordinators)
            {
                if (!lookup.TryAdd(coordinator.Name, coordinator))
                {
                    throw new ArgumentException(
                        $"Duplicate configuration-set coordinator name '{coordinator.Name}'.",
                        nameof(coordinators));
                }

                if (!applyModes.TryGetValue(coordinator.Name, out ConfigurationSetApplyMode applyMode) ||
                    !Enum.IsDefined(applyMode))
                {
                    throw new ArgumentException(
                        $"Configuration set '{coordinator.Name}' does not have a valid state apply mode.",
                        nameof(applyModes));
                }

                modes.Add(coordinator.Name, applyMode);
                _desiredValues.Add(coordinator.Name, coordinator.ActiveValue);
            }

            _coordinatorLookup = new ReadOnlyDictionary<string, IConfigurationSetCoordinator>(lookup);
            _applyModes = new ReadOnlyDictionary<string, ConfigurationSetApplyMode>(modes);
        }

        public string FilePath { get; }

        public event EventHandler<ConfigurationSetStateStoreEventArgs>? LifecycleChanged;

        public void Initialize()
        {
            ConfigurationSetStateApplyResult? result = null;
            var deferredSwitches = new List<ConfigurationSetDeferredSwitch>();
            InvalidOperationException? startupException = null;

            lock (_gate)
            {
                ThrowIfDisposed();

                if (File.Exists(FilePath))
                {
                    result = ReloadLocked(canonicalizeOnSuccess: true, isStartup: true, deferredSwitches);
                    _lastApplyResult = result;
                    if (!result.Succeeded)
                    {
                        startupException = new InvalidOperationException(
                            $"Configuration set state file '{FilePath}' could not be applied during startup. " +
                            $"Status: {result.Status}; failure: {result.FailureKind}.",
                            result.Exception);
                    }
                }
                else
                {
                    WriteCanonicalLocked();
                }
            }

            PublishDeferredSwitches(deferredSwitches);

            if (startupException is not null)
            {
                throw startupException;
            }
        }

        internal void StartWatching()
        {
            lock (_gate)
            {
                ThrowIfDisposed();

                if (!_reloadOnChange || _watcher is not null)
                {
                    return;
                }

                _watcher = ConfigurationSetStateFileWatcher.Create(
                    FilePath,
                    _reloadDelayMilliseconds,
                    ReloadFromWatcher);
            }
        }

        public ConfigurationSetStateStoreStatus GetStatus()
        {
            lock (_gate)
            {
                ThrowIfDisposed();

                var setStates = new List<ConfigurationSetStateStatus>(_coordinators.Count);

                foreach (IConfigurationSetCoordinator coordinator in _coordinators)
                {
                    ConfigurationSetStatus runtime = coordinator.GetStatus();
                    setStates.Add(
                        new ConfigurationSetStateStatus(
                            runtime,
                            _desiredValues[coordinator.Name],
                            _applyModes[coordinator.Name]));
                }

                return new ConfigurationSetStateStoreStatus(
                    FilePath,
                    new ReadOnlyCollection<ConfigurationSetStateStatus>(setStates),
                    _lastApplyResult);
            }
        }

        public IReadOnlyList<ConfigurationSetStateStatus> GetDesiredStateStatus()
        {
            return GetStatus().SetStates;
        }

        public ConfigurationSetStateApplyResult Reload()
        {
            ConfigurationSetStateApplyResult result;
            var deferredSwitches = new List<ConfigurationSetDeferredSwitch>();

            lock (_gate)
            {
                ThrowIfDisposed();
                result = ReloadLocked(canonicalizeOnSuccess: true, isStartup: false, deferredSwitches);
                _lastApplyResult = result;
            }

            PublishDeferredSwitches(deferredSwitches);
            PublishApplyLifecycle(result);
            return result;
        }

        public ConfigurationSetStateApplyResult TrySetDesiredValue(string setName, string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(setName);
            ArgumentException.ThrowIfNullOrWhiteSpace(value);

            ConfigurationSetStateApplyResult result;
            ConfigurationSetDeferredSwitch? coordinatorOperation = null;

            lock (_gate)
            {
                ThrowIfDisposed();

                long sequence = ++_sequence;
                DateTimeOffset timestamp = DateTimeOffset.UtcNow;

                if (!_coordinatorLookup.TryGetValue(setName, out IConfigurationSetCoordinator? coordinator))
                {
                    result = CreateRejected(
                        ConfigurationSetStateFailureKind.SetNotFound,
                        new InvalidOperationException($"Configuration set '{setName}' is not managed by this state store."),
                        sequence,
                        timestamp);
                }
                else if (!coordinator.IsAllowed(value))
                {
                    result = CreateRejected(
                        ConfigurationSetStateFailureKind.ValueNotAllowed,
                        new InvalidOperationException(
                            $"Value '{value}' is not allowed by registered configuration set '{setName}'."),
                        sequence,
                        timestamp);
                }
                else
                {
                    string previousDesiredValue = _desiredValues[setName];
                    _desiredValues[setName] = value;
                    Exception? persistenceException = null;

                    try
                    {
                        WriteCanonicalLocked();
                    }
                    catch (IOException ex)
                    {
                        persistenceException = ex;
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        persistenceException = ex;
                    }

                    if (persistenceException is not null)
                    {
                        _desiredValues[setName] = previousDesiredValue;
                        result = CreateRejected(
                            ConfigurationSetStateFailureKind.IoError,
                            persistenceException,
                            sequence,
                            timestamp);
                    }
                    else if (_applyModes[setName] == ConfigurationSetApplyMode.StartupOnly)
                    {
                        IReadOnlyList<ConfigurationSetPendingRestartChange> pending =
                            string.Equals(coordinator.ActiveValue, value, StringComparison.Ordinal)
                                ? Array.Empty<ConfigurationSetPendingRestartChange>()
                                : new ReadOnlyCollection<ConfigurationSetPendingRestartChange>(
                                    new List<ConfigurationSetPendingRestartChange>
                                    {
                                        new ConfigurationSetPendingRestartChange(
                                            setName,
                                            coordinator.ActiveValue,
                                            value,
                                            ConfigurationSetApplyMode.StartupOnly),
                                    });

                        result = new ConfigurationSetStateApplyResult(
                            ConfigurationSetStateApplyStatus.Succeeded,
                            ConfigurationSetStateFailureKind.None,
                            Array.Empty<ConfigurationSetSwitchResult>(),
                            pending,
                            exception: null,
                            sequence,
                            timestamp);
                    }
                    else
                    {
                        coordinatorOperation = GetDeferredSwitch(coordinator, value);
                        ConfigurationSetSwitchResult switchResult = coordinatorOperation.Result;
                        IReadOnlyList<ConfigurationSetSwitchResult> switchResults =
                            new ReadOnlyCollection<ConfigurationSetSwitchResult>(
                                new List<ConfigurationSetSwitchResult> { switchResult });

                        result = new ConfigurationSetStateApplyResult(
                            switchResult.Succeeded
                                ? ConfigurationSetStateApplyStatus.Succeeded
                                : ConfigurationSetStateApplyStatus.CompletedWithFailures,
                            switchResult.Succeeded
                                ? ConfigurationSetStateFailureKind.None
                                : ConfigurationSetStateFailureKind.SetSwitchRejected,
                            switchResults,
                            Array.Empty<ConfigurationSetPendingRestartChange>(),
                            exception: null,
                            sequence,
                            timestamp);
                    }
                }

                _lastApplyResult = result;
            }

            coordinatorOperation?.Publish();
            PublishDesiredValueLifecycle(result);
            return result;
        }

        public void Materialize()
        {
            long sequence;
            DateTimeOffset timestamp;

            lock (_gate)
            {
                ThrowIfDisposed();
                WriteCanonicalLocked();
                sequence = ++_sequence;
                timestamp = DateTimeOffset.UtcNow;
            }

            PublishLifecycle(
                new ConfigurationSetStateStoreEventArgs(
                    ConfigurationSetStateStoreEventKind.StateMaterialized,
                    applyResult: null,
                    FilePath,
                    sequence,
                    timestamp));
        }

        public void Dispose()
        {
            ConfigurationSetStateFileWatcher? watcher;

            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                watcher = _watcher;
                _watcher = null;
            }

            watcher?.Dispose();
        }

        private ConfigurationSetStateApplyResult ReloadLocked(
            bool canonicalizeOnSuccess,
            bool isStartup,
            List<ConfigurationSetDeferredSwitch> deferredSwitches)
        {
            long sequence = ++_sequence;
            DateTimeOffset timestamp = DateTimeOffset.UtcNow;

            if (!File.Exists(FilePath))
            {
                var exception = new FileNotFoundException("Configuration set state file does not exist.", FilePath);
                return CreateRejected(
                    ConfigurationSetStateFailureKind.IoError,
                    exception,
                    sequence,
                    timestamp);
            }

            ConfigurationSetStateDocument? document;
            try
            {
                string json = File.ReadAllText(FilePath, Encoding.UTF8);
                document = JsonSerializer.Deserialize<ConfigurationSetStateDocument>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                return CreateRejected(
                    ConfigurationSetStateFailureKind.InvalidJson,
                    ex,
                    sequence,
                    timestamp);
            }
            catch (IOException ex)
            {
                return CreateRejected(
                    ConfigurationSetStateFailureKind.IoError,
                    ex,
                    sequence,
                    timestamp);
            }
            catch (UnauthorizedAccessException ex)
            {
                return CreateRejected(
                    ConfigurationSetStateFailureKind.IoError,
                    ex,
                    sequence,
                    timestamp);
            }

            if (document?.ConfigurationSets is null)
            {
                return CreateRejected(
                    ConfigurationSetStateFailureKind.InvalidDocument,
                    new InvalidDataException("Configuration set state document must contain a 'ConfigurationSets' object."),
                    sequence,
                    timestamp);
            }

            foreach ((string name, ConfigurationSetStateEntry? entry) in document.ConfigurationSets)
            {
                if (!_coordinatorLookup.TryGetValue(name, out IConfigurationSetCoordinator? coordinator))
                {
                    return CreateRejected(
                        ConfigurationSetStateFailureKind.InvalidDocument,
                        new InvalidDataException($"Configuration set state document references unknown set '{name}'."),
                        sequence,
                        timestamp);
                }

                if (entry is null || string.IsNullOrWhiteSpace(entry.DesiredValue))
                {
                    return CreateRejected(
                        ConfigurationSetStateFailureKind.InvalidDocument,
                        new InvalidDataException($"Configuration set '{name}' must contain a non-empty 'DesiredValue'."),
                        sequence,
                        timestamp);
                }

                // AllowedValues and ApplyMode in the file are descriptive metadata only.
                // Runtime authorization and apply policy always come from registered code.
                if (!coordinator.IsAllowed(entry.DesiredValue))
                {
                    return CreateRejected(
                        ConfigurationSetStateFailureKind.ValueNotAllowed,
                        new InvalidDataException(
                            $"Value '{entry.DesiredValue}' is not allowed by registered configuration set '{name}'."),
                        sequence,
                        timestamp);
                }
            }

            foreach (IConfigurationSetCoordinator coordinator in _coordinators)
            {
                if (!document.ConfigurationSets.TryGetValue(coordinator.Name, out ConfigurationSetStateEntry? registeredEntry) ||
                    registeredEntry is null)
                {
                    return CreateRejected(
                        ConfigurationSetStateFailureKind.InvalidDocument,
                        new InvalidDataException(
                            $"Configuration set state document is missing registered set '{coordinator.Name}'."),
                        sequence,
                        timestamp);
                }
            }

            foreach ((string name, ConfigurationSetStateEntry? entry) in document.ConfigurationSets)
            {
                if (entry is not null)
                {
                    _desiredValues[name] = entry.DesiredValue!;
                }
            }

            var results = new List<ConfigurationSetSwitchResult>();
            var pendingRestartChanges = new List<ConfigurationSetPendingRestartChange>();
            bool anyFailure = false;

            foreach (IConfigurationSetCoordinator coordinator in _coordinators)
            {
                ConfigurationSetStateEntry entry = document.ConfigurationSets[coordinator.Name]!;
                string desiredValue = entry.DesiredValue!;
                ConfigurationSetApplyMode applyMode = _applyModes[coordinator.Name];

                if (!isStartup && applyMode == ConfigurationSetApplyMode.StartupOnly)
                {
                    if (!string.Equals(coordinator.ActiveValue, desiredValue, StringComparison.Ordinal))
                    {
                        pendingRestartChanges.Add(
                            new ConfigurationSetPendingRestartChange(
                                coordinator.Name,
                                coordinator.ActiveValue,
                                desiredValue,
                                applyMode));
                    }

                    continue;
                }

                ConfigurationSetDeferredSwitch deferredSwitch = GetDeferredSwitch(coordinator, desiredValue);
                deferredSwitches.Add(deferredSwitch);
                ConfigurationSetSwitchResult result = deferredSwitch.Result;
                results.Add(result);
                if (!result.Succeeded)
                {
                    anyFailure = true;
                }
            }

            var readOnlyResults = new ReadOnlyCollection<ConfigurationSetSwitchResult>(results);
            var readOnlyPendingRestartChanges =
                new ReadOnlyCollection<ConfigurationSetPendingRestartChange>(pendingRestartChanges);

            if (anyFailure)
            {
                return new ConfigurationSetStateApplyResult(
                    ConfigurationSetStateApplyStatus.CompletedWithFailures,
                    ConfigurationSetStateFailureKind.SetSwitchRejected,
                    readOnlyResults,
                    readOnlyPendingRestartChanges,
                    exception: null,
                    sequence,
                    timestamp);
            }

            if (canonicalizeOnSuccess)
            {
                try
                {
                    WriteCanonicalLocked();
                }
                catch (IOException ex)
                {
                    return new ConfigurationSetStateApplyResult(
                        ConfigurationSetStateApplyStatus.CompletedWithFailures,
                        ConfigurationSetStateFailureKind.IoError,
                        readOnlyResults,
                        readOnlyPendingRestartChanges,
                        ex,
                        sequence,
                        timestamp);
                }
                catch (UnauthorizedAccessException ex)
                {
                    return new ConfigurationSetStateApplyResult(
                        ConfigurationSetStateApplyStatus.CompletedWithFailures,
                        ConfigurationSetStateFailureKind.IoError,
                        readOnlyResults,
                        readOnlyPendingRestartChanges,
                        ex,
                        sequence,
                        timestamp);
                }
            }

            return new ConfigurationSetStateApplyResult(
                ConfigurationSetStateApplyStatus.Succeeded,
                ConfigurationSetStateFailureKind.None,
                readOnlyResults,
                readOnlyPendingRestartChanges,
                exception: null,
                sequence,
                timestamp);
        }

        private void WriteCanonicalLocked()
        {
            string? directory = Path.GetDirectoryName(FilePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException($"State file '{FilePath}' has no parent directory.");
            }

            Directory.CreateDirectory(directory);

            var sets = new Dictionary<string, ConfigurationSetStateEntry>(StringComparer.Ordinal);
            foreach (IConfigurationSetCoordinator coordinator in _coordinators)
            {
                sets.Add(
                    coordinator.Name,
                    new ConfigurationSetStateEntry
                    {
                        DesiredValue = _desiredValues[coordinator.Name],
                        AllowedValues = coordinator.AllowedValues.ToList(),
                        ApplyMode = _applyModes[coordinator.Name].ToString(),
                    });
            }

            var document = new ConfigurationSetStateDocument { ConfigurationSets = sets };
            string canonical = JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine;

            if (File.Exists(FilePath) && string.Equals(File.ReadAllText(FilePath, Encoding.UTF8), canonical, StringComparison.Ordinal))
            {
                return;
            }

            string temporaryPath = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, canonical, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.Move(temporaryPath, FilePath, overwrite: true);
                _pendingInternalWatcherSuppressionContent = canonical;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private ConfigurationSetStateApplyResult CreateRejected(
            ConfigurationSetStateFailureKind failureKind,
            Exception exception,
            long sequence,
            DateTimeOffset timestamp)
        {
            return new ConfigurationSetStateApplyResult(
                ConfigurationSetStateApplyStatus.Rejected,
                failureKind,
                Array.Empty<ConfigurationSetSwitchResult>(),
                Array.Empty<ConfigurationSetPendingRestartChange>(),
                exception,
                sequence,
                timestamp);
        }

        private void ReloadFromWatcher()
        {
            ConfigurationSetStateApplyResult result;
            var deferredSwitches = new List<ConfigurationSetDeferredSwitch>();

            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                if (_pendingInternalWatcherSuppressionContent is string internallyWrittenContent)
                {
                    _pendingInternalWatcherSuppressionContent = null;

                    try
                    {
                        if (File.Exists(FilePath) &&
                            string.Equals(
                                File.ReadAllText(FilePath, Encoding.UTF8),
                                internallyWrittenContent,
                                StringComparison.Ordinal))
                        {
                            return;
                        }
                    }
                    catch (IOException)
                    {
                        // Fall through so the normal reload path can classify and publish the I/O failure.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Fall through so the normal reload path can classify and publish the access failure.
                    }
                }

                result = ReloadLocked(canonicalizeOnSuccess: true, isStartup: false, deferredSwitches);
                _lastApplyResult = result;
            }

            PublishDeferredSwitches(deferredSwitches);
            PublishApplyLifecycle(result);
        }

        private static ConfigurationSetDeferredSwitch GetDeferredSwitch(
            IConfigurationSetCoordinator coordinator,
            string value)
        {
            if (coordinator is not ConfigurationSetCoordinator concrete)
            {
                throw new InvalidOperationException(
                    "The built-in configuration-set state store can only manage coordinators created by the library registration API.");
            }

            return concrete.TrySwitchDeferred(value);
        }

        private static void PublishDeferredSwitches(IEnumerable<ConfigurationSetDeferredSwitch> deferredSwitches)
        {
            foreach (ConfigurationSetDeferredSwitch deferredSwitch in deferredSwitches)
            {
                deferredSwitch.Publish();
            }
        }

        private void PublishDesiredValueLifecycle(ConfigurationSetStateApplyResult result)
        {
            ConfigurationSetStateStoreEventKind kind = result.Status switch
            {
                ConfigurationSetStateApplyStatus.Succeeded => ConfigurationSetStateStoreEventKind.DesiredValueUpdated,
                ConfigurationSetStateApplyStatus.CompletedWithFailures => ConfigurationSetStateStoreEventKind.DesiredValueUpdatedWithFailures,
                ConfigurationSetStateApplyStatus.Rejected => ConfigurationSetStateStoreEventKind.DesiredValueUpdateRejected,
                _ => throw new InvalidOperationException($"Unsupported state apply status '{result.Status}'."),
            };

            PublishLifecycle(
                new ConfigurationSetStateStoreEventArgs(
                    kind,
                    result,
                    FilePath,
                    result.Sequence,
                    result.Timestamp));
        }

        private void PublishApplyLifecycle(ConfigurationSetStateApplyResult result)
        {
            ConfigurationSetStateStoreEventKind kind = result.Status switch
            {
                ConfigurationSetStateApplyStatus.Succeeded => ConfigurationSetStateStoreEventKind.StateApplied,
                ConfigurationSetStateApplyStatus.CompletedWithFailures => ConfigurationSetStateStoreEventKind.StateAppliedWithFailures,
                ConfigurationSetStateApplyStatus.Rejected => ConfigurationSetStateStoreEventKind.StateRejected,
                _ => throw new InvalidOperationException($"Unsupported state apply status '{result.Status}'."),
            };

            PublishLifecycle(
                new ConfigurationSetStateStoreEventArgs(
                    kind,
                    result,
                    FilePath,
                    result.Sequence,
                    result.Timestamp));
        }

        private void PublishLifecycle(ConfigurationSetStateStoreEventArgs eventArgs)
        {
            EventHandler<ConfigurationSetStateStoreEventArgs>? handlers = LifecycleChanged;
            if (handlers is null)
            {
                return;
            }

            foreach (Delegate subscriber in handlers.GetInvocationList())
            {
                try
                {
                    ((EventHandler<ConfigurationSetStateStoreEventArgs>)subscriber)(this, eventArgs);
                }
                catch (Exception)
                {
                    // Observers are diagnostics/automation consumers, not state-store transaction participants.
                }
            }
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        private sealed class ConfigurationSetStateDocument
        {
            public Dictionary<string, ConfigurationSetStateEntry>? ConfigurationSets { get; set; }
        }

        private sealed class ConfigurationSetStateEntry
        {
            public string? DesiredValue { get; set; }

            public List<string>? AllowedValues { get; set; }

            public string? ApplyMode { get; set; }
        }
    }
}
