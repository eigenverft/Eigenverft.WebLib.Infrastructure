using System;
using System.Linq;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson
{
    /// <summary>Registers switchable JSON configuration sources and their runtime control handles.</summary>
    public static class SwitchableJsonConfigurationExtensions
    {
        /// <summary>
        /// Adds one JSON configuration source that can later switch to another JSON file through keyed dependency injection.
        /// </summary>
        /// <param name="builder">The host application builder receiving both the configuration source and runtime handle.</param>
        /// <param name="name">Caller-defined provider identity and keyed-service key.</param>
        /// <param name="initialPath">Initial JSON path, absolute or relative to the host content root.</param>
        /// <param name="optional">Whether a missing initial source produces an empty initial provider instead of startup failure.</param>
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
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentException.ThrowIfNullOrWhiteSpace(initialPath);

            if (reloadDelayMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(reloadDelayMilliseconds));
            }

            if (builder.Services.Any(descriptor =>
                    descriptor.ServiceType == typeof(ISwitchableJsonConfiguration) &&
                    descriptor.IsKeyedService &&
                    Equals(descriptor.ServiceKey, name)))
            {
                throw new InvalidOperationException(
                    $"A switchable JSON configuration source named '{name}' is already registered.");
            }

            var provider = new SwitchableJsonConfigurationProvider(
                name,
                builder.Environment.ContentRootPath,
                initialPath,
                optional,
                reloadOnChange,
                reloadDelayMilliseconds,
                runtimeFailurePolicy);

            IConfigurationBuilder configurationBuilder = builder.Configuration;
            var source = new SwitchableJsonConfigurationSource(provider);

            // ConfigurationManager mutates Sources before it builds/loads the provider. If the initial Load throws, the source
            // therefore remains in Sources unless we remove it ourselves. Treat the whole registration as a tiny transaction:
            // publish both the configuration source and its keyed runtime alias, or publish neither. The provider is disposed on
            // rollback because reloadOnChange may already have prepared a physical watcher before initial JSON loading failed.
            try
            {
                configurationBuilder.Add(source);

                // Keyed DI is used instead of a custom global registry so multiple independent sources remain addressable through
                // the standard Microsoft DI container. Strongly typed handles or a registry can be layered on top later if a caller
                // wants domain-specific identities. This registration is an alias to the same provider instance returned by Source.Build.
                builder.Services.AddKeyedSingleton<ISwitchableJsonConfiguration>(name, provider);
            }
            catch
            {
                _ = configurationBuilder.Sources.Remove(source);
                provider.Dispose();
                throw;
            }

            return builder;
        }
    }
}
