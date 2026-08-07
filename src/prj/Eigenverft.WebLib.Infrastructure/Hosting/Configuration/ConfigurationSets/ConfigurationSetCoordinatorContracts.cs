using System;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>Describes the completed outcome of requesting a configuration-set value change.</summary>
    public enum ConfigurationSetSwitchStatus
    {
        /// <summary>A different allowed value became active.</summary>
        Succeeded = 0,

        /// <summary>The requested value was already active, so no set state changed.</summary>
        AlreadyActive = 1,

        /// <summary>The requested value was rejected and the existing active value was retained.</summary>
        Rejected = 2,
    }

    /// <summary>Classifies configuration-set switch failures.</summary>
    public enum ConfigurationSetSwitchFailureKind
    {
        /// <summary>No failure occurred.</summary>
        None = 0,

        /// <summary>The requested value is not part of the set's allowed values.</summary>
        ValueNotAllowed = 1,
    }

    /// <summary>Describes an observable completed configuration-set lifecycle outcome.</summary>
    public enum ConfigurationSetEventKind
    {
        /// <summary>A different allowed value became active.</summary>
        SwitchSucceeded = 0,

        /// <summary>The requested value was already active.</summary>
        SwitchAlreadyActive = 1,

        /// <summary>The requested value was rejected and the current value was retained.</summary>
        SwitchRejected = 2,
    }

    /// <summary>Contains the completed result of one configuration-set switch request.</summary>
    public sealed class ConfigurationSetSwitchResult
    {
        internal ConfigurationSetSwitchResult(
            string name,
            ConfigurationSetSwitchStatus status,
            string previousValue,
            string requestedValue,
            string activeValue,
            bool valueChanged,
            ConfigurationSetSwitchFailureKind failureKind,
            long sequence,
            DateTimeOffset timestamp)
        {
            Name = name;
            Status = status;
            PreviousValue = previousValue;
            RequestedValue = requestedValue;
            ActiveValue = activeValue;
            ValueChanged = valueChanged;
            FailureKind = failureKind;
            Sequence = sequence;
            Timestamp = timestamp;
        }

        /// <summary>Gets the caller-defined set identity.</summary>
        public string Name { get; }

        /// <summary>Gets the completed switch status.</summary>
        public ConfigurationSetSwitchStatus Status { get; }

        /// <summary>Gets the active value before the request.</summary>
        public string PreviousValue { get; }

        /// <summary>Gets the value requested by the caller.</summary>
        public string RequestedValue { get; }

        /// <summary>Gets the active value after the request completed.</summary>
        public string ActiveValue { get; }

        /// <summary>Gets whether the active set value changed.</summary>
        public bool ValueChanged { get; }

        /// <summary>Gets the classified failure kind, or <see cref="ConfigurationSetSwitchFailureKind.None"/>.</summary>
        public ConfigurationSetSwitchFailureKind FailureKind { get; }

        /// <summary>
        /// Gets the monotonically increasing lifecycle sequence scoped to this coordinator.
        /// </summary>
        public long Sequence { get; }

        /// <summary>Gets the UTC timestamp at which the outcome completed.</summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>Gets whether the request completed without rejection.</summary>
        public bool Succeeded => Status != ConfigurationSetSwitchStatus.Rejected;
    }

    /// <summary>Provides completed configuration-set lifecycle information to interested consumers.</summary>
    public sealed class ConfigurationSetEventArgs : EventArgs
    {
        internal ConfigurationSetEventArgs(ConfigurationSetEventKind kind, ConfigurationSetSwitchResult result)
        {
            Kind = kind;
            Result = result;
        }

        /// <summary>Gets the lifecycle event kind.</summary>
        public ConfigurationSetEventKind Kind { get; }

        /// <summary>Gets the completed switch result represented by this event.</summary>
        public ConfigurationSetSwitchResult Result { get; }
    }
}
