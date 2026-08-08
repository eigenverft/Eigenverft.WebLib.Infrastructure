using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson
{
    /// <summary>Registers switchable JSON configuration sources and their runtime control handles.</summary>
    public static class SwitchableJsonConfigurationExtensions
    {
        private static readonly object RegisteredRuntimeHandlesKey = new();
        private static readonly object RegisteredSourcesKey = new();

        /// <summary>
        /// Adds one JSON configuration source that can later switch to another JSON file through keyed dependency injection.
        /// </summary>
        /// <param name="builder">The host application builder receiving both the configuration source and runtime handle.</param>
        /// <param name="name">Caller-defined provider identity and keyed-service key.</param>
        /// <param name="initialPath">Initial JSON path, absolute or relative to the host content root.</param>
        /// <param name="optional">
        /// Whether a missing source is treated as empty during framework-driven provider loads, including the initial load and
        /// explicit <see cref="IConfigurationRoot.Reload"/> operations.
        /// </param>
        /// <param name="reloadOnChange">
        /// Whether the currently active source is watched for physical file changes after initial load and after each successful switch.
        /// </param>
        /// <param name="reloadDelayMilliseconds">
        /// Debounce delay applied to physical file notifications before the active source is prepared again. The default mirrors
        /// the conventional Microsoft file-configuration reload delay.
        /// </param>
        /// <param name="runtimeFailurePolicy">How failed manual runtime candidate loads are reported after the host is running.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <remarks>
        /// The registration is deliberately on <see cref="IHostApplicationBuilder"/> rather than only
        /// <see cref="IConfigurationBuilder"/> because one operation must add an IConfiguration source and a DI runtime handle.
        /// A split configuration/service registration API is possible, but would make it easier to accidentally register only
        /// one half. The provider identity is otherwise completely agnostic and carries no profile, environment, or directory semantics.
        /// File watching is opt-in and independent from manual source switching.
        /// <para>
        /// The keyed DI object is a stable runtime handle, not the concrete IConfigurationProvider instance. ConfigurationManager
        /// may rebuild concrete providers when its Sources collection changes; the source creates a fresh provider for every Build
        /// while the runtime handle preserves the selected source, watcher and lifecycle subscriptions.
        /// </para>
        /// </remarks>
        public static IHostApplicationBuilder AddSwitchableJsonFile(
            this IHostApplicationBuilder builder,
            string name,
            string initialPath,
            bool optional = false,
            bool reloadOnChange = false,
            int reloadDelayMilliseconds = 250,
            SwitchableJsonRuntimeFailurePolicy runtimeFailurePolicy = SwitchableJsonRuntimeFailurePolicy.KeepLastKnownGood)
        {
            return builder.AddSwitchableJsonFile(
                name,
                initialPath,
                new SwitchableJsonRegistrationOptions
                {
                    Optional = optional,
                    ReloadOnChange = reloadOnChange,
                    ReloadDelayMilliseconds = reloadDelayMilliseconds,
                    RuntimeFailurePolicy = runtimeFailurePolicy,
                });
        }

        /// <summary>
        /// Adds one switchable JSON source with the complete registration options, including ordered candidate source preparations.
        /// </summary>
        public static IHostApplicationBuilder AddSwitchableJsonFile(
            this IHostApplicationBuilder builder,
            string name,
            string initialPath,
            SwitchableJsonRegistrationOptions options)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentException.ThrowIfNullOrWhiteSpace(initialPath);
            ArgumentNullException.ThrowIfNull(options);
            options.Validate();

            if (builder.Services.Any(descriptor =>
                    descriptor.ServiceType == typeof(ISwitchableJsonConfiguration) &&
                    descriptor.IsKeyedService &&
                    Equals(descriptor.ServiceKey, name)))
            {
                throw new InvalidOperationException(
                    $"A switchable JSON configuration source named '{name}' is already registered.");
            }

            var runtime = new SwitchableJsonConfigurationRuntime(
                name,
                builder.Environment.ContentRootPath,
                initialPath,
                options.Optional,
                options.ReloadOnChange,
                options.ReloadDelayMilliseconds,
                options.RuntimeFailurePolicy,
                options.SourcePreparations);

            IConfigurationBuilder configurationBuilder = builder.Configuration;
            var source = new SwitchableJsonConfigurationSource(runtime);

            try
            {
                configurationBuilder.Add(source);

                builder.Services.AddKeyedSingleton<ISwitchableJsonConfiguration>(
                    name,
                    (_, _) => runtime);

                GetRegisteredRuntimeHandles(builder).Add(name, runtime);
                GetRegisteredSources(builder).Add(name, source);
            }
            catch
            {
                _ = configurationBuilder.Sources.Remove(source);
                runtime.Dispose();
                throw;
            }

            return builder;
        }

        internal static bool TryGetRegisteredRuntimeHandle(
            IHostApplicationBuilder builder,
            string name,
            out ISwitchableJsonConfiguration? runtime)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            if (builder.Properties.TryGetValue(RegisteredRuntimeHandlesKey, out object? value) &&
                value is Dictionary<string, ISwitchableJsonConfiguration> registrations &&
                registrations.TryGetValue(name, out ISwitchableJsonConfiguration? registered))
            {
                runtime = registered;
                return true;
            }

            runtime = null;
            return false;
        }

        internal static bool RemoveRegisteredSwitchableJsonFile(
            IHostApplicationBuilder builder,
            string name)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            Dictionary<string, ISwitchableJsonConfiguration> runtimes = GetRegisteredRuntimeHandles(builder);
            if (!runtimes.TryGetValue(name, out ISwitchableJsonConfiguration? runtime))
            {
                return false;
            }

            Dictionary<string, SwitchableJsonConfigurationSource> sources = GetRegisteredSources(builder);
            if (sources.TryGetValue(name, out SwitchableJsonConfigurationSource? source))
            {
                _ = builder.Configuration.Sources.Remove(source);
                _ = sources.Remove(name);
            }

            _ = runtimes.Remove(name);

            for (int index = builder.Services.Count - 1; index >= 0; index--)
            {
                ServiceDescriptor descriptor = builder.Services[index];
                if (descriptor.ServiceType == typeof(ISwitchableJsonConfiguration) &&
                    descriptor.IsKeyedService &&
                    Equals(descriptor.ServiceKey, name))
                {
                    builder.Services.RemoveAt(index);
                }
            }

            if (runtime is IDisposable disposable)
            {
                disposable.Dispose();
            }

            return true;
        }

        private static Dictionary<string, ISwitchableJsonConfiguration> GetRegisteredRuntimeHandles(
            IHostApplicationBuilder builder)
        {
            if (builder.Properties.TryGetValue(RegisteredRuntimeHandlesKey, out object? value) &&
                value is Dictionary<string, ISwitchableJsonConfiguration> registrations)
            {
                return registrations;
            }

            var created = new Dictionary<string, ISwitchableJsonConfiguration>(StringComparer.Ordinal);
            builder.Properties[RegisteredRuntimeHandlesKey] = created;
            return created;
        }

        private static Dictionary<string, SwitchableJsonConfigurationSource> GetRegisteredSources(
            IHostApplicationBuilder builder)
        {
            if (builder.Properties.TryGetValue(RegisteredSourcesKey, out object? value) &&
                value is Dictionary<string, SwitchableJsonConfigurationSource> registrations)
            {
                return registrations;
            }

            var created = new Dictionary<string, SwitchableJsonConfigurationSource>(StringComparer.Ordinal);
            builder.Properties[RegisteredSourcesKey] = created;
            return created;
        }
    }
}
