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
        private static readonly object EventHubKey = new();
        private static readonly object ManagerKey = new();

        /// <summary>
        /// Adds one configuration set and returns a fluent startup handle for binding switchable configuration sources.
        /// </summary>
        /// <param name="builder">The host application builder receiving the coordinator.</param>
        /// <param name="name">Caller-defined set identity and keyed-service key.</param>
        /// <param name="initialValue">Initial active value; it is automatically included in the allowed values.</param>
        /// <param name="additionalAllowedValues">Additional values that may become active.</param>
        /// <returns>A startup registration handle for this set.</returns>
        public static ConfigurationSetRegistration AddConfigurationSet(
            this IHostApplicationBuilder builder,
            string name,
            string initialValue,
            params string[] additionalAllowedValues)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ConfigurationSetDefinition definition = ConfigurationSetDefinition.Create(
                name,
                initialValue,
                additionalAllowedValues);
            IConfigurationSetCoordinator coordinator = builder.AddConfigurationSet(definition);
            return new ConfigurationSetRegistration(builder, coordinator);
        }

        /// <summary>
        /// Adds one independent configuration-set coordinator and exposes the same runtime instance through keyed DI.
        /// </summary>
        /// <param name="builder">The host application builder receiving the keyed runtime service.</param>
        /// <param name="name">Caller-defined set identity and keyed-service key.</param>
        /// <param name="initialValue">Value active when the coordinator is created.</param>
        /// <param name="allowedValues">Complete values accepted by this set.</param>
        /// <returns>The created coordinator runtime.</returns>
        public static IConfigurationSetCoordinator AddConfigurationSetCoordinator(
            this IHostApplicationBuilder builder,
            string name,
            string initialValue,
            IEnumerable<string> allowedValues)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.AddConfigurationSet(
                new ConfigurationSetDefinition(name, initialValue, allowedValues));
        }

        /// <summary>Adds one validated configuration-set definition and exposes its coordinator through keyed DI.</summary>
        /// <param name="builder">The host application builder receiving the coordinator.</param>
        /// <param name="definition">The complete set definition to register.</param>
        /// <returns>The created coordinator runtime.</returns>
        public static IConfigurationSetCoordinator AddConfigurationSet(
            this IHostApplicationBuilder builder,
            ConfigurationSetDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(definition);

            if (builder.Services.Any(descriptor =>
                    descriptor.ServiceType == typeof(IConfigurationSetCoordinator) &&
                    descriptor.IsKeyedService &&
                    Equals(descriptor.ServiceKey, definition.Name)))
            {
                throw new InvalidOperationException(
                    $"A configuration set coordinator named '{definition.Name}' is already registered.");
            }

            IConfigurationSetCoordinator coordinator = new ConfigurationSetCoordinator(definition);

            builder.Services.AddKeyedSingleton<IConfigurationSetCoordinator>(
                definition.Name,
                (_, _) => coordinator);

            GetRegisteredCoordinators(builder).Add(definition.Name, coordinator);
            GetOrCreateEventHub(builder).Attach(coordinator);
            GetOrCreateManager(builder).Attach(coordinator);
            return coordinator;
        }

        /// <summary>Registers multiple independent configuration-set definitions in declaration order.</summary>
        /// <param name="builder">The host application builder receiving the coordinators.</param>
        /// <param name="definitions">The definitions to register.</param>
        /// <returns>The same builder for chaining.</returns>
        public static IHostApplicationBuilder AddConfigurationSets(
            this IHostApplicationBuilder builder,
            params ConfigurationSetDefinition[] definitions)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(definitions);

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConfigurationSetDefinition definition in definitions)
            {
                ArgumentNullException.ThrowIfNull(definition);

                if (!names.Add(definition.Name))
                {
                    throw new InvalidOperationException(
                        $"Configuration set batch contains duplicate name '{definition.Name}'.");
                }

                if (builder.Services.Any(descriptor =>
                        descriptor.ServiceType == typeof(IConfigurationSetCoordinator) &&
                        descriptor.IsKeyedService &&
                        Equals(descriptor.ServiceKey, definition.Name)))
                {
                    throw new InvalidOperationException(
                        $"A configuration set coordinator named '{definition.Name}' is already registered.");
                }
            }

            foreach (ConfigurationSetDefinition definition in definitions)
            {
                _ = builder.AddConfigurationSet(definition);
            }

            return builder;
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

        internal static IReadOnlyList<IConfigurationSetCoordinator> GetRegisteredCoordinatorSnapshot(
            IHostApplicationBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return new List<IConfigurationSetCoordinator>(GetRegisteredCoordinators(builder).Values).AsReadOnly();
        }

        private static ConfigurationSetEventHub GetOrCreateEventHub(IHostApplicationBuilder builder)
        {
            if (builder.Properties.TryGetValue(EventHubKey, out object? value) &&
                value is ConfigurationSetEventHub existing)
            {
                return existing;
            }

            var created = new ConfigurationSetEventHub();
            builder.Properties[EventHubKey] = created;
            builder.Services.AddSingleton<IConfigurationSetEventHub>(created);
            return created;
        }

        private static ConfigurationSetManager GetOrCreateManager(IHostApplicationBuilder builder)
        {
            if (builder.Properties.TryGetValue(ManagerKey, out object? value) &&
                value is ConfigurationSetManager existing)
            {
                return existing;
            }

            var created = new ConfigurationSetManager();
            builder.Properties[ManagerKey] = created;
            builder.Services.AddSingleton<IConfigurationSetManager>(created);
            return created;
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
