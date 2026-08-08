using System;

using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson;

using Microsoft.Extensions.Hosting;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>
    /// Startup registration handle for one named configuration set.
    /// </summary>
    /// <remarks>
    /// This type is startup convenience only. Application-level runtime control should normally use
    /// <see cref="IConfigurationSetManager"/> for ephemeral switches or <see cref="IConfigurationSetDesiredStateStore"/>
    /// when desired state must persist. Keyed <see cref="IConfigurationSetCoordinator"/> access remains available for
    /// advanced set-specific integration, while <see cref="IConfigurationSetEventHub"/> provides host-wide observation.
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

        /// <summary>Configures how this set's desired value may be applied when a desired-state store is used.</summary>
        /// <param name="applyMode">Whether desired-state changes may apply at runtime or only during host startup.</param>
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
        /// Registers multiple independent switchable JSON sources in the same <c>{rootPath}/{setValue}</c> directory
        /// while applying the same source options to every file.
        /// </summary>
        /// <param name="rootPath">Root directory containing one subdirectory per allowed set value.</param>
        /// <param name="options">Shared switchable JSON registration options applied to every file.</param>
        /// <param name="fileNames">JSON file paths within each set-value directory.</param>
        /// <returns>This registration handle for chaining.</returns>
        public ConfigurationSetRegistration AddSwitchableJson(
            string rootPath,
            SwitchableJsonRegistrationOptions options,
            params string[] fileNames)
        {
            ArgumentNullException.ThrowIfNull(options);
            _builder.AddSwitchableJsonToConfigurationSet(Name, rootPath, options, fileNames);
            return this;
        }

        /// <summary>
        /// Registers multiple independent switchable JSON sources in the same <c>{rootPath}/{setValue}</c> directory using default source options.
        /// Technical participant identities are derived automatically from the set and logical file paths.
        /// </summary>
        /// <param name="rootPath">Root directory containing one subdirectory per allowed set value.</param>
        /// <param name="fileNames">JSON file paths within each set-value directory.</param>
        /// <returns>This registration handle for chaining.</returns>
        public ConfigurationSetRegistration AddSwitchableJson(
            string rootPath,
            params string[] fileNames)
        {
            _builder.AddSwitchableJsonToConfigurationSet(Name, rootPath, fileNames);
            return this;
        }
    }
}
