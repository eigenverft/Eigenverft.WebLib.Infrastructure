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

            var runtime = new SwitchableJsonConfigurationRuntime(
                name,
                builder.Environment.ContentRootPath,
                initialPath,
                optional,
                reloadOnChange,
                reloadDelayMilliseconds,
                runtimeFailurePolicy);

            IConfigurationBuilder configurationBuilder = builder.Configuration;
            var source = new SwitchableJsonConfigurationSource(runtime);

            // ConfigurationManager inserts a source before Build/Load. If initial loading fails, remove the inserted source so the
            // registration remains all-or-nothing. Remove() can rebuild the remaining provider stack; that is safe because every
            // switchable source now follows IConfigurationSource ownership and returns a fresh provider instance from Build().
            try
            {
                configurationBuilder.Add(source);

                // Keyed DI is used instead of a custom registry so multiple independent sources remain addressable through the
                // standard Microsoft container. The runtime handle survives framework-driven concrete-provider rebuilds.
                // Register through a factory so the DI container owns and disposes the stable runtime handle. Concrete
                // IConfigurationProvider instances are framework-owned and may be replaced/removed independently.
                builder.Services.AddKeyedSingleton<ISwitchableJsonConfiguration>(
                    name,
                    (_, _) => runtime);
            }
            catch
            {
                _ = configurationBuilder.Sources.Remove(source);
                runtime.Dispose();
                throw;
            }

            return builder;
        }
    }
}
