using System;
using System.Collections.Generic;

using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>Default runtime implementation for one independent named configuration-set axis.</summary>
    internal sealed class ConfigurationSetCoordinator : IConfigurationSetCoordinator
    {
        private readonly object _gate = new();
        private readonly ConfigurationSetDefinition _definition;
        private readonly List<SwitchableJsonConfigurationSetBinding> _bindings = new();
        private string _activeValue;
        private bool _isConsistent = true;
        private bool _switchInProgress;
        private long _sequence;

        public ConfigurationSetCoordinator(ConfigurationSetDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            _definition = definition;
            _activeValue = definition.InitialValue;
        }

        public string Name => _definition.Name;

        public string InitialValue => _definition.InitialValue;

        public string ActiveValue
        {
            get
            {
                lock (_gate)
                {
                    return _activeValue;
                }
            }
        }

        public bool IsConsistent
        {
            get
            {
                lock (_gate)
                {
                    return _isConsistent;
                }
            }
        }

        public IReadOnlyList<string> AllowedValues => _definition.AllowedValues;

        public IReadOnlyList<string> BoundParticipantNames
        {
            get
            {
                lock (_gate)
                {
                    string[] names = new string[_bindings.Count];
                    for (int index = 0; index < _bindings.Count; index++)
                    {
                        names[index] = _bindings[index].Name;
                    }

                    return names;
                }
            }
        }

        public event EventHandler<ConfigurationSetEventArgs>? LifecycleChanged;

        public ConfigurationSetStatus GetStatus()
        {
            lock (_gate)
            {
                string[] participantNames = new string[_bindings.Count];
                for (int index = 0; index < _bindings.Count; index++)
                {
                    participantNames[index] = _bindings[index].Name;
                }

                return new ConfigurationSetStatus(
                    Name,
                    _definition.InitialValue,
                    _activeValue,
                    _isConsistent,
                    _definition.AllowedValues,
                    Array.AsReadOnly(participantNames));
            }
        }

        public bool IsAllowed(string value)
        {
            return _definition.IsAllowed(value);
        }

        public ConfigurationSetSwitchResult TrySwitch(string value)
        {
            ConfigurationSetDeferredSwitch deferred = TrySwitchDeferred(value);
            deferred.Publish();
            return deferred.Result;
        }

        internal ConfigurationSetDeferredSwitch TrySwitchDeferred(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);

            lock (_gate)
            {
                string previousValue = _activeValue;

                if (_switchInProgress)
                {
                    return CreateDeferredSwitch(
                        CreateResult(
                            ConfigurationSetSwitchStatus.Rejected,
                            previousValue,
                            value,
                            previousValue,
                            valueChanged: false,
                            _isConsistent,
                            ConfigurationSetSwitchFailureKind.SwitchInProgress,
                            failedParticipantName: null,
                            exception: null),
                        Array.Empty<SwitchableJsonDeferredCommit>(),
                        completesSwitchInProgress: false);
                }

                if (!_definition.IsAllowed(value))
                {
                    return CreateDeferredSwitch(
                        CreateResult(
                            ConfigurationSetSwitchStatus.Rejected,
                            previousValue,
                            value,
                            previousValue,
                            valueChanged: false,
                            _isConsistent,
                            ConfigurationSetSwitchFailureKind.ValueNotAllowed,
                            failedParticipantName: null,
                            exception: null),
                        Array.Empty<SwitchableJsonDeferredCommit>(),
                        completesSwitchInProgress: false);
                }

                if (_isConsistent && string.Equals(previousValue, value, StringComparison.Ordinal))
                {
                    return CreateDeferredSwitch(
                        CreateResult(
                            ConfigurationSetSwitchStatus.AlreadyActive,
                            previousValue,
                            value,
                            previousValue,
                            valueChanged: false,
                            isConsistent: true,
                            ConfigurationSetSwitchFailureKind.None,
                            failedParticipantName: null,
                            exception: null),
                        Array.Empty<SwitchableJsonDeferredCommit>(),
                        completesSwitchInProgress: false);
                }

                _switchInProgress = true;
                try
                {
                    return CoordinateSwitchLocked(previousValue, value);
                }
                catch
                {
                    _switchInProgress = false;
                    throw;
                }
            }
        }

        internal void CompleteDeferredSwitchPublication(
            ConfigurationSetSwitchResult result,
            bool completesSwitchInProgress)
        {
            ArgumentNullException.ThrowIfNull(result);

            if (completesSwitchInProgress)
            {
                lock (_gate)
                {
                    _switchInProgress = false;
                }
            }

            PublishLifecycle(result);
        }

        internal void AddSwitchableJsonBinding(SwitchableJsonConfigurationSetBinding binding)
        {
            ArgumentNullException.ThrowIfNull(binding);

            lock (_gate)
            {
                if (_switchInProgress)
                {
                    throw new InvalidOperationException(
                        $"Configuration set '{Name}' cannot add bindings while a switch is in progress.");
                }

                if (!_isConsistent)
                {
                    throw new InvalidOperationException(
                        $"Configuration set '{Name}' is inconsistent and must be reconciled before adding bindings.");
                }

                foreach (SwitchableJsonConfigurationSetBinding existing in _bindings)
                {
                    if (string.Equals(existing.Name, binding.Name, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Configuration set '{Name}' already contains participant '{binding.Name}'.");
                    }
                }

                using SwitchableJsonSwitchPreparation alignment = binding.Prepare(_activeValue);
                if (alignment.Status == SwitchableJsonPreparationStatus.Rejected)
                {
                    throw new InvalidOperationException(
                        $"Participant '{binding.Name}' cannot represent active configuration set value '{_activeValue}'.",
                        alignment.Exception);
                }

                if (alignment.Status != SwitchableJsonPreparationStatus.AlreadyCurrent)
                {
                    throw new InvalidOperationException(
                        $"Participant '{binding.Name}' is not currently on the source mapped to configuration set " +
                        $"'{Name}' value '{_activeValue}'.");
                }

                _bindings.Add(binding);
            }
        }

        internal bool RemoveSwitchableJsonBinding(string participantName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(participantName);

            lock (_gate)
            {
                if (_switchInProgress)
                {
                    throw new InvalidOperationException(
                        $"Configuration set '{Name}' cannot remove bindings while a switch is in progress.");
                }

                for (int index = _bindings.Count - 1; index >= 0; index--)
                {
                    if (string.Equals(_bindings[index].Name, participantName, StringComparison.Ordinal))
                    {
                        _bindings.RemoveAt(index);
                        return true;
                    }
                }

                return false;
            }
        }

        private ConfigurationSetDeferredSwitch CoordinateSwitchLocked(string previousValue, string requestedValue)
        {
            if (_bindings.Count == 0)
            {
                _activeValue = requestedValue;
                _isConsistent = true;
                return CreateDeferredSwitch(
                    CreateResult(
                        ConfigurationSetSwitchStatus.Succeeded,
                        previousValue,
                        requestedValue,
                        requestedValue,
                        !string.Equals(previousValue, requestedValue, StringComparison.Ordinal),
                        isConsistent: true,
                        ConfigurationSetSwitchFailureKind.None,
                        failedParticipantName: null,
                        exception: null),
                    Array.Empty<SwitchableJsonDeferredCommit>(),
                    completesSwitchInProgress: true);
            }

            bool consistencyBefore = _isConsistent;
            var preparations = new List<PreparedBinding>(_bindings.Count);
            var deferredCommits = new List<SwitchableJsonDeferredCommit>(_bindings.Count);

            try
            {
                foreach (SwitchableJsonConfigurationSetBinding binding in _bindings)
                {
                    SwitchableJsonSwitchPreparation preparation;
                    try
                    {
                        preparation = binding.Prepare(requestedValue);
                    }
                    catch (Exception exception)
                    {
                        return CreateDeferredSwitch(
                            CreateResult(
                                ConfigurationSetSwitchStatus.Rejected,
                                previousValue,
                                requestedValue,
                                previousValue,
                                valueChanged: false,
                                consistencyBefore,
                                ConfigurationSetSwitchFailureKind.ParticipantOperationFailed,
                                binding.Name,
                                exception),
                            deferredCommits,
                            completesSwitchInProgress: true);
                    }

                    preparations.Add(new PreparedBinding(binding, preparation));

                    if (preparation.Status == SwitchableJsonPreparationStatus.Rejected)
                    {
                        return CreateDeferredSwitch(
                            CreateResult(
                                ConfigurationSetSwitchStatus.Rejected,
                                previousValue,
                                requestedValue,
                                previousValue,
                                valueChanged: false,
                                consistencyBefore,
                                ConfigurationSetSwitchFailureKind.ParticipantPreparationRejected,
                                binding.Name,
                                preparation.Exception),
                            deferredCommits,
                            completesSwitchInProgress: true);
                    }
                }

                bool anyParticipantSourceChanged = false;
                var participantResults = new List<ConfigurationSetParticipantSwitchResult>(preparations.Count);

                for (int index = 0; index < preparations.Count; index++)
                {
                    PreparedBinding prepared = preparations[index];
                    SwitchableJsonSwitchResult participantResult;

                    try
                    {
                        SwitchableJsonDeferredCommit deferredCommit = prepared.Preparation.CommitDeferred();
                        deferredCommits.Add(deferredCommit);
                        participantResult = deferredCommit.Result;
                    }
                    catch (Exception exception)
                    {
                        return CreateDeferredSwitch(
                            CompleteCommitFailureLocked(
                                previousValue,
                                requestedValue,
                                consistencyBefore,
                                anyParticipantSourceChanged,
                                participantResults,
                                prepared.Binding.Name,
                                ConfigurationSetSwitchFailureKind.ParticipantOperationFailed,
                                exception),
                            deferredCommits,
                            completesSwitchInProgress: true);
                    }

                    if (participantResult.Status == SwitchableJsonSwitchStatus.Rejected)
                    {
                        return CreateDeferredSwitch(
                            CompleteCommitFailureLocked(
                                previousValue,
                                requestedValue,
                                consistencyBefore,
                                anyParticipantSourceChanged,
                                participantResults,
                                prepared.Binding.Name,
                                ConfigurationSetSwitchFailureKind.ParticipantCommitRejected,
                                participantResult.Exception),
                            deferredCommits,
                            completesSwitchInProgress: true);
                    }

                    participantResults.Add(
                        new ConfigurationSetParticipantSwitchResult(
                            participantResult.Name,
                            participantResult.PreviousSourcePath,
                            participantResult.CurrentSourcePath,
                            participantResult.SourceChanged,
                            participantResult.ConfigurationChanged));
                    anyParticipantSourceChanged |= participantResult.SourceChanged;
                }

                _activeValue = requestedValue;
                _isConsistent = true;

                return CreateDeferredSwitch(
                    CreateResult(
                        ConfigurationSetSwitchStatus.Succeeded,
                        previousValue,
                        requestedValue,
                        requestedValue,
                        !string.Equals(previousValue, requestedValue, StringComparison.Ordinal),
                        isConsistent: true,
                        ConfigurationSetSwitchFailureKind.None,
                        failedParticipantName: null,
                        exception: null,
                        participantResults: participantResults),
                    deferredCommits,
                    completesSwitchInProgress: true);
            }
            finally
            {
                foreach (PreparedBinding prepared in preparations)
                {
                    prepared.Preparation.Dispose();
                }
            }
        }

        private ConfigurationSetSwitchResult CompleteCommitFailureLocked(
            string previousValue,
            string requestedValue,
            bool consistencyBefore,
            bool anyParticipantSourceChanged,
            IReadOnlyList<ConfigurationSetParticipantSwitchResult> participantResults,
            string failedParticipantName,
            ConfigurationSetSwitchFailureKind failureKind,
            Exception? exception)
        {
            if (anyParticipantSourceChanged)
            {
                _isConsistent = false;
                return CreateResult(
                    ConfigurationSetSwitchStatus.PartiallyCommitted,
                    previousValue,
                    requestedValue,
                    previousValue,
                    valueChanged: false,
                    isConsistent: false,
                    ConfigurationSetSwitchFailureKind.PartialCommit,
                    failedParticipantName,
                    exception,
                    participantResults);
            }

            _isConsistent = consistencyBefore;
            return CreateResult(
                ConfigurationSetSwitchStatus.Rejected,
                previousValue,
                requestedValue,
                previousValue,
                valueChanged: false,
                consistencyBefore,
                failureKind,
                failedParticipantName,
                exception,
                participantResults);
        }

        private ConfigurationSetDeferredSwitch CreateDeferredSwitch(
            ConfigurationSetSwitchResult result,
            IReadOnlyList<SwitchableJsonDeferredCommit> participantCommits,
            bool completesSwitchInProgress)
        {
            return new ConfigurationSetDeferredSwitch(this, result, participantCommits, completesSwitchInProgress);
        }

        private ConfigurationSetSwitchResult CreateResult(
            ConfigurationSetSwitchStatus status,
            string previousValue,
            string requestedValue,
            string activeValue,
            bool valueChanged,
            bool isConsistent,
            ConfigurationSetSwitchFailureKind failureKind,
            string? failedParticipantName,
            Exception? exception,
            IReadOnlyList<ConfigurationSetParticipantSwitchResult>? participantResults = null)
        {
            return new ConfigurationSetSwitchResult(
                Name,
                status,
                previousValue,
                requestedValue,
                activeValue,
                valueChanged,
                isConsistent,
                failureKind,
                failedParticipantName,
                exception,
                participantResults ?? Array.Empty<ConfigurationSetParticipantSwitchResult>(),
                ++_sequence,
                DateTimeOffset.UtcNow);
        }

        private void PublishLifecycle(ConfigurationSetSwitchResult result)
        {
            EventHandler<ConfigurationSetEventArgs>? handlers = LifecycleChanged;
            if (handlers is null)
            {
                return;
            }

            ConfigurationSetEventKind kind = result.Status switch
            {
                ConfigurationSetSwitchStatus.Succeeded => ConfigurationSetEventKind.SwitchSucceeded,
                ConfigurationSetSwitchStatus.AlreadyActive => ConfigurationSetEventKind.SwitchAlreadyActive,
                ConfigurationSetSwitchStatus.Rejected => ConfigurationSetEventKind.SwitchRejected,
                ConfigurationSetSwitchStatus.PartiallyCommitted => ConfigurationSetEventKind.SwitchPartiallyCommitted,
                _ => throw new InvalidOperationException($"Unsupported configuration set switch status '{result.Status}'."),
            };

            var eventArgs = new ConfigurationSetEventArgs(kind, result);

            foreach (Delegate subscriber in handlers.GetInvocationList())
            {
                try
                {
                    ((EventHandler<ConfigurationSetEventArgs>)subscriber)(this, eventArgs);
                }
                catch (Exception)
                {
                    // Lifecycle consumers are observations, not transaction participants.
                }
            }
        }

        private sealed record PreparedBinding(
            SwitchableJsonConfigurationSetBinding Binding,
            SwitchableJsonSwitchPreparation Preparation);
    }
}
