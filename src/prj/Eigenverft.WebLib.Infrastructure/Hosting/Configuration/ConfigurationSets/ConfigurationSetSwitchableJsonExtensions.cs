using System;
using System.Collections.Generic;

using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>Connects configuration-set coordinators to existing switchable JSON configuration runtimes.</summary>
    public static class ConfigurationSetSwitchableJsonExtensions
    {
        /// <summary>
        /// Binds one switchable JSON configuration runtime to this set by defining the source path used for each allowed value.
        /// </summary>
        /// <param name="coordinator">The set coordinator that owns the logical value transition.</param>
        /// <param name="configuration">The independently registered switchable JSON runtime to coordinate.</param>
        /// <param name="sourcePathResolver">
        /// Resolves a JSON source path for every allowed set value. The mapping is evaluated and frozen during binding.
        /// </param>
        /// <returns>The same coordinator for chaining.</returns>
        /// <remarks>
        /// Binding validates that the switchable runtime is already on the source mapped to the coordinator's current active value.
        /// Once bound, the coordinator exclusively owns source selection for that runtime. Direct public source switches are rejected,
        /// while active-file reload and lifecycle observation remain normal switchable-runtime behavior. A runtime can belong to only
        /// one configuration set at a time.
        /// </remarks>
        public static IConfigurationSetCoordinator BindSwitchableJson(
            this IConfigurationSetCoordinator coordinator,
            ISwitchableJsonConfiguration configuration,
            Func<string, string> sourcePathResolver)
        {
            ArgumentNullException.ThrowIfNull(coordinator);
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(sourcePathResolver);

            if (coordinator is not ConfigurationSetCoordinator implementation)
            {
                throw new NotSupportedException(
                    "Switchable JSON binding requires the Eigenverft ConfigurationSetCoordinator implementation.");
            }

            var paths = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string allowedValue in coordinator.AllowedValues)
            {
                string path = sourcePathResolver(allowedValue);
                ArgumentException.ThrowIfNullOrWhiteSpace(path, nameof(sourcePathResolver));
                paths.Add(allowedValue, path);
            }

            implementation.AddSwitchableJsonBinding(
                new SwitchableJsonConfigurationSetBinding(coordinator.Name, configuration, paths));

            return coordinator;
        }
    }
}
