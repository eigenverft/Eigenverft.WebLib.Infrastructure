using System;
using System.Linq;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.Sources
{
    /// <summary>
    /// Provides configuration-source operations for <see cref="WebApplicationBuilder"/>.
    /// </summary>
    public static class WebApplicationBuilderConfigurationExtensions
    {
        /// <summary>
        /// Clears all configuration sources and adds the selected minimal process-level sources.
        /// </summary>
        /// <param name="builder">The builder to modify.</param>
        /// <param name="includeCommandLineArguments">
        /// Whether to add command-line configuration from the current process arguments, excluding the executable path.
        /// </param>
        /// <param name="includeEnvironmentVariables">Whether to add environment-variable configuration.</param>
        /// <returns>The same builder instance for chaining.</returns>
        /// <remarks>
        /// Call this method before adding other configuration providers because it clears the existing source collection.
        /// Environment variables are added before command-line arguments, so command-line values have higher precedence.
        /// </remarks>
        public static WebApplicationBuilder ResetToMinimalConfigurationSources(
            this WebApplicationBuilder builder,
            bool includeCommandLineArguments = false,
            bool includeEnvironmentVariables = true)
        {
            ArgumentNullException.ThrowIfNull(builder);

            ((IConfigurationBuilder)builder.Configuration).Sources.Clear();

            if (includeEnvironmentVariables)
            {
                builder.Configuration.AddEnvironmentVariables();
            }

            if (includeCommandLineArguments)
            {
                string[] args = Environment.GetCommandLineArgs().Skip(1).ToArray();
                builder.Configuration.AddCommandLine(args);
            }

            return builder;
        }
    }
}
