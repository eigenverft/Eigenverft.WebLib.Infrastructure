using System;
using System.Collections.Generic;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>Describes the completed outcome of loading and applying a configuration-set state file.</summary>
    public enum ConfigurationSetStateApplyStatus
    {
        /// <summary>Every requested set transition completed without rejection.</summary>
        Succeeded = 0,

        /// <summary>The document was valid, but one or more requested independent set transitions were rejected.</summary>
        CompletedWithFailures = 1,

        /// <summary>The state document could not be applied, and no set transition was attempted.</summary>
        Rejected = 2,
    }

    /// <summary>Classifies state-file failures without coupling consumers to concrete exception types.</summary>
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
    }

    /// <summary>Identifies an observable configuration-set state-store lifecycle outcome.</summary>
    public enum ConfigurationSetStateStoreEventKind
    {
        /// <summary>A canonical self-describing state file was created or refreshed.</summary>
        StateMaterialized = 0,

        /// <summary>The state file was loaded and every requested set transition completed successfully.</summary>
        StateApplied = 1,

        /// <summary>The state file was valid, but one or more requested independent set transitions were rejected.</summary>
        StateAppliedWithFailures = 2,

        /// <summary>The state file was rejected before any requested set transition was attempted.</summary>
        StateRejected = 3,
    }

    /// <summary>Contains the completed result of one state-file apply operation.</summary>
    public sealed class ConfigurationSetStateApplyResult
    {
        internal ConfigurationSetStateApplyResult(
            ConfigurationSetStateApplyStatus status,
            ConfigurationSetStateFailureKind failureKind,
            IReadOnlyList<ConfigurationSetSwitchResult> setResults,
            Exception? exception,
            long sequence,
            DateTimeOffset timestamp)
        {
            Status = status;
            FailureKind = failureKind;
            SetResults = setResults;
            Exception = exception;
            Sequence = sequence;
            Timestamp = timestamp;
        }

        /// <summary>Gets the completed apply status.</summary>
        public ConfigurationSetStateApplyStatus Status { get; }

        /// <summary>Gets the classified failure kind, or <see cref="ConfigurationSetStateFailureKind.None"/>.</summary>
        public ConfigurationSetStateFailureKind FailureKind { get; }

        /// <summary>Gets the per-set outcomes in coordinator registration order.</summary>
        public IReadOnlyList<ConfigurationSetSwitchResult> SetResults { get; }

        /// <summary>Gets the underlying load or validation exception when available.</summary>
        public Exception? Exception { get; }

        /// <summary>Gets the monotonically increasing lifecycle sequence scoped to this state store.</summary>
        public long Sequence { get; }

        /// <summary>Gets the UTC timestamp at which the apply operation completed.</summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>Gets whether the complete requested state was applied without rejection.</summary>
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

        /// <summary>Loads the current state file and applies requested values to the registered independent set coordinators.</summary>
        ConfigurationSetStateApplyResult Reload();

        /// <summary>Writes the current coordinator values and authoritative allowed-value metadata to the state file.</summary>
        void Materialize();
    }
}
