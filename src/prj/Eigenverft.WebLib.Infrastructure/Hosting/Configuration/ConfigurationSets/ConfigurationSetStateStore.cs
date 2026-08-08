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
        private readonly bool _reloadOnChange;
        private readonly int _reloadDelayMilliseconds;
        private ConfigurationSetStateFileWatcher? _watcher;
        private long _sequence;
        private bool _disposed;

        public ConfigurationSetStateStore(
            string filePath,
            IReadOnlyList<IConfigurationSetCoordinator> coordinators,
            bool reloadOnChange,
            int reloadDelayMilliseconds)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(coordinators);

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
            foreach (IConfigurationSetCoordinator coordinator in coordinators)
            {
                if (!lookup.TryAdd(coordinator.Name, coordinator))
                {
                    throw new ArgumentException(
                        $"Duplicate configuration-set coordinator name '{coordinator.Name}'.",
                        nameof(coordinators));
                }
            }

            _coordinatorLookup = new ReadOnlyDictionary<string, IConfigurationSetCoordinator>(lookup);
        }

        public string FilePath { get; }

        public event EventHandler<ConfigurationSetStateStoreEventArgs>? LifecycleChanged;

        public void Initialize()
        {
            lock (_gate)
            {
                ThrowIfDisposed();

                if (File.Exists(FilePath))
                {
                    ConfigurationSetStateApplyResult result = ReloadLocked(canonicalizeOnSuccess: true);
                    if (!result.Succeeded)
                    {
                        throw new InvalidOperationException(
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

        public ConfigurationSetStateApplyResult Reload()
        {
            ConfigurationSetStateApplyResult result;

            lock (_gate)
            {
                ThrowIfDisposed();
                result = ReloadLocked(canonicalizeOnSuccess: true);
            }

            PublishApplyLifecycle(result);
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

        private ConfigurationSetStateApplyResult ReloadLocked(bool canonicalizeOnSuccess)
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

            if (document?.Sets is null)
            {
                return CreateRejected(
                    ConfigurationSetStateFailureKind.InvalidDocument,
                    new InvalidDataException("Configuration set state document must contain a 'Sets' object."),
                    sequence,
                    timestamp);
            }

            foreach ((string name, ConfigurationSetStateEntry? entry) in document.Sets)
            {
                if (!_coordinatorLookup.TryGetValue(name, out IConfigurationSetCoordinator? coordinator))
                {
                    return CreateRejected(
                        ConfigurationSetStateFailureKind.InvalidDocument,
                        new InvalidDataException($"Configuration set state document references unknown set '{name}'."),
                        sequence,
                        timestamp);
                }

                if (entry is null || string.IsNullOrWhiteSpace(entry.Value))
                {
                    return CreateRejected(
                        ConfigurationSetStateFailureKind.InvalidDocument,
                        new InvalidDataException($"Configuration set '{name}' must contain a non-empty 'Value'."),
                        sequence,
                        timestamp);
                }

                // AllowedValues in the file are descriptive metadata only. Runtime authorization always comes from the coordinator.
                if (!coordinator.IsAllowed(entry.Value))
                {
                    return CreateRejected(
                        ConfigurationSetStateFailureKind.ValueNotAllowed,
                        new InvalidDataException(
                            $"Value '{entry.Value}' is not allowed by registered configuration set '{name}'."),
                        sequence,
                        timestamp);
                }
            }

            var results = new List<ConfigurationSetSwitchResult>();
            bool anyFailure = false;

            foreach (IConfigurationSetCoordinator coordinator in _coordinators)
            {
                if (!document.Sets.TryGetValue(coordinator.Name, out ConfigurationSetStateEntry? entry) || entry is null)
                {
                    continue;
                }

                ConfigurationSetSwitchResult result = coordinator.TrySwitch(entry.Value!);
                results.Add(result);
                if (!result.Succeeded)
                {
                    anyFailure = true;
                }
            }

            var readOnlyResults = new ReadOnlyCollection<ConfigurationSetSwitchResult>(results);

            if (anyFailure)
            {
                return new ConfigurationSetStateApplyResult(
                    ConfigurationSetStateApplyStatus.CompletedWithFailures,
                    ConfigurationSetStateFailureKind.SetSwitchRejected,
                    readOnlyResults,
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
                        ex,
                        sequence,
                        timestamp);
                }
            }

            return new ConfigurationSetStateApplyResult(
                ConfigurationSetStateApplyStatus.Succeeded,
                ConfigurationSetStateFailureKind.None,
                readOnlyResults,
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
                        Value = coordinator.ActiveValue,
                        AllowedValues = coordinator.AllowedValues.ToList(),
                    });
            }

            var document = new ConfigurationSetStateDocument { Sets = sets };
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
                exception,
                sequence,
                timestamp);
        }

        private void ReloadFromWatcher()
        {
            ConfigurationSetStateApplyResult result;

            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                result = ReloadLocked(canonicalizeOnSuccess: true);
            }

            PublishApplyLifecycle(result);
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
            public Dictionary<string, ConfigurationSetStateEntry>? Sets { get; set; }
        }

        private sealed class ConfigurationSetStateEntry
        {
            public string? Value { get; set; }

            public List<string>? AllowedValues { get; set; }
        }
    }
}
