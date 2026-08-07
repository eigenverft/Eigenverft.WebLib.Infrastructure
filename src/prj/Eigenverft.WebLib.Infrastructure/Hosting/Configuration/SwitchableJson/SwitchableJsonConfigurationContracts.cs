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

        /// <summary>Keep the current source and snapshot, publish the lifecycle event, then throw the load failure.</summary>
        Throw = 1,
    }

    /// <summary>Describes the completed outcome of a manual source switch.</summary>
    public enum SwitchableJsonSwitchStatus
    {
        /// <summary>The requested source was loaded and became the current source.</summary>
        Succeeded = 0,

        /// <summary>The requested normalized path was already the current source.</summary>
        AlreadyCurrent = 1,

        /// <summary>The candidate could not be loaded and the current source was retained.</summary>
        Rejected = 2,
    }

    /// <summary>Classifies runtime candidate-load failures without coupling callers to concrete exception types.</summary>
    public enum SwitchableJsonFailureKind
    {
        /// <summary>No failure occurred.</summary>
        None = 0,

        /// <summary>The requested source file or one of its parent directories does not exist.</summary>
        SourceNotFound = 1,

        /// <summary>The requested source does not contain valid configuration JSON.</summary>
        InvalidJson = 2,

        /// <summary>The process is not permitted to read the requested source.</summary>
        AccessDenied = 3,

        /// <summary>An input/output error prevented the candidate from being loaded.</summary>
        IoError = 4,
    }

    /// <summary>Identifies the provider lifecycle event raised for a completed manual switch attempt.</summary>
    public enum SwitchableJsonConfigurationEventKind
    {
        /// <summary>A different source path was successfully selected.</summary>
        SwitchSucceeded = 0,

        /// <summary>The requested source was already current, so no source or configuration state changed.</summary>
        SwitchAlreadyCurrent = 1,

        /// <summary>The requested candidate was rejected and the current source remained active.</summary>
        SwitchRejected = 2,
    }

    /// <summary>Contains the completed outcome of a runtime source switch.</summary>
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

        /// <summary>Gets the underlying load exception for rejected candidates, when available.</summary>
        public Exception? Exception { get; }

        /// <summary>Gets the UTC timestamp at which the outcome was completed.</summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>Gets whether the request completed without candidate rejection.</summary>
        public bool Succeeded => Status != SwitchableJsonSwitchStatus.Rejected;
    }

    /// <summary>Provides the source lifecycle outcome independently of the normal IConfiguration reload channel.</summary>
    public sealed class SwitchableJsonConfigurationEventArgs : EventArgs
    {
        internal SwitchableJsonConfigurationEventArgs(
            SwitchableJsonConfigurationEventKind kind,
            SwitchableJsonSwitchResult result)
        {
            Kind = kind;
            Result = result;
        }

        /// <summary>Gets the lifecycle event kind.</summary>
        public SwitchableJsonConfigurationEventKind Kind { get; }

        /// <summary>Gets the completed switch outcome associated with this event.</summary>
        public SwitchableJsonSwitchResult Result { get; }
    }
}
