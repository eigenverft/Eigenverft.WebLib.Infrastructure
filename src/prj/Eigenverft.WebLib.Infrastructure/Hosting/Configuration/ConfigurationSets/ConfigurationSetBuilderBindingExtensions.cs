using System;

using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson;

using Microsoft.Extensions.Hosting;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>Provides startup-time binding helpers for named configuration sets.</summary>
    public static class ConfigurationSetBuilderBindingExtensions
    {
        /// <summary>
        /// Binds an already registered switchable JSON source to an already registered configuration set before the host is built.
        /// </summary>
        /// <param name="builder">The host application builder containing both registrations.</param>
        /// <param name="setName">The keyed configuration-set coordinator name.</param>
        /// <param name="switchableName">The keyed switchable JSON runtime name.</param>
        /// <param name="sourcePathResolver">Resolves the source path represented by each allowed set value.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <remarks>
        /// The coordinator and switchable source may be registered in either order, but both must exist before this method is called.
        /// Binding is completed immediately during startup, so no hosted-service timing or post-Build initialization is required.
        /// The existing binding contract verifies that the switchable source already represents the coordinator's active value.
        /// </remarks>
        public static IHostApplicationBuilder BindSwitchableJsonToConfigurationSet(
            this IHostApplicationBuilder builder,
            string setName,
            string switchableName,
            Func<string, string> sourcePathResolver)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(setName);
            ArgumentException.ThrowIfNullOrWhiteSpace(switchableName);
            ArgumentNullException.ThrowIfNull(sourcePathResolver);

            if (!ConfigurationSetCoordinatorExtensions.TryGetRegisteredCoordinator(
                    builder,
                    setName,
                    out IConfigurationSetCoordinator? coordinator) ||
                coordinator is null)
            {
                throw new InvalidOperationException(
                    $"Configuration set coordinator '{setName}' must be registered before it can be bound.");
            }

            if (!SwitchableJsonConfigurationExtensions.TryGetRegisteredRuntimeHandle(
                    builder,
                    switchableName,
                    out ISwitchableJsonConfiguration? configuration) ||
                configuration is null)
            {
                throw new InvalidOperationException(
                    $"Switchable JSON configuration '{switchableName}' must be registered before it can be bound.");
            }

            coordinator.BindSwitchableJson(configuration, sourcePathResolver);
            return builder;
        }
    }
}
