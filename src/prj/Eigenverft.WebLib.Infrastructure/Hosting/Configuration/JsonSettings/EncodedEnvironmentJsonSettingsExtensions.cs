using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.JsonSettings
{
    /// <summary>
    /// Composes JSON file encoding with environment-aware decoded configuration loading.
    /// </summary>
    public static class EncodedEnvironmentJsonSettingsExtensions
    {
        /// <summary>
        /// Encodes matching values in common and environment-specific JSON files, then adds decoded providers.
        /// </summary>
        /// <param name="builder">The web application builder receiving the JSON providers.</param>
        /// <param name="commonJsonFilePath">
        /// The required common JSON file path. Relative paths are resolved against the host content root.
        /// </param>
        /// <param name="keyPathPatterns">Case-insensitive glob patterns for complete configuration paths.</param>
        /// <param name="codec">The reversible codec used for persistence encoding and in-memory decoding.</param>
        /// <param name="optionalEnvironment">Whether the environment-specific file may be absent.</param>
        /// <param name="reloadOnChange">
        /// Whether the providers reload their files after changes. The default is <see langword="false"/> because this method
        /// encodes matching values only during startup; enabling reload does not automatically re-encode later clear-text file edits.
        /// </param>
        /// <param name="nullAsEmpty">Whether matching JSON <see langword="null"/> values are encoded as empty strings.</param>
        /// <returns>The builder's <see cref="ConfigurationManager"/> for chaining.</returns>
        /// <remarks>
        /// This method intentionally mutates existing JSON files before adding providers. Use
        /// <see cref="DecodedEnvironmentJsonConfigurationExtensions.AddEnvironmentJsonSettingsWithDecodedValues"/>
        /// for load-only scenarios. Added JSON providers have higher precedence than configuration providers
        /// already present on <paramref name="builder"/>.
        /// </remarks>
        public static ConfigurationManager EncodeAndAddEnvironmentJsonSettings(
            this WebApplicationBuilder builder,
            string commonJsonFilePath,
            IEnumerable<string> keyPathPatterns,
            JsonSettingsValueCodec codec,
            bool optionalEnvironment = true,
            bool reloadOnChange = false,
            bool nullAsEmpty = true)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(commonJsonFilePath);
            ArgumentNullException.ThrowIfNull(keyPathPatterns);
            ArgumentNullException.ThrowIfNull(codec);

            string[] patterns = keyPathPatterns.ToArray();
            string resolvedCommonPath = EnvironmentJsonFileResolver.ResolveCommonPath(
                commonJsonFilePath,
                builder.Environment.ContentRootPath);

            _ = JsonSettingsFileEncoder.EncodeMatchingValuesInPlace(
                resolvedCommonPath,
                patterns,
                codec,
                nullAsEmpty);

            if (EnvironmentJsonFileResolver.TryResolve(
                    resolvedCommonPath,
                    builder.Environment.EnvironmentName,
                    out string environmentJsonFilePath))
            {
                _ = JsonSettingsFileEncoder.EncodeMatchingValuesInPlace(
                    environmentJsonFilePath,
                    patterns,
                    codec,
                    nullAsEmpty);
            }

            ((IConfigurationBuilder)builder.Configuration)
                .AddEnvironmentJsonSettingsWithDecodedValues(
                    resolvedCommonPath,
                    builder.Environment,
                    optionalCommon: false,
                    optionalEnvironment: optionalEnvironment,
                    reloadOnChange: reloadOnChange,
                    decodeCodec: codec);

            return builder.Configuration;
        }


        /// <summary>
        /// Encodes matching values using one key-path pattern, then adds decoded providers.
        /// </summary>
        /// <param name="builder">The web application builder receiving the JSON providers.</param>
        /// <param name="commonJsonFilePath">
        /// The required common JSON file path. Relative paths are resolved against the host content root.
        /// </param>
        /// <param name="keyPathPattern">A case-insensitive glob pattern for complete configuration paths.</param>
        /// <param name="codec">The reversible codec used for persistence encoding and in-memory decoding.</param>
        /// <param name="optionalEnvironment">Whether the environment-specific file may be absent.</param>
        /// <param name="reloadOnChange">
        /// Whether the providers reload their files after changes. The default is <see langword="false"/> because this method
        /// encodes matching values only during startup; enabling reload does not automatically re-encode later clear-text file edits.
        /// </param>
        /// <param name="nullAsEmpty">Whether matching JSON <see langword="null"/> values are encoded as empty strings.</param>
        /// <returns>The builder's <see cref="ConfigurationManager"/> for chaining.</returns>
        public static ConfigurationManager EncodeAndAddEnvironmentJsonSettings(
            this WebApplicationBuilder builder,
            string commonJsonFilePath,
            string keyPathPattern,
            JsonSettingsValueCodec codec,
            bool optionalEnvironment = true,
            bool reloadOnChange = false,
            bool nullAsEmpty = true)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(keyPathPattern);

            return builder.EncodeAndAddEnvironmentJsonSettings(
                commonJsonFilePath,
                new[] { keyPathPattern },
                codec,
                optionalEnvironment,
                reloadOnChange,
                nullAsEmpty);
        }

    }
}
