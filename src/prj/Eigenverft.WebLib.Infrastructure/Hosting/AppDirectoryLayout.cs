using System;
using System.Collections.Generic;
using System.IO;

namespace Eigenverft.WebLib.Infrastructure.Hosting
{
    /// <summary>
    /// Resolved writable root plus named directory paths under that root.
    /// </summary>
    public sealed class AppDirectoryLayout
    {
        /// <summary>
        /// Initializes a new instance of <see cref="AppDirectoryLayout"/>.
        /// </summary>
        /// <param name="rootPath">Writable per-executable root directory path.</param>
        /// <param name="directoriesByKey">Resolved directory paths by semantic key (case-insensitive).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="directoriesByKey"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="rootPath"/> is <c>null</c>, empty, or whitespace.</exception>
        public AppDirectoryLayout(string rootPath, IReadOnlyDictionary<string, string> directoriesByKey)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                throw new ArgumentException("Root path must not be null/empty.", nameof(rootPath));
            }

            RootPath = rootPath;
            GetByKey = directoriesByKey ?? throw new ArgumentNullException(nameof(directoriesByKey));
        }

        /// <summary>
        /// Writable per-executable root directory path.
        /// </summary>
        public string RootPath { get; }

        /// <summary>
        /// Resolved directory paths by semantic key (case-insensitive).
        /// </summary>
        public IReadOnlyDictionary<string, string> GetByKey { get; }

        /// <summary>
        /// Gets a directory path by semantic key.
        /// </summary>
        /// <param name="key">The semantic key.</param>
        /// <returns>The resolved directory path.</returns>
        /// <remarks>
        /// This indexer is equivalent to calling <see cref="Get(string)"/>.
        /// </remarks>
        /// <example>
        /// <code>
        /// var logsPath = layout["Logs"];
        /// </code>
        /// </example>
        public string this[string key] => Get(key);

        /// <summary>
        /// Tries to find a directory whose leaf folder name is <c>wwwroot</c> (case-insensitive).
        /// </summary>
        /// <param name="directoryPath">The resolved directory path if present.</param>
        /// <returns><c>true</c> if a web root directory exists in the layout; otherwise <c>false</c>.</returns>
        /// <remarks>
        /// Convention: if any mapped directory ends with "wwwroot", treat it as web root.
        /// The check uses the leaf folder name to support mappings like <c>Static\wwwroot</c> as well.
        /// </remarks>
        public bool TryGetWebRoot(out string directoryPath)
        {
            foreach (var path in GetByKey.Values)
            {
                var trimmed = Path.TrimEndingDirectorySeparator(path);
                var leaf = Path.GetFileName(trimmed);

                if (string.Equals(leaf, "wwwroot", StringComparison.OrdinalIgnoreCase))
                {
                    directoryPath = path;
                    return true;
                }
            }

            directoryPath = string.Empty;
            return false;
        }

        /// <summary>
        /// Gets a directory path by key.
        /// </summary>
        /// <param name="key">The semantic key.</param>
        /// <returns>The resolved directory path.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="key"/> is <c>null</c>, empty, or whitespace.
        /// </exception>
        /// <exception cref="KeyNotFoundException">
        /// Thrown when the key is not present in <see cref="GetByKey"/>.
        /// </exception>
        public string Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Key must not be null/empty.", nameof(key));
            }

            if (!GetByKey.TryGetValue(key, out var path))
            {
                throw new KeyNotFoundException($"Directory key '{key}' is not configured. Known keys: {string.Join(", ", GetByKey.Keys)}");
            }

            return path;
        }

        /// <summary>
        /// Tries to get a directory path by key.
        /// </summary>
        /// <param name="key">The semantic key.</param>
        /// <param name="directoryPath">The resolved directory path if found.</param>
        /// <returns><c>true</c> if the key is present; otherwise <c>false</c>.</returns>
        public bool TryGet(string key, out string directoryPath)
        {
            directoryPath = string.Empty;

            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (GetByKey.TryGetValue(key, out var found) && !string.IsNullOrEmpty(found))
            {
                directoryPath = found;
                return true;
            }

            return false;
        }
    }
}