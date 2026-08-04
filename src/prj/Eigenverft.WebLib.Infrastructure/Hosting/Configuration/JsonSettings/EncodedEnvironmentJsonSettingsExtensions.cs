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
        /// <param name="encode">
        /// The encoder applied to matching clear-text values. Use a member of
        /// <see cref="JsonSettingsValueEncoders"/> for an idempotent, decodable result.
        /// </param>
        /// <param name="optionalEnvironment">Whether the environment-specific file may be absent.</param>
        /// <param name="reloadOnChange">Whether the providers reload their files after changes.</param>
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
            Func<string, string> encode,
            bool optionalEnvironment = true,
            bool reloadOnChange = true,
            bool nullAsEmpty = true)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(commonJsonFilePath);
            ArgumentNullException.ThrowIfNull(keyPathPatterns);
            ArgumentNullException.ThrowIfNull(encode);

            string[] patterns = keyPathPatterns.ToArray();
            string resolvedCommonPath = EnvironmentJsonFileResolver.ResolveCommonPath(
                commonJsonFilePath,
                builder.Environment.ContentRootPath);

            _ = JsonSettingsFileEncoder.EncodeMatchingValuesInPlace(
                resolvedCommonPath,
                patterns,
                encode,
                nullAsEmpty);

            if (EnvironmentJsonFileResolver.TryResolve(
                    resolvedCommonPath,
                    builder.Environment.EnvironmentName,
                    out string environmentJsonFilePath))
            {
                _ = JsonSettingsFileEncoder.EncodeMatchingValuesInPlace(
                    environmentJsonFilePath,
                    patterns,
                    encode,
                    nullAsEmpty);
            }

            ((IConfigurationBuilder)builder.Configuration)
                .AddEnvironmentJsonSettingsWithDecodedValues(
                    resolvedCommonPath,
                    builder.Environment,
                    optionalCommon: false,
                    optionalEnvironment: optionalEnvironment,
                    reloadOnChange: reloadOnChange);

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
        /// <param name="encode">The encoder applied to matching clear-text values.</param>
        /// <param name="optionalEnvironment">Whether the environment-specific file may be absent.</param>
        /// <param name="reloadOnChange">Whether the providers reload their files after changes.</param>
        /// <param name="nullAsEmpty">Whether matching JSON <see langword="null"/> values are encoded as empty strings.</param>
        /// <returns>The builder's <see cref="ConfigurationManager"/> for chaining.</returns>
        public static ConfigurationManager EncodeAndAddEnvironmentJsonSettings(
            this WebApplicationBuilder builder,
            string commonJsonFilePath,
            string keyPathPattern,
            Func<string, string> encode,
            bool optionalEnvironment = true,
            bool reloadOnChange = true,
            bool nullAsEmpty = true)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(keyPathPattern);

            return builder.EncodeAndAddEnvironmentJsonSettings(
                commonJsonFilePath,
                new[] { keyPathPattern },
                encode,
                optionalEnvironment,
                reloadOnChange,
                nullAsEmpty);
        }
    }
}
