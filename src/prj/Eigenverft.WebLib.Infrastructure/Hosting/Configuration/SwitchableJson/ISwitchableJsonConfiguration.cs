using System;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson
{
    /// <summary>
    /// Provides runtime control and lifecycle observation for one switchable JSON configuration source.
    /// </summary>
    /// <remarks>
    /// The abstraction is intentionally source-agnostic: names and paths may represent profiles, blue/green layouts,
    /// tenants, application-settings folders, or any other caller-defined convention. The provider assigns no meaning to them.
    /// The underlying <see cref="Microsoft.Extensions.Configuration.IConfigurationProvider"/> remains an implementation detail.
    /// The DI handle is stable even if ConfigurationManager rebuilds concrete providers after its Sources collection changes;
    /// such framework rebuilds do not change this handle's identity or the provider's precedence position in the source stack.
    /// </remarks>
    public interface ISwitchableJsonConfiguration
    {
        /// <summary>Gets the caller-defined identity used for keyed dependency-injection lookup and diagnostics.</summary>
        string Name { get; }

        /// <summary>Gets the normalized path of the currently active JSON source.</summary>
        string CurrentSourcePath { get; }

        /// <summary>
        /// Occurs after a source lifecycle operation completes, including manual switches and optional active-file reloads.
        /// </summary>
        /// <remarks>
        /// This event is independent of the normal IConfiguration change token. A successful source switch or file reload can
        /// therefore be observable here while producing no IConfiguration reload when the effective key/value snapshot is equal.
        /// Active-file events are raised from the file-watcher callback path, so handlers must be thread-safe and should avoid
        /// long-running work. Observer exceptions are isolated by the provider: a notification handler cannot roll back or make a
        /// completed source operation fail, and one failing observer does not prevent later observers from receiving the event.
        /// The same non-veto rule applies to IConfiguration change-token consumers: an exception raised by a reload observer is
        /// isolated after the provider snapshot has committed and does not turn a successful switch/reload into a rejected one.
        /// Consumers should not coordinate work by assuming ordering between this lifecycle channel and IConfiguration change-token
        /// callbacks; both describe an already committed provider state, while IConfiguration reload is emitted only for effective
        /// data changes. Lifecycle handlers also run outside the provider state lock, so with concurrent callers the event payload
        /// describes the completed operation and should be preferred over re-reading CurrentSourcePath to reconstruct that operation.
        /// A separate pre-I/O SwitchRequested event remains intentionally omitted from the minimal lifecycle contract.
        /// </remarks>
        event EventHandler<SwitchableJsonConfigurationEventArgs>? LifecycleChanged;

        /// <summary>
        /// Loads a candidate JSON source completely before atomically publishing it as the active source.
        /// </summary>
        /// <param name="sourcePath">Absolute path, or a path relative to the host content root used during registration.</param>
        /// <returns>The completed switch outcome.</returns>
        /// <remarks>
        /// The API is synchronous because IConfigurationProvider and local file loading are synchronous. Concurrent calls are thread-safe
        /// and serialize complete candidate-load/compare/commit operations; whichever call acquires runtime serialization next is
        /// the next operation committed, so no separate cross-thread request-priority guarantee is implied. A future asynchronous
        /// API would be appropriate for remote or otherwise slow sources, but is intentionally not introduced for local JSON files.
        /// <para>
        /// An explicit <see cref="Microsoft.Extensions.Configuration.IConfigurationRoot.Reload"/> remains a framework-level command:
        /// it invokes provider Load semantics and the root emits its normal reload notification even when effective data is equal.
        /// The switchable Source/Lifecycle channel does not reinterpret that global framework operation as a manual source switch.
        /// </para>
        /// </remarks>
        SwitchableJsonSwitchResult TrySwitch(string sourcePath);
    }
}
