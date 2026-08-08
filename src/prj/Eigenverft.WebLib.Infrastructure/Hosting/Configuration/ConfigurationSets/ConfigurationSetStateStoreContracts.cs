using System;
using System.Collections.Generic;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>Controls when a state-file value is applied to its configuration-set coordinator.</summary>
    public enum ConfigurationSetStateApplyMode
    {
        /// <summary>State-file changes may be applied while the host is running.</summary>
        Runtime = 0,

        /// <summary>State-file changes are applied during startup and remain pending while the current host is running.</summary>
        StartupOnly = 1,
    }

    /// <summary>Describes the completed outcome of loading and applying a configuration-set state file.</summary>
    public enum ConfigurationSetStateApplyStatus
    {
        /// <summary>Every requested runtime transition completed without rejection; startup-only changes may still be pending.</summary>
        Succeeded = 0,

        /// <summary>The document was valid, but one or more requested independent set transitions were rejected.</summary>
        CompletedWithFailures = 1,

        /// <summary>The state document could not be applied, and no set transition was attempted.</summary>
        Rejected = 2,
    }

    /// <summary>Classifies state-store file, validation, and switch failures without coupling consumers to concrete exception types.</summary>
    public enum ConfigurationSetStateFailureKind
    {
        /// <summary>No failure occurred.</summary>
        None = 0,

        /// <summary>The state file does not contain valid JSON.</summary>
        InvalidJson = 1,

        /// <summary>The state document is structurally invalid or references an unknown configuration set.</summary>
        InvalidDocument = 2,

        /// <summary>A requested set value is not allowed by its registered coordinator.</summary>
        ValueNotAllowed = 3,

        /// <summary>One or more configuration-set coordinators rejected their requested transition.</summary>
        SetSwitchRejected = 4,

        /// <summary>An input/output error prevented the state document from being read or materialized.</summary>
        IoError = 5,

        /// <summary>A programmatic desired-state request references a configuration-set name not managed by this store.</summary>
        SetNotFound = 6,
    }

    /// <summary>Identifies an observable configuration-set state-store lifecycle outcome.</summary>
    public enum ConfigurationSetStateStoreEventKind
    {
        /// <summary>A canonical self-describing state file was created or refreshed.</summary>
        StateMaterialized = 0,

        /// <summary>The state file was loaded and every requested runtime transition completed successfully.</summary>
        StateApplied = 1,

        /// <summary>The state file was valid, but one or more requested independent set transitions were rejected.</summary>
        StateAppliedWithFailures = 2,

        /// <summary>The state file was rejected before any requested set transition was attempted.</summary>
        StateRejected = 3,
        /// <summary>A programmatic desired-value update was persisted and its allowed runtime action completed.</summary>
        DesiredValueUpdated = 4,

        /// <summary>A programmatic desired-value update was persisted, but its runtime transition did not fully complete.</summary>
        DesiredValueUpdatedWithFailures = 5,

        /// <summary>A programmatic desired-value update was rejected before desired state was persisted.</summary>
        DesiredValueUpdateRejected = 6,
    }

    /// <summary>Describes a desired state-file value that is intentionally waiting for the next host startup.</summary>
    public sealed class ConfigurationSetPendingRestartChange
    {
        internal ConfigurationSetPendingRestartChange(
            string name,
            string activeValue,
            string desiredValue,
            ConfigurationSetStateApplyMode applyMode)
        {
            Name = name;
            ActiveValue = activeValue;
            DesiredValue = desiredValue;
            ApplyMode = applyMode;
        }

        /// <summary>Gets the configuration-set identity.</summary>
        public string Name { get; }

        /// <summary>Gets the value active in the running process.</summary>
        public string ActiveValue { get; }

        /// <summary>Gets the desired value stored in the state file.</summary>
        public string DesiredValue { get; }

        /// <summary>Gets the code-owned state-file apply mode.</summary>
        public ConfigurationSetStateApplyMode ApplyMode { get; }
    }

    /// <summary>Contains the completed result of one state-file apply operation.</summary>
    public sealed class ConfigurationSetStateApplyResult
    {
        internal ConfigurationSetStateApplyResult(
            ConfigurationSetStateApplyStatus status,
            ConfigurationSetStateFailureKind failureKind,
            IReadOnlyList<ConfigurationSetSwitchResult> setResults,
            IReadOnlyList<ConfigurationSetPendingRestartChange> pendingRestartChanges,
            Exception? exception,
            long sequence,
            DateTimeOffset timestamp)
        {
            Status = status;
            FailureKind = failureKind;
            SetResults = setResults;
            PendingRestartChanges = pendingRestartChanges;
            Exception = exception;
            Sequence = sequence;
            Timestamp = timestamp;
        }

        /// <summary>Gets the completed apply status.</summary>
        public ConfigurationSetStateApplyStatus Status { get; }

        /// <summary>Gets the classified failure kind, or <see cref="ConfigurationSetStateFailureKind.None"/>.</summary>
        public ConfigurationSetStateFailureKind FailureKind { get; }

        /// <summary>Gets the per-set runtime outcomes in coordinator registration order.</summary>
        public IReadOnlyList<ConfigurationSetSwitchResult> SetResults { get; }

        /// <summary>Gets startup-only desired values that remain pending for the next host startup.</summary>
        public IReadOnlyList<ConfigurationSetPendingRestartChange> PendingRestartChanges { get; }

        /// <summary>Gets whether at least one startup-only set has a desired value different from its active runtime value.</summary>
        public bool HasPendingRestart => PendingRestartChanges.Count > 0;

        /// <summary>Gets the underlying load or validation exception when available.</summary>
        public Exception? Exception { get; }

        /// <summary>Gets the monotonically increasing lifecycle sequence scoped to this state store.</summary>
        public long Sequence { get; }

        /// <summary>Gets the UTC timestamp at which the apply operation completed.</summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>Gets whether the state document was accepted without a runtime transition rejection.</summary>
        public bool Succeeded => Status == ConfigurationSetStateApplyStatus.Succeeded;
    }

    /// <summary>Provides completed state-store lifecycle information to interested consumers.</summary>
    public sealed class ConfigurationSetStateStoreEventArgs : EventArgs
    {
        internal ConfigurationSetStateStoreEventArgs(
            ConfigurationSetStateStoreEventKind kind,
            ConfigurationSetStateApplyResult? applyResult,
            string filePath,
            long sequence,
            DateTimeOffset timestamp)
        {
            Kind = kind;
            ApplyResult = applyResult;
            FilePath = filePath;
            Sequence = sequence;
            Timestamp = timestamp;
        }

        /// <summary>Gets the lifecycle event kind.</summary>
        public ConfigurationSetStateStoreEventKind Kind { get; }

        /// <summary>Gets the apply result for apply/reject events, or <see langword="null"/> for pure materialization.</summary>
        public ConfigurationSetStateApplyResult? ApplyResult { get; }

        /// <summary>Gets the normalized state-file path.</summary>
        public string FilePath { get; }

        /// <summary>Gets the monotonically increasing lifecycle sequence scoped to this store.</summary>
        public long Sequence { get; }

        /// <summary>Gets the UTC timestamp at which the lifecycle outcome completed.</summary>
        public DateTimeOffset Timestamp { get; }
    }

    /// <summary>Runtime control and observation contract for a self-describing configuration-set state file.</summary>
    public interface IConfigurationSetStateStore
    {
        /// <summary>Gets the normalized path of the managed JSON state file.</summary>
        string FilePath { get; }

        /// <summary>
        /// Occurs after state materialization or a completed apply attempt. Observers are notifications and cannot veto outcomes.
        /// </summary>
        event EventHandler<ConfigurationSetStateStoreEventArgs>? LifecycleChanged;

        /// <summary>Captures the current managed state-file and coordinator runtime status.</summary>
        ConfigurationSetStateStoreStatus GetStatus();

        /// <summary>Loads the current state file and applies values permitted to change in the running host.</summary>
        ConfigurationSetStateApplyResult Reload();

        /// <summary>
        /// Persists one allowed desired value to the managed state file and then honors that set's registered apply mode.
        /// </summary>
        /// <remarks>
        /// For <see cref="ConfigurationSetStateApplyMode.Runtime"/>, runtime switching is attempted only after the desired state file
        /// was persisted successfully. If that runtime switch rejects, the persisted desired value remains and status reports
        /// desired-state drift from the last-known-good active value. For <see cref="ConfigurationSetStateApplyMode.StartupOnly"/>,
        /// the desired value is persisted and reported as pending restart without switching the running coordinator.
        /// </remarks>
        ConfigurationSetStateApplyResult TrySetDesiredValue(string setName, string value);

        /// <summary>Writes current desired values and authoritative state metadata to the state file.</summary>
        void Materialize();
    }
}
