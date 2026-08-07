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
    /// </remarks>
    public interface ISwitchableJsonConfiguration
    {
        /// <summary>Gets the caller-defined identity used for keyed dependency-injection lookup and diagnostics.</summary>
        string Name { get; }

        /// <summary>Gets the normalized path of the currently active JSON source.</summary>
        string CurrentSourcePath { get; }

        /// <summary>
        /// Occurs after a runtime switch attempt has completed, including successful no-op and rejected attempts.
        /// </summary>
        /// <remarks>
        /// V1 reports completed outcomes only. A separate pre-I/O SwitchRequested event can be added later if an audit scenario
        /// must observe an attempt before source access begins; it is intentionally omitted from the minimal lifecycle contract.
        /// </remarks>
        event EventHandler<SwitchableJsonConfigurationEventArgs>? LifecycleChanged;

        /// <summary>
        /// Loads a candidate JSON source completely before atomically publishing it as the active source.
        /// </summary>
        /// <param name="sourcePath">Absolute path, or a path relative to the host content root used during registration.</param>
        /// <returns>The completed switch outcome.</returns>
        /// <remarks>
        /// V1 is synchronous because <c>IConfigurationProvider</c> and local file loading are synchronous. A future asynchronous
        /// API would be appropriate for remote or otherwise slow sources, but is intentionally not introduced for local JSON files.
        /// </remarks>
        SwitchableJsonSwitchResult TrySwitch(string sourcePath);
    }
}
