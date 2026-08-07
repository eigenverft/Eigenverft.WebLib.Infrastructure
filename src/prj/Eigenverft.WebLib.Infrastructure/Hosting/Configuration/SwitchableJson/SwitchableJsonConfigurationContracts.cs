using System;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson
{
    /// <summary>Controls how a failed runtime candidate load affects the currently active source.</summary>
    /// <remarks>
    /// V1 intentionally omits a Clear policy. A failed candidate never implicitly becomes active; callers that want an empty
    /// configuration can switch to a valid empty JSON document instead.
    /// </remarks>
    public enum SwitchableJsonRuntimeFailurePolicy
    {
        /// <summary>Keep the current source and its last successfully published configuration snapshot.</summary>
        KeepLastKnownGood = 0,

        /// <summary>
        /// Keep the current source and snapshot, publish the lifecycle event, then throw a manual switch load failure.
        /// </summary>
        /// <remarks>
        /// Background watcher reloads have no synchronous caller to receive an exception. They therefore retain the last known
        /// good snapshot and publish <see cref="SwitchableJsonConfigurationEventKind.ActiveSourceReloadRejected"/> instead.
        /// </remarks>
        Throw = 1,
    }

    /// <summary>Describes the completed outcome of preparing a source switch without publishing it.</summary>
    public enum SwitchableJsonPreparationStatus
    {
        /// <summary>A different source was loaded successfully and is ready for a later commit.</summary>
        Prepared = 0,

        /// <summary>The requested normalized path was already the current source when preparation occurred.</summary>
        AlreadyCurrent = 1,

        /// <summary>The candidate could not be prepared; the active provider state was not changed.</summary>
        Rejected = 2,
    }

    /// <summary>Describes the completed outcome of a manual source switch.</summary>
    public enum SwitchableJsonSwitchStatus
    {
        /// <summary>The requested source was loaded and became the current source.</summary>
        Succeeded = 0,

        /// <summary>The requested normalized path was already the current source.</summary>
        AlreadyCurrent = 1,

        /// <summary>The requested switch could not be committed and the current source was retained.</summary>
        Rejected = 2,
    }

    /// <summary>Classifies source preparation/switch failures without coupling callers to concrete exception types.</summary>
    public enum SwitchableJsonFailureKind
    {
        /// <summary>No failure occurred.</summary>
        None = 0,

        /// <summary>The source file or one of its parent directories does not exist.</summary>
        SourceNotFound = 1,

        /// <summary>The source does not contain valid configuration JSON.</summary>
        InvalidJson = 2,

        /// <summary>The process is not permitted to read the source.</summary>
        AccessDenied = 3,

        /// <summary>An input/output error prevented the source from being loaded.</summary>
        IoError = 4,

        /// <summary>
        /// A previously successful preparation no longer matches the provider state against which it was prepared.
        /// </summary>
        StalePreparation = 5,
    }

    /// <summary>Identifies an observable provider/source lifecycle outcome.</summary>
    public enum SwitchableJsonConfigurationEventKind
    {
        /// <summary>A different source path was successfully selected.</summary>
        SwitchSucceeded = 0,

        /// <summary>The requested source was already current, so no source or configuration state changed.</summary>
        SwitchAlreadyCurrent = 1,

        /// <summary>The requested candidate was rejected and the current source remained active.</summary>
        SwitchRejected = 2,

        /// <summary>The active source changed on disk and was successfully loaded, whether or not effective data changed.</summary>
        ActiveSourceReloaded = 3,

        /// <summary>The active source changed on disk but the new candidate was rejected; last-known-good data remained active.</summary>
        ActiveSourceReloadRejected = 4,
    }

    /// <summary>Contains the completed outcome of a manual runtime source switch.</summary>
    public sealed class SwitchableJsonSwitchResult
    {
        internal SwitchableJsonSwitchResult(
            string name,
            SwitchableJsonSwitchStatus status,
            string previousSourcePath,
            string requestedSourcePath,
            string currentSourcePath,
            bool sourceChanged,
            bool configurationChanged,
            SwitchableJsonFailureKind failureKind,
            Exception? exception,
            DateTimeOffset timestamp)
        {
            Name = name;
            Status = status;
            PreviousSourcePath = previousSourcePath;
            RequestedSourcePath = requestedSourcePath;
            CurrentSourcePath = currentSourcePath;
            SourceChanged = sourceChanged;
            ConfigurationChanged = configurationChanged;
            FailureKind = failureKind;
            Exception = exception;
            Timestamp = timestamp;
        }

        /// <summary>Gets the caller-defined provider identity.</summary>
        public string Name { get; }

        /// <summary>Gets the completed switch status.</summary>
        public SwitchableJsonSwitchStatus Status { get; }

        /// <summary>Gets the source path that was active before the request.</summary>
        public string PreviousSourcePath { get; }

        /// <summary>Gets the normalized source path requested by the caller.</summary>
        public string RequestedSourcePath { get; }

        /// <summary>Gets the source path active after the request completed.</summary>
        public string CurrentSourcePath { get; }

        /// <summary>Gets whether the active source path changed.</summary>
        public bool SourceChanged { get; }

        /// <summary>Gets whether the provider's effective key/value snapshot changed.</summary>
        public bool ConfigurationChanged { get; }

        /// <summary>Gets the classified failure kind, or <see cref="SwitchableJsonFailureKind.None"/>.</summary>
        public SwitchableJsonFailureKind FailureKind { get; }

        /// <summary>Gets the underlying failure exception for rejected switches, when available.</summary>
        public Exception? Exception { get; }

        /// <summary>Gets the UTC timestamp at which the outcome was completed.</summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>Gets whether the request completed without candidate rejection.</summary>
        public bool Succeeded => Status != SwitchableJsonSwitchStatus.Rejected;
    }

    /// <summary>Provides source lifecycle information independently of the normal IConfiguration reload channel.</summary>
    public sealed class SwitchableJsonConfigurationEventArgs : EventArgs
    {
        internal SwitchableJsonConfigurationEventArgs(
            SwitchableJsonConfigurationEventKind kind,
            string name,
            string previousSourcePath,
            string requestedSourcePath,
            string currentSourcePath,
            bool sourceChanged,
            bool configurationChanged,
            SwitchableJsonFailureKind failureKind,
            Exception? exception,
            DateTimeOffset timestamp)
        {
            Kind = kind;
            Name = name;
            PreviousSourcePath = previousSourcePath;
            RequestedSourcePath = requestedSourcePath;
            CurrentSourcePath = currentSourcePath;
            SourceChanged = sourceChanged;
            ConfigurationChanged = configurationChanged;
            FailureKind = failureKind;
            Exception = exception;
            Timestamp = timestamp;
        }

        /// <summary>Gets the lifecycle event kind.</summary>
        public SwitchableJsonConfigurationEventKind Kind { get; }

        /// <summary>Gets the caller-defined provider identity.</summary>
        public string Name { get; }

        /// <summary>Gets the source path that was active before the operation.</summary>
        public string PreviousSourcePath { get; }

        /// <summary>
        /// Gets the source path involved in the operation. For active-source reloads this equals the current source path.
        /// </summary>
        public string RequestedSourcePath { get; }

        /// <summary>Gets the active source path after the operation.</summary>
        public string CurrentSourcePath { get; }

        /// <summary>Gets whether the active source identity changed.</summary>
        public bool SourceChanged { get; }

        /// <summary>Gets whether the effective key/value snapshot changed.</summary>
        public bool ConfigurationChanged { get; }

        /// <summary>Gets the classified failure kind, or <see cref="SwitchableJsonFailureKind.None"/>.</summary>
        public SwitchableJsonFailureKind FailureKind { get; }

        /// <summary>Gets the underlying load exception for rejected operations, when available.</summary>
        public Exception? Exception { get; }

        /// <summary>Gets the UTC timestamp at which the lifecycle outcome completed.</summary>
        public DateTimeOffset Timestamp { get; }
    }
}
