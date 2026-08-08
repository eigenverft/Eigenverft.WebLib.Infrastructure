using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.JsonSettings
{
    /// <summary>
    /// Adds common and environment-specific JSON providers through the generic source-preparation contract.
    /// </summary>
    public static class PreparedEnvironmentJsonSettingsExtensions
    {
        /// <summary>
        /// Adds a required common JSON file and an optional environment-specific override without mutating either file on disk.
        /// </summary>
        /// <remarks>
        /// Every provider parses into an isolated candidate snapshot, runs the supplied preparations in order and publishes the
        /// candidate only when every preparation succeeds. Existing EncodeAndAddEnvironmentJsonSettings behavior is independent.
        /// </remarks>
        public static ConfigurationManager AddPreparedEnvironmentJsonSettings(
            this WebApplicationBuilder builder,
            string commonJsonFilePath,
            IEnumerable<IJsonConfigurationSourcePreparation> sourcePreparations,
            bool optionalEnvironment = true,
            bool reloadOnChange = false)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(commonJsonFilePath);
            ArgumentNullException.ThrowIfNull(sourcePreparations);

            IJsonConfigurationSourcePreparation[] preparations = sourcePreparations.ToArray();
            string resolvedCommonPath = EnvironmentJsonFileResolver.ResolveCommonPath(
                commonJsonFilePath,
                builder.Environment.ContentRootPath);

            ((IConfigurationBuilder)builder.Configuration).AddPreparedJsonFile(
                resolvedCommonPath,
                preparations,
                optional: false,
                reloadOnChange: reloadOnChange);

            if (EnvironmentJsonFileResolver.TryResolve(
                    resolvedCommonPath,
                    builder.Environment.EnvironmentName,
                    out string environmentJsonFilePath))
            {
                ((IConfigurationBuilder)builder.Configuration).AddPreparedJsonFile(
                    environmentJsonFilePath,
                    preparations,
                    optional: false,
                    reloadOnChange: reloadOnChange);
            }
            else if (!optionalEnvironment)
            {
                throw new FileNotFoundException(
                    "Environment-specific JSON settings file not found.",
                    EnvironmentJsonFileResolver.GetExpectedPath(
                        resolvedCommonPath,
                        builder.Environment.EnvironmentName));
            }

            return builder.Configuration;
        }

        /// <summary>Adds prepared environment JSON settings using one preparation step.</summary>
        public static ConfigurationManager AddPreparedEnvironmentJsonSettings(
            this WebApplicationBuilder builder,
            string commonJsonFilePath,
            IJsonConfigurationSourcePreparation sourcePreparation,
            bool optionalEnvironment = true,
            bool reloadOnChange = false)
        {
            ArgumentNullException.ThrowIfNull(sourcePreparation);
            return builder.AddPreparedEnvironmentJsonSettings(
                commonJsonFilePath,
                new[] { sourcePreparation },
                optionalEnvironment,
                reloadOnChange);
        }
    }
}
