using System;

using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson;

using Microsoft.Extensions.Hosting;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>
    /// Startup registration handle for one named configuration set.
    /// </summary>
    /// <remarks>
    /// This type is convenience only. Runtime consumers should continue to depend on
    /// <see cref="IConfigurationSetCoordinator"/> and <see cref="IConfigurationSetEventHub"/> through DI.
    /// </remarks>
    public sealed class ConfigurationSetRegistration
    {
        private readonly IHostApplicationBuilder _builder;

        internal ConfigurationSetRegistration(
            IHostApplicationBuilder builder,
            IConfigurationSetCoordinator coordinator)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(coordinator);
            _builder = builder;
            Coordinator = coordinator;
        }

        /// <summary>Gets the registered runtime coordinator.</summary>
        public IConfigurationSetCoordinator Coordinator { get; }

        /// <summary>Gets the caller-defined set identity.</summary>
        public string Name => Coordinator.Name;

        /// <summary>Configures how this set's value from <c>ConfigurationSets.json</c> may be applied.</summary>
        /// <param name="applyMode">Whether state-file changes may apply at runtime or only during host startup.</param>
        /// <returns>This registration handle for chaining.</returns>
        public ConfigurationSetRegistration ApplyMode(ConfigurationSetApplyMode applyMode)
        {
            _builder.SetConfigurationSetApplyMode(Name, applyMode);
            return this;
        }

        /// <summary>
        /// Registers one switchable JSON source using an arbitrary complete path mapping for the allowed set values.
        /// </summary>
        /// <param name="sourcePathResolver">Resolves the complete JSON source path for each allowed set value.</param>
        /// <param name="optional">Whether a missing active source is treated as empty by framework-driven loads.</param>
        /// <param name="reloadOnChange">Whether the active JSON source is watched independently for physical changes.</param>
        /// <param name="reloadDelayMilliseconds">Debounce delay for active-source file notifications.</param>
        /// <param name="runtimeFailurePolicy">Runtime failure policy used by the switchable JSON source.</param>
        /// <returns>This registration handle for chaining.</returns>
        public ConfigurationSetRegistration AddSwitchableJson(
            Func<string, string> sourcePathResolver,
            bool optional = false,
            bool reloadOnChange = false,
            int reloadDelayMilliseconds = 250,
            SwitchableJsonRuntimeFailurePolicy runtimeFailurePolicy = SwitchableJsonRuntimeFailurePolicy.KeepLastKnownGood)
        {
            _builder.AddSwitchableJsonToConfigurationSet(
                Name,
                sourcePathResolver,
                optional,
                reloadOnChange,
                reloadDelayMilliseconds,
                runtimeFailurePolicy);

            return this;
        }

        /// <summary>
        /// Registers one switchable JSON source whose path follows <c>{rootPath}/{setValue}/{fileName}</c>.
        /// The technical participant identity is derived automatically for DI and diagnostics.
        /// </summary>
        public ConfigurationSetRegistration AddSwitchableJson(
            string rootPath,
            string fileName,
            bool optional = false,
            bool reloadOnChange = false,
            int reloadDelayMilliseconds = 250,
            SwitchableJsonRuntimeFailurePolicy runtimeFailurePolicy = SwitchableJsonRuntimeFailurePolicy.KeepLastKnownGood)
        {
            _builder.AddSwitchableJsonToConfigurationSet(
                Name,
                rootPath,
                fileName,
                optional,
                reloadOnChange,
                reloadDelayMilliseconds,
                runtimeFailurePolicy);

            return this;
        }

        /// <summary>
        /// Registers multiple independent switchable JSON sources in the same <c>{rootPath}/{setValue}</c> directory.
        /// Technical participant identities are derived automatically from the set and logical file paths.
        /// </summary>
        public ConfigurationSetRegistration AddSwitchableJson(
            string rootPath,
            params string[] fileNames)
        {
            _builder.AddSwitchableJsonToConfigurationSet(Name, rootPath, fileNames);
            return this;
        }
    }
}
