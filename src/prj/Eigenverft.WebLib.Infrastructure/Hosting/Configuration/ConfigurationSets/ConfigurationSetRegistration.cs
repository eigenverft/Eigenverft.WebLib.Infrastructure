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
