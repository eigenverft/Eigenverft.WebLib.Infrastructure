using System;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.JsonSettings
{
    /// <summary>
    /// Adds a common JSON settings file followed by its environment-specific override.
    /// </summary>
    public static class EnvironmentJsonConfigurationExtensions
    {
        /// <summary>
        /// Adds a common JSON file and the matching <c>.{Environment}</c> file when present.
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
        /// For <c>appsettings.json</c> in the <c>Production</c> environment, the override name is
        /// <c>appsettings.Production.json</c>. The override provider is added last and therefore has
        /// higher precedence than the common file and all providers already present on
        /// <paramref name="builder"/>.
        /// </remarks>
        public static IConfigurationBuilder AddEnvironmentJsonSettings(
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

            builder.AddJsonFile(
                path: resolvedCommonPath,
                optional: optionalCommon,
                reloadOnChange: reloadOnChange);

            if (EnvironmentJsonFileResolver.TryResolve(
                    resolvedCommonPath,
                    hostEnvironment.EnvironmentName,
                    out string environmentJsonFilePath))
            {
                builder.AddJsonFile(
                    path: environmentJsonFilePath,
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

    internal static class EnvironmentJsonFileResolver
    {
        public static string ResolveCommonPath(string commonJsonFilePath, string contentRootPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(commonJsonFilePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

            return Path.IsPathFullyQualified(commonJsonFilePath)
                ? Path.GetFullPath(commonJsonFilePath)
                : Path.GetFullPath(Path.Combine(contentRootPath, commonJsonFilePath));
        }

        public static bool TryResolve(
            string commonJsonFilePath,
            string? environmentName,
            out string environmentJsonFilePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(commonJsonFilePath);

            environmentJsonFilePath = string.Empty;

            if (string.IsNullOrWhiteSpace(environmentName))
            {
                return false;
            }

            string? directoryPath = Path.GetDirectoryName(commonJsonFilePath);

            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return false;
            }

            string expectedFileName = Path.GetFileName(
                GetExpectedPath(commonJsonFilePath, environmentName));

            string[] matchingPaths = Directory
                .EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly)
                .Where(path => string.Equals(
                    Path.GetFileName(path),
                    expectedFileName,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (matchingPaths.Length == 0)
            {
                return false;
            }

            if (matchingPaths.Length > 1)
            {
                throw new InvalidOperationException(
                    "Multiple environment-specific JSON settings files differ only by casing: " +
                    string.Join(", ", matchingPaths));
            }

            environmentJsonFilePath = matchingPaths[0];
            return true;
        }

        public static string GetExpectedPath(string commonJsonFilePath, string? environmentName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(commonJsonFilePath);

            string directoryPath = Path.GetDirectoryName(commonJsonFilePath) ?? string.Empty;
            string fileName = Path.GetFileName(commonJsonFilePath);
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);

            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".json";
            }

            return Path.Combine(directoryPath, $"{baseName}.{environmentName ?? string.Empty}{extension}");
        }
    }
}
