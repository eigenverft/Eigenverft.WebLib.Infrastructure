using System;
using System.Collections.Generic;
using System.IO;

using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson;

using Microsoft.Extensions.Hosting;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>Provides startup-time registration and binding helpers for named configuration sets.</summary>
    public static class ConfigurationSetBuilderBindingExtensions
    {
        /// <summary>
        /// Registers one switchable JSON source and binds it to a configuration set using an internally derived runtime identity.
        /// </summary>
        public static IHostApplicationBuilder AddSwitchableJsonToConfigurationSet(
            this IHostApplicationBuilder builder,
            string setName,
            string rootPath,
            string fileName,
            bool optional = false,
            bool reloadOnChange = false,
            int reloadDelayMilliseconds = 250,
            SwitchableJsonRuntimeFailurePolicy runtimeFailurePolicy = SwitchableJsonRuntimeFailurePolicy.KeepLastKnownGood)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(setName);
            ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

            string switchableName = CreateGeneratedSwitchableName(setName, rootPath, fileName);
            return builder.AddSwitchableJsonToConfigurationSet(
                setName,
                switchableName,
                rootPath,
                fileName,
                optional,
                reloadOnChange,
                reloadDelayMilliseconds,
                runtimeFailurePolicy);
        }

        /// <summary>
        /// Registers multiple switchable JSON sources in one set directory using internally derived runtime identities.
        /// </summary>
        public static IHostApplicationBuilder AddSwitchableJsonToConfigurationSet(
            this IHostApplicationBuilder builder,
            string setName,
            string rootPath,
            params string[] fileNames)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(setName);
            ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
            ArgumentNullException.ThrowIfNull(fileNames);

            var files = new (string SwitchableName, string FileName)[fileNames.Length];
            for (int index = 0; index < fileNames.Length; index++)
            {
                string fileName = fileNames[index];
                ArgumentException.ThrowIfNullOrWhiteSpace(fileName, nameof(fileNames));
                files[index] = (CreateGeneratedSwitchableName(setName, rootPath, fileName), fileName);
            }

            return builder.AddSwitchableJsonToConfigurationSet(setName, rootPath, files);
        }

        /// <summary>
        /// Registers one switchable JSON source and binds it to the common directory layout
        /// <c>{rootPath}/{setValue}/{fileName}</c> in one startup call.
        /// </summary>
        /// <param name="builder">The host application builder receiving the switchable source and binding.</param>
        /// <param name="setName">The already registered keyed configuration-set coordinator name.</param>
        /// <param name="switchableName">The keyed switchable JSON runtime name to register.</param>
        /// <param name="rootPath">Root directory containing one subdirectory per allowed set value.</param>
        /// <param name="fileName">JSON file path relative to each set-value directory.</param>
        /// <param name="optional">Whether a missing active source is treated as empty by framework-driven loads.</param>
        /// <param name="reloadOnChange">Whether the active JSON source is watched independently for physical changes.</param>
        /// <param name="reloadDelayMilliseconds">Debounce delay for active-source file notifications.</param>
        /// <param name="runtimeFailurePolicy">Runtime failure policy used by the switchable JSON source.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <remarks>
        /// The initial JSON path is derived from the coordinator's current <see cref="IConfigurationSetCoordinator.ActiveValue"/>,
        /// so startup code does not repeat that value. If binding fails after registration, the just-created switchable source is
        /// removed again so this convenience operation remains all-or-nothing from the builder's perspective.
        /// </remarks>
        public static IHostApplicationBuilder AddSwitchableJsonToConfigurationSet(
            this IHostApplicationBuilder builder,
            string setName,
            string switchableName,
            string rootPath,
            string fileName,
            bool optional = false,
            bool reloadOnChange = false,
            int reloadDelayMilliseconds = 250,
            SwitchableJsonRuntimeFailurePolicy runtimeFailurePolicy = SwitchableJsonRuntimeFailurePolicy.KeepLastKnownGood)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(setName);
            ArgumentException.ThrowIfNullOrWhiteSpace(switchableName);
            ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

            IConfigurationSetCoordinator coordinator = GetRequiredCoordinator(builder, setName);
            string initialPath = Path.Combine(rootPath, coordinator.ActiveValue, fileName);

            builder.AddSwitchableJsonFile(
                switchableName,
                initialPath,
                optional,
                reloadOnChange,
                reloadDelayMilliseconds,
                runtimeFailurePolicy);

            try
            {
                return builder.BindSwitchableJsonDirectoryToConfigurationSet(
                    setName,
                    switchableName,
                    rootPath,
                    fileName);
            }
            catch
            {
                _ = SwitchableJsonConfigurationExtensions.RemoveRegisteredSwitchableJsonFile(builder, switchableName);
                throw;
            }
        }

        /// <summary>
        /// Registers and binds multiple independent switchable JSON sources that share the same
        /// <c>{rootPath}/{setValue}</c> directory layout.
        /// </summary>
        /// <param name="builder">The host application builder receiving all switchable sources and bindings.</param>
        /// <param name="setName">The already registered keyed configuration-set coordinator name.</param>
        /// <param name="rootPath">Root directory containing one subdirectory per allowed set value.</param>
        /// <param name="files">Pairs of keyed switchable runtime name and JSON file path within each set-value directory.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <remarks>
        /// Every pair remains a normal independent <see cref="ISwitchableJsonConfiguration"/> runtime. This method only removes
        /// repetitive startup wiring; it does not create a grouped provider or change lifecycle, LKG, DI, or reload semantics.
        /// All entries are validated before registration begins. If a later registration still fails, already-created entries from
        /// this batch are rolled back in reverse order.
        /// </remarks>
        public static IHostApplicationBuilder AddSwitchableJsonToConfigurationSet(
            this IHostApplicationBuilder builder,
            string setName,
            string rootPath,
            params (string SwitchableName, string FileName)[] files)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(setName);
            ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
            ArgumentNullException.ThrowIfNull(files);

            IConfigurationSetCoordinator coordinator = GetRequiredCoordinator(builder, setName);
            if (coordinator is not ConfigurationSetCoordinator implementation)
            {
                throw new NotSupportedException(
                    "Multi-file configuration-set registration requires the Eigenverft ConfigurationSetCoordinator implementation.");
            }

            if (files.Length == 0)
            {
                throw new ArgumentException("At least one switchable JSON file is required.", nameof(files));
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach ((string switchableName, string fileName) in files)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(switchableName, nameof(files));
                ArgumentException.ThrowIfNullOrWhiteSpace(fileName, nameof(files));

                if (!names.Add(switchableName))
                {
                    throw new ArgumentException(
                        $"Switchable JSON runtime name '{switchableName}' appears more than once in the batch.",
                        nameof(files));
                }

                if (SwitchableJsonConfigurationExtensions.TryGetRegisteredRuntimeHandle(
                        builder,
                        switchableName,
                        out _))
                {
                    throw new InvalidOperationException(
                        $"A switchable JSON configuration source named '{switchableName}' is already registered.");
                }
            }

            var registeredNames = new List<string>(files.Length);
            try
            {
                foreach ((string switchableName, string fileName) in files)
                {
                    builder.AddSwitchableJsonToConfigurationSet(
                        setName,
                        switchableName,
                        rootPath,
                        fileName);
                    registeredNames.Add(switchableName);
                }

                return builder;
            }
            catch
            {
                for (int index = registeredNames.Count - 1; index >= 0; index--)
                {
                    string registeredName = registeredNames[index];
                    _ = implementation.RemoveSwitchableJsonBinding(registeredName);
                    _ = SwitchableJsonConfigurationExtensions.RemoveRegisteredSwitchableJsonFile(
                        builder,
                        registeredName);
                }

                throw;
            }
        }

        /// <summary>
        /// Binds a switchable JSON source using the common directory layout <c>{rootPath}/{setValue}/{fileName}</c>.
        /// </summary>
        /// <param name="builder">The host application builder containing both registrations.</param>
        /// <param name="setName">The keyed configuration-set coordinator name.</param>
        /// <param name="switchableName">The keyed switchable JSON runtime name.</param>
        /// <param name="rootPath">Root directory containing one subdirectory per allowed set value.</param>
        /// <param name="fileName">JSON file path located inside each set-value directory.</param>
        /// <returns>The same builder for chaining.</returns>
        public static IHostApplicationBuilder BindSwitchableJsonDirectoryToConfigurationSet(
            this IHostApplicationBuilder builder,
            string setName,
            string switchableName,
            string rootPath,
            string fileName)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

            return builder.BindSwitchableJsonToConfigurationSet(
                setName,
                switchableName,
                value => Path.Combine(rootPath, value, fileName));
        }

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
        /// The binding verifies that the switchable source already represents the coordinator's active value.
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

            IConfigurationSetCoordinator coordinator = GetRequiredCoordinator(builder, setName);

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

        internal static string CreateGeneratedSwitchableName(
            string setName,
            string rootPath,
            string fileName)
        {
            string logicalPath = Path.Combine(rootPath, fileName)
                .Replace('\\', '/')
                .Trim('/');

            return $"{setName}:{logicalPath}";
        }

        private static IConfigurationSetCoordinator GetRequiredCoordinator(
            IHostApplicationBuilder builder,
            string setName)
        {
            if (!ConfigurationSetCoordinatorExtensions.TryGetRegisteredCoordinator(
                    builder,
                    setName,
                    out IConfigurationSetCoordinator? coordinator) ||
                coordinator is null)
            {
                throw new InvalidOperationException(
                    $"Configuration set coordinator '{setName}' must be registered before it can be bound.");
            }

            return coordinator;
        }
    }
}
