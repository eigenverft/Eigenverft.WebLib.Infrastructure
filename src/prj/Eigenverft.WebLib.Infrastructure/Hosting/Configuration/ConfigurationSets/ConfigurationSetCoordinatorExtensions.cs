using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>Registers independent named configuration-set coordinators.</summary>
    public static class ConfigurationSetCoordinatorExtensions
    {
        private static readonly object RegisteredCoordinatorsKey = new();

        /// <summary>
        /// Adds one independent configuration-set coordinator and exposes the same runtime instance through keyed DI.
        /// </summary>
        /// <param name="builder">The host application builder receiving the keyed runtime service.</param>
        /// <param name="name">Caller-defined set identity and keyed-service key.</param>
        /// <param name="initialValue">Value active when the coordinator is created.</param>
        /// <param name="allowedValues">Complete values accepted by this set.</param>
        /// <returns>
        /// The coordinator runtime instance. Returning the runtime allows startup code to bind further set-specific behavior before
        /// the host is built while later runtime consumers resolve the same instance through keyed dependency injection.
        /// </returns>
        public static IConfigurationSetCoordinator AddConfigurationSetCoordinator(
            this IHostApplicationBuilder builder,
            string name,
            string initialValue,
            IEnumerable<string> allowedValues)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentException.ThrowIfNullOrWhiteSpace(initialValue);
            ArgumentNullException.ThrowIfNull(allowedValues);

            if (builder.Services.Any(descriptor =>
                    descriptor.ServiceType == typeof(IConfigurationSetCoordinator) &&
                    descriptor.IsKeyedService &&
                    Equals(descriptor.ServiceKey, name)))
            {
                throw new InvalidOperationException(
                    $"A configuration set coordinator named '{name}' is already registered.");
            }

            var definition = new ConfigurationSetDefinition(name, initialValue, allowedValues);
            IConfigurationSetCoordinator coordinator = new ConfigurationSetCoordinator(definition);

            builder.Services.AddKeyedSingleton<IConfigurationSetCoordinator>(
                name,
                (_, _) => coordinator);

            GetRegisteredCoordinators(builder).Add(name, coordinator);
            return coordinator;
        }

        internal static bool TryGetRegisteredCoordinator(
            IHostApplicationBuilder builder,
            string name,
            out IConfigurationSetCoordinator? coordinator)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            if (builder.Properties.TryGetValue(RegisteredCoordinatorsKey, out object? value) &&
                value is Dictionary<string, IConfigurationSetCoordinator> registrations &&
                registrations.TryGetValue(name, out IConfigurationSetCoordinator? registered))
            {
                coordinator = registered;
                return true;
            }

            coordinator = null;
            return false;
        }

        private static Dictionary<string, IConfigurationSetCoordinator> GetRegisteredCoordinators(
            IHostApplicationBuilder builder)
        {
            if (builder.Properties.TryGetValue(RegisteredCoordinatorsKey, out object? value) &&
                value is Dictionary<string, IConfigurationSetCoordinator> registrations)
            {
                return registrations;
            }

            var created = new Dictionary<string, IConfigurationSetCoordinator>(StringComparer.Ordinal);
            builder.Properties[RegisteredCoordinatorsKey] = created;
            return created;
        }
    }
}
