using System;
using System.IO;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.JsonSettings
{
    /// <summary>
    /// Adds common and environment-specific JSON files whose encoded values are decoded in memory.
    /// </summary>
    public static class DecodedEnvironmentJsonConfigurationExtensions
    {
        /// <summary>
        /// Adds a common decoded JSON provider followed by the matching environment-specific provider.
        /// </summary>
        /// <param name="builder">The configuration builder receiving the JSON providers.</param>
        /// <param name="commonJsonFilePath">
        /// The common JSON file path. Relative paths are resolved against the host content root.
        /// </param>
        /// <param name="hostEnvironment">The host environment that supplies the environment name.</param>
        /// <param name="optionalCommon">Whether the common file may be absent.</param>
        /// <param name="optionalEnvironment">Whether the environment-specific file may be absent.</param>
        /// <param name="reloadOnChange">Whether the providers reload their files after changes.</param>
        /// <returns>The same configuration builder for chaining.</returns>
        /// <remarks>
        /// Providers are appended to the existing source collection. The environment-specific file therefore
        /// overrides the common file, and both override providers that were already present.
        /// </remarks>
        public static IConfigurationBuilder AddEnvironmentJsonSettingsWithDecodedValues(
            this IConfigurationBuilder builder,
            string commonJsonFilePath,
            IHostEnvironment hostEnvironment,
            bool optionalCommon = false,
            bool optionalEnvironment = true,
            bool reloadOnChange = true)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(commonJsonFilePath);
            ArgumentNullException.ThrowIfNull(hostEnvironment);

            string resolvedCommonPath = EnvironmentJsonFileResolver.ResolveCommonPath(
                commonJsonFilePath,
                hostEnvironment.ContentRootPath);

            builder.AddJsonFileWithDecodedValues(
                resolvedCommonPath,
                optional: optionalCommon,
                reloadOnChange: reloadOnChange);

            if (EnvironmentJsonFileResolver.TryResolve(
                    resolvedCommonPath,
                    hostEnvironment.EnvironmentName,
                    out string environmentJsonFilePath))
            {
                builder.AddJsonFileWithDecodedValues(
                    environmentJsonFilePath,
                    optional: false,
                    reloadOnChange: reloadOnChange);
            }
            else if (!optionalEnvironment)
            {
                throw new FileNotFoundException(
                    "Environment-specific JSON settings file not found.",
                    EnvironmentJsonFileResolver.GetExpectedPath(
                        resolvedCommonPath,
                        hostEnvironment.EnvironmentName));
            }

            return builder;
        }
    }
}
