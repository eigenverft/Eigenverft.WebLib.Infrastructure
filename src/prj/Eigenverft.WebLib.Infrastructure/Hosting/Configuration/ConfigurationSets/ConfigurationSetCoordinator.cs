using System;
using System.Collections.Generic;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>
    /// Default runtime implementation for one independent named configuration-set axis.
    /// </summary>
    internal sealed class ConfigurationSetCoordinator : IConfigurationSetCoordinator
    {
        private readonly object _gate = new();
        private readonly ConfigurationSetDefinition _definition;
        private string _activeValue;
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

        public IReadOnlyList<string> AllowedValues => _definition.AllowedValues;

        public event EventHandler<ConfigurationSetEventArgs>? LifecycleChanged;

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

                if (!_definition.IsAllowed(value))
                {
                    result = CreateResult(
                        ConfigurationSetSwitchStatus.Rejected,
                        previousValue,
                        value,
                        previousValue,
                        valueChanged: false,
                        ConfigurationSetSwitchFailureKind.ValueNotAllowed);
                }
                else if (string.Equals(previousValue, value, StringComparison.Ordinal))
                {
                    result = CreateResult(
                        ConfigurationSetSwitchStatus.AlreadyActive,
                        previousValue,
                        value,
                        previousValue,
                        valueChanged: false,
                        ConfigurationSetSwitchFailureKind.None);
                }
                else
                {
                    _activeValue = value;
                    result = CreateResult(
                        ConfigurationSetSwitchStatus.Succeeded,
                        previousValue,
                        value,
                        value,
                        valueChanged: true,
                        ConfigurationSetSwitchFailureKind.None);
                }
            }

            PublishLifecycle(result);
            return result;
        }

        private ConfigurationSetSwitchResult CreateResult(
            ConfigurationSetSwitchStatus status,
            string previousValue,
            string requestedValue,
            string activeValue,
            bool valueChanged,
            ConfigurationSetSwitchFailureKind failureKind)
        {
            return new ConfigurationSetSwitchResult(
                Name,
                status,
                previousValue,
                requestedValue,
                activeValue,
                valueChanged,
                failureKind,
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
                _ => throw new InvalidOperationException($"Unsupported configuration set switch status '{result.Status}'."),
            };

            var eventArgs = new ConfigurationSetEventArgs(kind, result);

            // Set lifecycle callbacks are observations, not transaction participants. A logger, metrics sink, audit consumer, or
            // administrative service must not be able to roll back or reinterpret an already completed set transition.
            foreach (Delegate subscriber in handlers.GetInvocationList())
            {
                try
                {
                    ((EventHandler<ConfigurationSetEventArgs>)subscriber)(this, eventArgs);
                }
                catch (Exception)
                {
                    // Intentionally isolated. The coordinator owns no application logging or observer-failure policy.
                }
            }
        }
    }
}
