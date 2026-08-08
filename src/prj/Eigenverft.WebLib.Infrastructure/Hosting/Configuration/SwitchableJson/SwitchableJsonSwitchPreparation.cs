using System;
using System.Collections.Generic;
using System.Threading;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson
{
    /// <summary>
    /// Represents one completed source-switch preparation that can later be committed or aborted.
    /// </summary>
    /// <remarks>
    /// A preparation is bound to the provider state that existed when it was created. Commit rejects the preparation if that
    /// state changed in the meantime. Dispose aborts an uncommitted preparation and releases any prepared watcher resources.
    /// The object is single-use: after Commit or Abort it cannot be committed again.
    /// </remarks>
    public sealed class SwitchableJsonSwitchPreparation : IDisposable
    {
        private int _claimed;

        internal SwitchableJsonSwitchPreparation(
            SwitchableJsonConfigurationRuntime runtime,
            SwitchableJsonPreparationStatus status,
            string name,
            string previousSourcePath,
            string requestedSourcePath,
            bool configurationChanged,
            SwitchableJsonFailureKind failureKind,
            Exception? exception,
            DateTimeOffset timestamp,
            long preparedStateVersion,
            long preparedWatcherGeneration,
            SwitchableJsonConfigurationProvider? provider,
            IDictionary<string, string?>? candidateData,
            ActiveSourceWatcher? preparedWatcher,
            PreparedSourceWatcherRelay? watcherRelay)
        {
            Runtime = runtime;
            Status = status;
            Name = name;
            PreviousSourcePath = previousSourcePath;
            RequestedSourcePath = requestedSourcePath;
            ConfigurationChanged = configurationChanged;
            FailureKind = failureKind;
            Exception = exception;
            Timestamp = timestamp;
            PreparedStateVersion = preparedStateVersion;
            PreparedWatcherGeneration = preparedWatcherGeneration;
            Provider = provider;
            CandidateData = candidateData;
            PreparedWatcher = preparedWatcher;
            WatcherRelay = watcherRelay;
        }

        /// <summary>Gets the caller-defined provider identity.</summary>
        public string Name { get; }

        /// <summary>Gets the completed prepare outcome.</summary>
        public SwitchableJsonPreparationStatus Status { get; }

        /// <summary>Gets the source path that was active when preparation started.</summary>
        public string PreviousSourcePath { get; }

        /// <summary>Gets the normalized candidate source path.</summary>
        public string RequestedSourcePath { get; }

        /// <summary>Gets whether the prepared candidate differs from the effective provider snapshot it was compared against.</summary>
        public bool ConfigurationChanged { get; }

        /// <summary>Gets the classified prepare failure, or <see cref="SwitchableJsonFailureKind.None"/>.</summary>
        public SwitchableJsonFailureKind FailureKind { get; }

        /// <summary>Gets the underlying prepare exception for rejected preparations, when available.</summary>
        public Exception? Exception { get; }

        /// <summary>Gets the UTC timestamp at which preparation completed.</summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>Gets whether this preparation completed successfully and is eligible for Commit.</summary>
        public bool CanCommit =>
            Status != SwitchableJsonPreparationStatus.Rejected && Volatile.Read(ref _claimed) == 0;

        /// <summary>
        /// Atomically publishes this preparation if the provider still matches the state against which it was prepared.
        /// </summary>
        /// <returns>The completed source-switch result.</returns>
        /// <remarks>
        /// A stale preparation returns a rejected switch result with
        /// <see cref="SwitchableJsonFailureKind.StalePreparation"/>. Prepared Commit is intentionally result-driven and does not
        /// apply <see cref="SwitchableJsonRuntimeFailurePolicy.Throw"/>; that policy remains part of the one-step TrySwitch API.
        /// </remarks>
        public SwitchableJsonSwitchResult Commit()
        {
            SwitchableJsonDeferredCommit deferred = CommitDeferred();
            deferred.Publish();
            return deferred.Result;
        }

        internal SwitchableJsonDeferredCommit CommitDeferred()
        {
            if (Status == SwitchableJsonPreparationStatus.Rejected)
            {
                throw new InvalidOperationException("A rejected switch preparation cannot be committed.");
            }

            if (Interlocked.CompareExchange(ref _claimed, 1, 0) != 0)
            {
                throw new InvalidOperationException("This switch preparation has already been committed or aborted.");
            }

            try
            {
                return Runtime.CommitPreparationDeferred(this);
            }
            catch
            {
                Runtime.AbortPreparationResources(this);
                throw;
            }
        }

        /// <summary>Discards this preparation without changing the active provider state.</summary>
        /// <remarks>Abort is idempotent and emits no lifecycle or IConfiguration reload signal.</remarks>
        public void Abort()
        {
            if (Interlocked.CompareExchange(ref _claimed, 1, 0) == 0)
            {
                Runtime.AbortPreparationResources(this);
            }
        }

        /// <summary>Aborts an uncommitted preparation.</summary>
        public void Dispose()
        {
            Abort();
        }

        internal SwitchableJsonConfigurationRuntime Runtime { get; }

        internal long PreparedStateVersion { get; }

        internal long PreparedWatcherGeneration { get; }

        internal SwitchableJsonConfigurationProvider? Provider { get; }

        internal IDictionary<string, string?>? CandidateData;

        internal ActiveSourceWatcher? PreparedWatcher;

        internal PreparedSourceWatcherRelay? WatcherRelay;

        internal bool TryClaimForDirectSwitch()
        {
            return Interlocked.CompareExchange(ref _claimed, 1, 0) == 0;
        }

        internal ActiveSourceWatcher? TakePreparedWatcher()
        {
            return Interlocked.Exchange(ref PreparedWatcher, null);
        }
    }
}
