using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Eigenverft.WebLib.Infrastructure.Hosting.DirectoryLayout
{
    /// <summary>
    /// Creates a <see cref="WebApplicationBuilder"/> and applies project-wide defaults.
    /// </summary>
    /// <remarks>
    /// This factory enforces an executable-folder rooted directory layout:
    /// every mapped folder is created as a direct subfolder of <see cref="AppContext.BaseDirectory"/>.
    ///
    /// Static files: a canonical key "Web" is always available in the resulting <see cref="AppDirectoryLayout"/>.
    /// By default, if the caller explicitly uses the key "Web", it must map to "wwwroot" to avoid build/launch target
    /// dependent behavior in Visual Studio. If the caller omits "Web", the factory will:
    /// - detect a mapping whose value equals "wwwroot" and add an alias key "Web", or
    /// - inject "Web" => "wwwroot" if no such mapping exists.
    ///
    /// Usage (pre-Build and post-Build):
    /// <code>
    /// // Create + access layout before Build()
    /// var builder = WebApplicationBuilderFactory.CreateWithDefaultDirectory(
    ///     folderMap: new Dictionary&lt;string, string&gt;
    ///     {
    ///         ["Logs"] = "log",
    ///         ["Data"] = "Data",
    ///         ["WebFolder"] = "wwwroot",
    ///     },
    ///     includeCommandLineArgs: true);
    ///
    /// var layoutPreBuild = builder.GetDirectoryLayout();
    ///
    /// // Build + access layout via DI
    /// var app = builder.Build();
    /// var layoutFromDi = app.Services.GetRequiredService&lt;AppDirectoryLayout&gt;();
    ///
    /// app.Run();
    /// </code>
    /// </remarks>
    public static class WebApplicationBuilderFactory
    {
        private static readonly string WebKey = DefaultDirectory.Web.GetKey();
        private static readonly string StandardWebFolderName = DefaultDirectory.Web.GetDefaultFolderName();

        /// <summary>
        /// Creates a <see cref="WebApplicationBuilder"/> using the standard directory set with typed overrides.
        /// </summary>
        /// <param name="directoryOverrides">Folder-name overrides; unspecified standard entries retain their defaults.</param>
        /// <param name="includeCommandLineArgs">Whether command-line arguments are included.</param>
        /// <param name="strictWwwrootName">Whether the web directory must remain named <c>wwwroot</c>.</param>
        /// <returns>A configured <see cref="WebApplicationBuilder"/>.</returns>
        public static WebApplicationBuilder CreateWithDefaultDirectory(
            IReadOnlyDictionary<DefaultDirectory, string> directoryOverrides,
            bool includeCommandLineArgs = true,
            bool strictWwwrootName = true)
        {
            if (directoryOverrides is null)
            {
                throw new ArgumentNullException(nameof(directoryOverrides));
            }

            var folderMap = new Dictionary<string, string>(BuildDefaultMap(), StringComparer.OrdinalIgnoreCase);

            foreach (var entry in directoryOverrides)
            {
                folderMap[entry.Key.GetKey()] = entry.Value;
            }

            return CreateWithDefaultDirectory(folderMap, includeCommandLineArgs, strictWwwrootName);
        }

        /// <summary>
        /// Creates a <see cref="WebApplicationBuilder"/> using either the library defaults or a custom semantic-key map.
        /// </summary>
        /// <param name="folderMap">Custom semantic keys and direct-child folder names, or <c>null</c> for the standard set.</param>
        /// <param name="includeCommandLineArgs">Whether command-line arguments are included.</param>
        /// <param name="strictWwwrootName">Whether an explicit web mapping must remain named <c>wwwroot</c>.</param>
        /// <returns>A configured <see cref="WebApplicationBuilder"/>.</returns>
        public static WebApplicationBuilder CreateWithDefaultDirectory(
            IReadOnlyDictionary<string, string>? folderMap = null,
            bool includeCommandLineArgs = true,
            bool strictWwwrootName = true)
        {
            folderMap ??= BuildDefaultMap();

            var exeRoot = GetExecutableRootDirectory();
            var normalized = NormalizeAndValidateMap(folderMap);

            // Ensure a canonical "Web" mapping exists (by value detection or injection).
            EnsureCanonicalWebMapping(normalized, strictWwwrootName);

            // Resolve absolute paths under exeRoot, create them, verify write access.
            var resolved = ResolveAndEnsureDirectories(exeRoot, normalized);

            // Assign WebRootPath on the builder (must).
            var args = includeCommandLineArgs ? GetCommandLineArgsWithoutExePath() : Array.Empty<string>();
            var webRootPath = Path.GetFullPath(Path.Combine(exeRoot, normalized[WebKey]));

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args,
                ContentRootPath = exeRoot,
                WebRootPath = webRootPath
            });

            // Attach layout using the shared extension key (pre-Build access) + DI (post-Build access).
            var layout = new AppDirectoryLayout(exeRoot, resolved);

            builder.SetDirectoryLayout(layout);
            builder.Services.AddSingleton(layout);

            return builder;
        }

        private static IReadOnlyDictionary<string, string> BuildDefaultMap()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (DefaultDirectory directory in Enum.GetValues(typeof(DefaultDirectory)))
            {
                result[directory.GetKey()] = directory.GetDefaultFolderName();
            }

            return result;
        }

        private static string GetExecutableRootDirectory()
        {
            var root = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

            if (string.IsNullOrWhiteSpace(root))
                throw new IOException("Unable to determine executable base directory via AppContext.BaseDirectory.");

            return root;
        }

        private static string[] GetCommandLineArgsWithoutExePath()
        {
            return Environment.GetCommandLineArgs().Skip(1).ToArray();
        }

        private static Dictionary<string, string> NormalizeAndValidateMap(IReadOnlyDictionary<string, string> input)
        {
            if (input.Count == 0)
                throw new ArgumentException("folderMap must not be empty.", nameof(input));

            // Semantic keys normalized case-insensitively for ergonomics.
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in input)
            {
                var key = kvp.Key;
                var folderName = kvp.Value;

                if (string.IsNullOrWhiteSpace(key))
                    throw new ArgumentException("folderMap contains an empty key.", nameof(input));

                if (string.IsNullOrWhiteSpace(folderName))
                    throw new ArgumentException($"folderMap['{key}'] is null/empty.", nameof(input));

                folderName = folderName.Trim();

                // Requirement: direct subfolder only => single segment, no separators.
                if (folderName.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0)
                    throw new ArgumentException(
                        $"folderMap['{key}'] must be a single folder name (no path separators), but was '{folderName}'.",
                        nameof(input));

                if (Path.IsPathRooted(folderName))
                    throw new ArgumentException(
                        $"folderMap['{key}'] must not be rooted, but was '{folderName}'.",
                        nameof(input));

                if (string.Equals(folderName, ".", StringComparison.Ordinal) ||
                    string.Equals(folderName, "..", StringComparison.Ordinal) ||
                    folderName.Contains("..", StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"folderMap['{key}'] must not contain traversal patterns, but was '{folderName}'.",
                        nameof(input));
                }

                result[key.Trim()] = folderName;
            }

            return result;
        }

        private static void EnsureCanonicalWebMapping(
            Dictionary<string, string> normalized,
            bool strictWwwrootName)
        {
            var comparison = GetFileSystemStringComparison();

            // Case 1: caller explicitly provided "Web"
            if (normalized.TryGetValue(WebKey, out var configuredWeb))
            {
                if (strictWwwrootName && !string.Equals(configuredWeb, StandardWebFolderName, comparison))
                {
                    throw new InvalidOperationException(
                        $"Hard warn: folderMap['{WebKey}'] is configured as '{configuredWeb}', but the standard folder name is '{StandardWebFolderName}'. " +
                        $"In Visual Studio (and across different build/launch targets), using a non-standard web root folder name can lead to confusing or inconsistent " +
                        $"static file behavior (static web assets, dev-time hosting, and launch profile differences). " +
                        $"To allow a non-standard name, call CreateWithDefaultDirectory(..., strictWwwrootName: false).");
                }

                return; // "Web" exists; keep as-is (strict may have validated).
            }

            // Case 2: caller did not provide "Web" — detect "wwwroot" by value.
            var wwwrootKeys = normalized
                .Where(kvp => string.Equals(kvp.Value, StandardWebFolderName, comparison))
                .Select(kvp => kvp.Key)
                .ToArray();

            if (wwwrootKeys.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Hard warn: multiple folderMap entries map to '{StandardWebFolderName}', which is ambiguous for WebRootPath assignment. " +
                    $"Keys: {string.Join(", ", wwwrootKeys)}. Configure a single mapping to '{StandardWebFolderName}' or add an explicit '{WebKey}' entry.");
            }

            if (wwwrootKeys.Length == 1)
            {
                // Add alias key so callers can reliably use layout['Web'].
                normalized[WebKey] = StandardWebFolderName;
                return;
            }

            // Case 3: nobody mapped to wwwroot — inject canonical default.
            normalized[WebKey] = StandardWebFolderName;
        }

        private static StringComparison GetFileSystemStringComparison()
        {
            // Windows: typically case-insensitive, Linux: typically case-sensitive.
            return OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        }

        private static Dictionary<string, string> ResolveAndEnsureDirectories(
            string exeRoot,
            IReadOnlyDictionary<string, string> normalized)
        {
            var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in normalized)
            {
                var key = kvp.Key;
                var childFolderName = kvp.Value;

                var fullPath = Path.GetFullPath(Path.Combine(exeRoot, childFolderName));

                EnsureDirectoryExists(fullPath);
                VerifyWritable(fullPath);

                resolved[key] = fullPath;
            }

            return resolved;
        }

        private static void EnsureDirectoryExists(string fullPath)
        {
            try
            {
                Directory.CreateDirectory(fullPath);
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to create directory '{fullPath}'.", ex);
            }
        }

        private static void VerifyWritable(string fullPath)
        {
            var probeName = $".writeprobe_{Guid.NewGuid():N}.tmp";
            var probePath = Path.Combine(fullPath, probeName);

            try
            {
                using (var fs = new FileStream(
                    probePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1,
                    options: FileOptions.None))
                {
                    fs.WriteByte(0);
                    fs.Flush(true);
                }

                try { File.Delete(probePath); } catch { /* best-effort cleanup */ }
            }
            catch (Exception ex)
            {
                throw new IOException($"Directory '{fullPath}' is not writable (write probe failed).", ex);
            }
        }
    }
}
