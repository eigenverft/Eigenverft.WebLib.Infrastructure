using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout;

using Microsoft.AspNetCore.Builder;

namespace Eigenverft.WebLib.Infrastructure.Hosting.DirectoryLayout
{
    /// <summary>
    /// Creates ASP.NET Core builders that use the shared NetLib application directory layout.
    /// </summary>
    public static class WebApplicationBuilderFactory
    {
        private const string WebKey = "Web";
        private const string StandardWebFolderName = "wwwroot";

        /// <summary>
        /// Creates a web application builder with the standard application directories and <c>wwwroot</c>.
        /// </summary>
        /// <param name="args">Optional command-line arguments passed to ASP.NET Core.</param>
        public static WebApplicationBuilder CreateWithDefaultDirectory(string[]? args = null)
        {
            return CreateWithDefaultDirectory(BuildDefaultMap(), args);
        }

        /// <summary>
        /// Creates a web application builder with the standard application directories and typed folder-name overrides.
        /// </summary>
        public static WebApplicationBuilder CreateWithDefaultDirectory(
            IReadOnlyDictionary<DefaultDirectory, string> directoryOverrides,
            string[]? args = null,
            bool strictWwwrootName = true)
        {
            if (directoryOverrides is null)
            {
                throw new ArgumentNullException(nameof(directoryOverrides));
            }

            var folderMap = new Dictionary<string, string>(BuildDefaultMap(), StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<DefaultDirectory, string> entry in directoryOverrides)
            {
                folderMap[entry.Key.GetKey()] = entry.Value;
            }

            return CreateWithDefaultDirectory(folderMap, args, strictWwwrootName);
        }

        /// <summary>
        /// Creates a web application builder from a custom semantic directory map.
        /// </summary>
        public static WebApplicationBuilder CreateWithDefaultDirectory(
            IReadOnlyDictionary<string, string> folderMap,
            string[]? args = null,
            bool strictWwwrootName = true)
        {
            if (folderMap is null)
            {
                throw new ArgumentNullException(nameof(folderMap));
            }

            if (folderMap.Count == 0)
            {
                throw new ArgumentException("folderMap must not be empty.", nameof(folderMap));
            }

            var effectiveMap = new Dictionary<string, string>(folderMap, StringComparer.OrdinalIgnoreCase);
            EnsureCanonicalWebMapping(effectiveMap, strictWwwrootName);

            string contentRootPath = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args ?? Array.Empty<string>(),
                ContentRootPath = contentRootPath,
                WebRootPath = Path.Combine(contentRootPath, effectiveMap[WebKey]),
            });

            // All generic layout creation, validation, writable probing and DI registration live in NetLib.
            builder.AddDirectoryLayout(effectiveMap);

            return builder;
        }

        private static IReadOnlyDictionary<string, string> BuildDefaultMap()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (DefaultDirectory directory in Enum.GetValues(typeof(DefaultDirectory)))
            {
                result[directory.GetKey()] = directory.GetDefaultFolderName();
            }

            result[WebKey] = StandardWebFolderName;
            return result;
        }

        private static void EnsureCanonicalWebMapping(
            Dictionary<string, string> folderMap,
            bool strictWwwrootName)
        {
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (folderMap.TryGetValue(WebKey, out string? configuredWeb))
            {
                if (strictWwwrootName && !string.Equals(configuredWeb, StandardWebFolderName, comparison))
                {
                    throw new InvalidOperationException(
                        $"folderMap['{WebKey}'] is configured as '{configuredWeb}', but the standard web-root folder is '{StandardWebFolderName}'. " +
                        "Pass strictWwwrootName: false to allow a non-standard web-root folder name.");
                }

                return;
            }

            string[] wwwrootKeys = folderMap
                .Where(entry => string.Equals(entry.Value, StandardWebFolderName, comparison))
                .Select(entry => entry.Key)
                .ToArray();

            if (wwwrootKeys.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Multiple folder mappings point to '{StandardWebFolderName}', so WebRootPath is ambiguous. Keys: {string.Join(", ", wwwrootKeys)}.");
            }

            folderMap[WebKey] = StandardWebFolderName;
        }
    }
}
