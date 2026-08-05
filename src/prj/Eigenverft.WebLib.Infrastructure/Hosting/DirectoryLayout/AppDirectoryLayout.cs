using System;
using System.Collections.Generic;
using System.IO;

namespace Eigenverft.WebLib.Infrastructure.Hosting.DirectoryLayout
{
    /// <summary>
    /// Represents a resolved executable-root directory layout with named writable child directories.
    /// </summary>
    public sealed class AppDirectoryLayout
    {
        /// <summary>
        /// Initializes a new instance of <see cref="AppDirectoryLayout"/>.
        /// </summary>
        /// <param name="rootPath">The executable-root directory path.</param>
        /// <param name="directoriesByKey">Resolved directory paths by semantic key.</param>
        public AppDirectoryLayout(string rootPath, IReadOnlyDictionary<string, string> directoriesByKey)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                throw new ArgumentException("Root path must not be null/empty.", nameof(rootPath));
            }

            RootPath = rootPath;
            GetByKey = directoriesByKey ?? throw new ArgumentNullException(nameof(directoriesByKey));
        }

        /// <summary>Gets the executable-root directory path.</summary>
        public string RootPath { get; }

        /// <summary>Gets resolved directory paths by semantic key.</summary>
        public IReadOnlyDictionary<string, string> GetByKey { get; }

        /// <summary>Gets a directory path by custom semantic key.</summary>
        /// <param name="key">The semantic key.</param>
        public string this[string key] => Get(key);

        /// <summary>Gets a directory path by standard typed key.</summary>
        /// <param name="directory">The standard directory key.</param>
        public string this[DefaultDirectory directory] => Get(directory);

        /// <summary>
        /// Tries to find the mapped static-web directory by its conventional <c>wwwroot</c> leaf name.
        /// </summary>
        /// <param name="directoryPath">The resolved web-root path if present.</param>
        /// <returns><c>true</c> when a web-root directory is present; otherwise <c>false</c>.</returns>
        public bool TryGetWebRoot(out string directoryPath)
        {
            foreach (string path in GetByKey.Values)
            {
                string trimmed = Path.TrimEndingDirectorySeparator(path);
                string leaf = Path.GetFileName(trimmed);

                if (string.Equals(leaf, "wwwroot", StringComparison.OrdinalIgnoreCase))
                {
                    directoryPath = path;
                    return true;
                }
            }

            directoryPath = string.Empty;
            return false;
        }

        /// <summary>Gets a directory path by standard typed key.</summary>
        /// <param name="directory">The standard directory key.</param>
        /// <returns>The resolved directory path.</returns>
        public string Get(DefaultDirectory directory)
        {
            return Get(directory.GetKey());
        }

        /// <summary>Gets a directory path by custom semantic key.</summary>
        /// <param name="key">The semantic key.</param>
        /// <returns>The resolved directory path.</returns>
        /// <exception cref="ArgumentException">The key is null, empty, or whitespace.</exception>
        /// <exception cref="KeyNotFoundException">The key is not configured.</exception>
        public string Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Key must not be null/empty.", nameof(key));
            }

            if (!GetByKey.TryGetValue(key, out string? path))
            {
                throw new KeyNotFoundException(
                    $"Directory key '{key}' is not configured. Known keys: {string.Join(", ", GetByKey.Keys)}");
            }

            return path;
        }

        /// <summary>Tries to get a directory path by standard typed key.</summary>
        /// <param name="directory">The standard directory key.</param>
        /// <param name="directoryPath">The resolved directory path if found.</param>
        /// <returns><c>true</c> when the directory is present; otherwise <c>false</c>.</returns>
        public bool TryGet(DefaultDirectory directory, out string directoryPath)
        {
            return TryGet(directory.GetKey(), out directoryPath);
        }

        /// <summary>Tries to get a directory path by custom semantic key.</summary>
        /// <param name="key">The semantic key.</param>
        /// <param name="directoryPath">The resolved directory path if found.</param>
        /// <returns><c>true</c> when the key is present; otherwise <c>false</c>.</returns>
        public bool TryGet(string key, out string directoryPath)
        {
            directoryPath = string.Empty;

            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (GetByKey.TryGetValue(key, out string? found) && !string.IsNullOrEmpty(found))
            {
                directoryPath = found;
                return true;
            }

            return false;
        }
    }
}
