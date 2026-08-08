using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>Registers a self-describing JSON control file for configuration-set coordinators known at startup.</summary>
    public static class ConfigurationSetStateStoreExtensions
    {
        private static readonly object StateApplyModesKey = new();


        /// <summary>Sets the code-owned desired-state apply mode for one already registered configuration set.</summary>
        /// <param name="builder">The host application builder containing the configuration set.</param>
        /// <param name="setName">The registered configuration-set name.</param>
        /// <param name="applyMode">Whether desired-state changes may apply at runtime or only during startup.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <remarks>
        /// This policy belongs to desired-state control, not to <see cref="IConfigurationSetCoordinator"/>. Direct coordinator
        /// switches remain technically possible regardless of this setting. The policy is frozen when the state store is registered.
        /// </remarks>
        public static IHostApplicationBuilder SetConfigurationSetApplyMode(
            this IHostApplicationBuilder builder,
            string setName,
            ConfigurationSetApplyMode applyMode)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(setName);

            if (!Enum.IsDefined(applyMode))
            {
                throw new ArgumentOutOfRangeException(nameof(applyMode));
            }

            if (builder.Services.Any(descriptor => descriptor.ServiceType == typeof(IConfigurationSetStateStore)))
            {
                throw new InvalidOperationException(
                    "Configuration set state apply modes must be configured before the state store is registered.");
            }

            if (!ConfigurationSetCoordinatorExtensions.TryGetRegisteredCoordinator(builder, setName, out _))
            {
                throw new InvalidOperationException(
                    $"Configuration set coordinator '{setName}' must be registered before its state apply mode is configured.");
            }

            GetStateApplyModes(builder)[setName] = applyMode;
            return builder;
        }

        /// <summary>
        /// Adds one configuration-set state file, materializes authoritative metadata, and optionally watches it.
        /// </summary>
        /// <param name="builder">The host application builder containing the set coordinators to manage.</param>
        /// <param name="path">Absolute path, or a path relative to the host content root.</param>
        /// <param name="watchForChanges">Whether the desired-state control file is watched for edits after startup.</param>
        /// <param name="reloadDelayMilliseconds">Debounce delay for physical file notifications.</param>
        /// <returns>The runtime state-store instance, also registered as a singleton through DI.</returns>
        /// <remarks>
        /// The store captures coordinators and their state apply modes registered before this call. Missing files are created from the
        /// current desired state. <c>AllowedValues</c> and <c>ApplyMode</c> in the file are descriptive metadata only; registered code
        /// remains authoritative. Unknown set names or disallowed values reject the document before any set is switched. Runtime
        /// operational failures in one otherwise-valid independent set do not roll back successful transitions of another independent set.
        /// </remarks>
        public static IConfigurationSetStateStore AddConfigurationSetStateFile(
            this IHostApplicationBuilder builder,
            string path,
            bool watchForChanges = true,
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

            IReadOnlyList<IConfigurationSetCoordinator> coordinators =
                ConfigurationSetCoordinatorExtensions.GetRegisteredCoordinatorSnapshot(builder);
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
                GetStateApplyModeSnapshot(builder, coordinators),
                watchForChanges,
                reloadDelayMilliseconds);

            try
            {
                store.Initialize();
                builder.Services.AddSingleton<IConfigurationSetDesiredStateStore>(_ => store);
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

        internal static IReadOnlyDictionary<string, ConfigurationSetApplyMode> GetStateApplyModeSnapshot(
            IHostApplicationBuilder builder,
            IReadOnlyList<IConfigurationSetCoordinator> coordinators)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(coordinators);

            Dictionary<string, ConfigurationSetApplyMode> configured = GetStateApplyModes(builder);
            var snapshot = new Dictionary<string, ConfigurationSetApplyMode>(StringComparer.Ordinal);

            foreach (IConfigurationSetCoordinator coordinator in coordinators)
            {
                snapshot.Add(
                    coordinator.Name,
                    configured.TryGetValue(coordinator.Name, out ConfigurationSetApplyMode applyMode)
                        ? applyMode
                        : ConfigurationSetApplyMode.Runtime);
            }

            return snapshot;
        }

        private static Dictionary<string, ConfigurationSetApplyMode> GetStateApplyModes(IHostApplicationBuilder builder)
        {
            if (builder.Properties.TryGetValue(StateApplyModesKey, out object? value) &&
                value is Dictionary<string, ConfigurationSetApplyMode> existing)
            {
                return existing;
            }

            var created = new Dictionary<string, ConfigurationSetApplyMode>(StringComparer.Ordinal);
            builder.Properties[StateApplyModesKey] = created;
            return created;
        }
    }
}
