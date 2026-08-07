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
        /// <param name="runtimeFailurePolicy">How failed runtime candidate loads are reported after the host is running.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <remarks>
        /// The registration is deliberately on <see cref="IHostApplicationBuilder"/> rather than only
        /// <see cref="IConfigurationBuilder"/> because one operation must add an IConfiguration source and a DI runtime handle.
        /// A split configuration/service registration API is possible, but would make it easier to accidentally register only
        /// one half. The provider identity is otherwise completely agnostic and carries no profile, environment, or directory semantics.
        /// </remarks>
        public static IHostApplicationBuilder AddSwitchableJsonFile(
            this IHostApplicationBuilder builder,
            string name,
            string initialPath,
            bool optional = false,
            SwitchableJsonRuntimeFailurePolicy runtimeFailurePolicy = SwitchableJsonRuntimeFailurePolicy.KeepLastKnownGood)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentException.ThrowIfNullOrWhiteSpace(initialPath);

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
                runtimeFailurePolicy);

            // Add the source first so an invalid required initial file fails before a runtime DI handle is published.
            ((IConfigurationBuilder)builder.Configuration).Add(new SwitchableJsonConfigurationSource(provider));

            // Keyed DI is used instead of a custom global registry so multiple independent sources remain addressable through
            // the standard Microsoft DI container. Strongly typed handles or a registry can be layered on top later if a caller
            // wants domain-specific identities.
            builder.Services.AddKeyedSingleton<ISwitchableJsonConfiguration>(name, provider);

            return builder;
        }
    }
}
