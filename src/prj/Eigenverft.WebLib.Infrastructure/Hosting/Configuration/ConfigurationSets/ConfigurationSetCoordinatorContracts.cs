using System;
using System.Collections.Generic;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>Describes the completed outcome of requesting a configuration-set value change.</summary>
    public enum ConfigurationSetSwitchStatus
    {
        /// <summary>The requested allowed value was fully coordinated across all bound participants.</summary>
        Succeeded = 0,

        /// <summary>The requested value was already active and the coordinator was already consistent.</summary>
        AlreadyActive = 1,

        /// <summary>The requested value was rejected before any bound participant changed source.</summary>
        Rejected = 2,

        /// <summary>
        /// At least one participant changed source before a later participant failed to commit, so the coordinator can no longer
        /// claim that all bindings represent one known set value.
        /// </summary>
        PartiallyCommitted = 3,
    }

    /// <summary>Classifies configuration-set switch failures.</summary>
    public enum ConfigurationSetSwitchFailureKind
    {
        /// <summary>No failure occurred.</summary>
        None = 0,

        /// <summary>The requested value is not part of the set's allowed values.</summary>
        ValueNotAllowed = 1,

        /// <summary>A bound participant could not prepare the source mapped to the requested set value.</summary>
        ParticipantPreparationRejected = 2,

        /// <summary>A prepared participant could not commit because its underlying source state changed after preparation.</summary>
        ParticipantCommitRejected = 3,

        /// <summary>An unexpected exception occurred while preparing or committing a bound participant.</summary>
        ParticipantOperationFailed = 4,

        /// <summary>
        /// At least one participant source had already changed when a later participant failed. The coordinator is marked
        /// inconsistent until a later successful switch converges every binding on one allowed set value again.
        /// </summary>
        PartialCommit = 5,

        /// <summary>A recursive switch request was rejected while this coordinator was already committing another switch.</summary>
        SwitchInProgress = 6,
    }

    /// <summary>Describes an observable completed configuration-set lifecycle outcome.</summary>
    public enum ConfigurationSetEventKind
    {
        /// <summary>The requested value was fully coordinated across all bindings.</summary>
        SwitchSucceeded = 0,

        /// <summary>The requested value was already active and the coordinator was already consistent.</summary>
        SwitchAlreadyActive = 1,

        /// <summary>The requested value was rejected without creating a partial coordinated state.</summary>
        SwitchRejected = 2,

        /// <summary>Some bindings changed source before a later participant failed to commit.</summary>
        SwitchPartiallyCommitted = 3,
    }

    /// <summary>
    /// Describes one participant commit that completed before the configuration-set operation finished.
    /// </summary>
    public sealed class ConfigurationSetParticipantSwitchResult
    {
        internal ConfigurationSetParticipantSwitchResult(
            string name,
            string previousSourcePath,
            string currentSourcePath,
            bool sourceChanged,
            bool configurationChanged)
        {
            Name = name;
            PreviousSourcePath = previousSourcePath;
            CurrentSourcePath = currentSourcePath;
            SourceChanged = sourceChanged;
            ConfigurationChanged = configurationChanged;
        }

        /// <summary>Gets the bound participant identity.</summary>
        public string Name { get; }

        /// <summary>Gets the source path active before the participant commit.</summary>
        public string PreviousSourcePath { get; }

        /// <summary>Gets the source path active after the participant commit.</summary>
        public string CurrentSourcePath { get; }

        /// <summary>Gets whether this participant selected a different source.</summary>
        public bool SourceChanged { get; }

        /// <summary>Gets whether this participant changed its effective configuration key/value snapshot.</summary>
        public bool ConfigurationChanged { get; }
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
            bool isConsistent,
            ConfigurationSetSwitchFailureKind failureKind,
            string? failedParticipantName,
            Exception? exception,
            IReadOnlyList<ConfigurationSetParticipantSwitchResult> participantResults,
            long sequence,
            DateTimeOffset timestamp)
        {
            Name = name;
            Status = status;
            PreviousValue = previousValue;
            RequestedValue = requestedValue;
            ActiveValue = activeValue;
            ValueChanged = valueChanged;
            IsConsistent = isConsistent;
            FailureKind = failureKind;
            FailedParticipantName = failedParticipantName;
            Exception = exception;
            ParticipantResults = participantResults;
            Sequence = sequence;
            Timestamp = timestamp;

            bool sourceChanged = false;
            bool configurationChanged = false;
            foreach (ConfigurationSetParticipantSwitchResult participant in participantResults)
            {
                sourceChanged |= participant.SourceChanged;
                configurationChanged |= participant.ConfigurationChanged;
            }

            SourceChanged = sourceChanged;
            ConfigurationChanged = configurationChanged;
        }

        /// <summary>Gets the caller-defined set identity.</summary>
        public string Name { get; }

        /// <summary>Gets the completed switch status.</summary>
        public ConfigurationSetSwitchStatus Status { get; }

        /// <summary>Gets the last fully coordinated value before the request.</summary>
        public string PreviousValue { get; }

        /// <summary>Gets the value requested by the caller.</summary>
        public string RequestedValue { get; }

        /// <summary>
        /// Gets the last value that was fully coordinated across all bindings. When <see cref="IsConsistent"/> is false, some
        /// participant sources may no longer correspond to this value until a later successful reconciliation.
        /// </summary>
        public string ActiveValue { get; }

        /// <summary>Gets whether the last fully coordinated set value changed.</summary>
        public bool ValueChanged { get; }

        /// <summary>Gets whether at least one committed participant selected a different source.</summary>
        public bool SourceChanged { get; }

        /// <summary>Gets whether at least one committed participant changed its effective configuration snapshot.</summary>
        public bool ConfigurationChanged { get; }

        /// <summary>
        /// Gets whether the operation changed the logical set value, at least one participant source, or effective configuration data.
        /// </summary>
        public bool HasChanges => ValueChanged || SourceChanged || ConfigurationChanged;

        /// <summary>Gets the completed participant commits in binding order.</summary>
        /// <remarks>
        /// A participant that rejects during preparation has no commit result and is represented by
        /// <see cref="FailedParticipantName"/> and <see cref="FailureKind"/> instead.
        /// </remarks>
        public IReadOnlyList<ConfigurationSetParticipantSwitchResult> ParticipantResults { get; }

        /// <summary>Gets whether all bound participants are known to represent <see cref="ActiveValue"/>.</summary>
        public bool IsConsistent { get; }

        /// <summary>Gets the classified failure kind, or <see cref="ConfigurationSetSwitchFailureKind.None"/>.</summary>
        public ConfigurationSetSwitchFailureKind FailureKind { get; }

        /// <summary>Gets the participant identity associated with a prepare/commit failure, when available.</summary>
        public string? FailedParticipantName { get; }

        /// <summary>Gets the underlying unexpected participant exception, when available.</summary>
        public Exception? Exception { get; }

        /// <summary>Gets the monotonically increasing lifecycle sequence scoped to this coordinator.</summary>
        public long Sequence { get; }

        /// <summary>Gets the UTC timestamp at which the outcome completed.</summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>Gets whether the requested set value became the fully coordinated active value.</summary>
        public bool Succeeded => Status is ConfigurationSetSwitchStatus.Succeeded or ConfigurationSetSwitchStatus.AlreadyActive;
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
