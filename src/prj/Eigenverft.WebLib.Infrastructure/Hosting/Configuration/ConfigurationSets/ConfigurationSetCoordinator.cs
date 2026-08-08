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
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            ConfigurationSetSwitchResult result;

            lock (_gate)
            {
                string previousValue = _activeValue;

                if (_switchInProgress)
                {
                    result = CreateResult(
                        ConfigurationSetSwitchStatus.Rejected,
                        previousValue,
                        value,
                        previousValue,
                        valueChanged: false,
                        _isConsistent,
                        ConfigurationSetSwitchFailureKind.SwitchInProgress,
                        failedParticipantName: null,
                        exception: null);
                }
                else if (!_definition.IsAllowed(value))
                {
                    result = CreateResult(
                        ConfigurationSetSwitchStatus.Rejected,
                        previousValue,
                        value,
                        previousValue,
                        valueChanged: false,
                        _isConsistent,
                        ConfigurationSetSwitchFailureKind.ValueNotAllowed,
                        failedParticipantName: null,
                        exception: null);
                }
                else if (_isConsistent && string.Equals(previousValue, value, StringComparison.Ordinal))
                {
                    result = CreateResult(
                        ConfigurationSetSwitchStatus.AlreadyActive,
                        previousValue,
                        value,
                        previousValue,
                        valueChanged: false,
                        isConsistent: true,
                        ConfigurationSetSwitchFailureKind.None,
                        failedParticipantName: null,
                        exception: null);
                }
                else
                {
                    _switchInProgress = true;
                    try
                    {
                        result = CoordinateSwitchLocked(previousValue, value);
                    }
                    finally
                    {
                        _switchInProgress = false;
                    }
                }
            }

            PublishLifecycle(result);
            return result;
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

        private ConfigurationSetSwitchResult CoordinateSwitchLocked(string previousValue, string requestedValue)
        {
            if (_bindings.Count == 0)
            {
                _activeValue = requestedValue;
                _isConsistent = true;
                return CreateResult(
                    ConfigurationSetSwitchStatus.Succeeded,
                    previousValue,
                    requestedValue,
                    requestedValue,
                    !string.Equals(previousValue, requestedValue, StringComparison.Ordinal),
                    isConsistent: true,
                    ConfigurationSetSwitchFailureKind.None,
                    failedParticipantName: null,
                    exception: null);
            }

            bool consistencyBefore = _isConsistent;
            var preparations = new List<PreparedBinding>(_bindings.Count);

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
                        return CreateResult(
                            ConfigurationSetSwitchStatus.Rejected,
                            previousValue,
                            requestedValue,
                            previousValue,
                            valueChanged: false,
                            consistencyBefore,
                            ConfigurationSetSwitchFailureKind.ParticipantOperationFailed,
                            binding.Name,
                            exception);
                    }

                    preparations.Add(new PreparedBinding(binding, preparation));

                    if (preparation.Status == SwitchableJsonPreparationStatus.Rejected)
                    {
                        return CreateResult(
                            ConfigurationSetSwitchStatus.Rejected,
                            previousValue,
                            requestedValue,
                            previousValue,
                            valueChanged: false,
                            consistencyBefore,
                            ConfigurationSetSwitchFailureKind.ParticipantPreparationRejected,
                            binding.Name,
                            preparation.Exception);
                    }
                }

                bool anyParticipantSourceChanged = false;

                for (int index = 0; index < preparations.Count; index++)
                {
                    PreparedBinding prepared = preparations[index];
                    SwitchableJsonSwitchResult participantResult;

                    try
                    {
                        participantResult = prepared.Preparation.Commit();
                    }
                    catch (Exception exception)
                    {
                        return CompleteCommitFailureLocked(
                            previousValue,
                            requestedValue,
                            consistencyBefore,
                            anyParticipantSourceChanged,
                            prepared.Binding.Name,
                            ConfigurationSetSwitchFailureKind.ParticipantOperationFailed,
                            exception);
                    }

                    if (participantResult.Status == SwitchableJsonSwitchStatus.Rejected)
                    {
                        return CompleteCommitFailureLocked(
                            previousValue,
                            requestedValue,
                            consistencyBefore,
                            anyParticipantSourceChanged,
                            prepared.Binding.Name,
                            ConfigurationSetSwitchFailureKind.ParticipantCommitRejected,
                            participantResult.Exception);
                    }

                    anyParticipantSourceChanged |= participantResult.SourceChanged;
                }

                _activeValue = requestedValue;
                _isConsistent = true;

                return CreateResult(
                    ConfigurationSetSwitchStatus.Succeeded,
                    previousValue,
                    requestedValue,
                    requestedValue,
                    !string.Equals(previousValue, requestedValue, StringComparison.Ordinal),
                    isConsistent: true,
                    ConfigurationSetSwitchFailureKind.None,
                    failedParticipantName: null,
                    exception: null);
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
                    exception);
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
                exception);
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
            Exception? exception)
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
