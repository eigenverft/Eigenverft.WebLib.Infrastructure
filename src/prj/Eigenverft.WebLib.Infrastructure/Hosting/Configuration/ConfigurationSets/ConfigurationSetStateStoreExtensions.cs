using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>Registers a self-describing JSON control file for configuration-set coordinators known at startup.</summary>
    public static class ConfigurationSetStateStoreExtensions
    {
        /// <summary>
        /// Registers multiple configuration sets and their shared self-describing state file in one startup declaration.
        /// </summary>
        /// <param name="builder">The host application builder receiving the coordinators and state store.</param>
        /// <param name="path">Absolute state-file path, or a path relative to the host content root.</param>
        /// <param name="definitions">Configuration-set definitions to register before the state file is initialized.</param>
        /// <returns>The runtime state-store instance.</returns>
        public static IConfigurationSetStateStore AddConfigurationSetsWithStateFile(
            this IHostApplicationBuilder builder,
            string path,
            params ConfigurationSetDefinition[] definitions)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(definitions);

            _ = builder.AddConfigurationSets(definitions);
            return builder.AddConfigurationSetStateFile(path);
        }

        /// <summary>
        /// Adds one configuration-set state file, materializes authoritative allowed-value metadata, and optionally watches it.
        /// </summary>
        /// <param name="builder">The host application builder containing the set coordinators to manage.</param>
        /// <param name="path">Absolute path, or a path relative to the host content root.</param>
        /// <param name="reloadOnChange">Whether physical state-file edits are applied after startup.</param>
        /// <param name="reloadDelayMilliseconds">Debounce delay for physical file notifications.</param>
        /// <returns>The runtime state-store instance, also registered as a singleton through DI.</returns>
        /// <remarks>
        /// The store captures the coordinators registered before this call. Missing files are created from the current coordinator
        /// state. AllowedValues arrays in the file are descriptive metadata only; registered coordinator definitions remain
        /// authoritative. Unknown set names or disallowed values reject the document before any set is switched. Operational
        /// failures in one otherwise-valid independent set do not roll back successful transitions of another independent set.
        /// </remarks>
        public static IConfigurationSetStateStore AddConfigurationSetStateFile(
            this IHostApplicationBuilder builder,
            string path,
            bool reloadOnChange = true,
            int reloadDelayMilliseconds = 250)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            if (reloadDelayMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(reloadDelayMilliseconds));
            }

            if (builder.Services.Any(descriptor => descriptor.ServiceType == typeof(IConfigurationSetStateStore)))
            {
                throw new InvalidOperationException("A configuration set state store is already registered.");
            }

            var coordinators = ConfigurationSetCoordinatorExtensions.GetRegisteredCoordinatorSnapshot(builder);
            if (coordinators.Count == 0)
            {
                throw new InvalidOperationException(
                    "At least one configuration set coordinator must be registered before the state file is added.");
            }

            string filePath = System.IO.Path.IsPathRooted(path)
                ? System.IO.Path.GetFullPath(path)
                : System.IO.Path.GetFullPath(System.IO.Path.Combine(builder.Environment.ContentRootPath, path));

            var store = new ConfigurationSetStateStore(
                filePath,
                coordinators,
                reloadOnChange,
                reloadDelayMilliseconds);

            try
            {
                store.Initialize();
                builder.Services.AddSingleton<IConfigurationSetStateStore>(_ => store);
                builder.Services.AddSingleton<IHostedService>(_ => new ConfigurationSetStateStoreHostedService(store));
                return store;
            }
            catch
            {
                store.Dispose();
                throw;
            }
        }
    }
}
