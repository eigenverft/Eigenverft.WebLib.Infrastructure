using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.JsonSettings
{
    /// <summary>
    /// Encodes selected string values directly in JSON settings files.
    /// </summary>
    public static class JsonSettingsFileEncoder
    {
        /// <summary>
        /// Encodes string values whose complete configuration paths match any supplied glob pattern.
        /// </summary>
        /// <param name="jsonFilePath">The JSON file that may be changed.</param>
        /// <param name="keyPathPatterns">
        /// Case-insensitive glob patterns for complete configuration paths, such as
        /// <c>Certificates:*:Password</c>. <c>*</c> matches any sequence and <c>?</c> one character.
        /// Array indexes are represented as path segments.
        /// </param>
        /// <param name="codec">The reversible codec applied to matching clear-text values.</param>
        /// <param name="nullAsEmpty">Whether matching JSON <see langword="null"/> values are encoded as empty strings.</param>
        /// <returns>The number of values changed.</returns>
        /// <exception cref="FileNotFoundException"><paramref name="jsonFilePath"/> does not exist.</exception>
        /// <exception cref="InvalidDataException">The JSON document has no root value.</exception>
        /// <remarks>
        /// The file is rewritten only when at least one value changes. Rewriting uses formatted JSON and therefore
        /// removes JSON comments, trailing commas, and the original whitespace. Any recognized encoded wrapper is left
        /// untouched, even when it was produced by a different codec or cannot currently be decoded. This avoids destroying
        /// potentially recoverable protected data, but also means this method is not a codec-migration engine: changing a
        /// codec, password, key ring, purpose, or composed stage order requires an explicit decode-and-rewrite migration.
        /// </remarks>
        public static int EncodeMatchingValuesInPlace(
            string jsonFilePath,
            IEnumerable<string> keyPathPatterns,
            JsonSettingsValueCodec codec,
            bool nullAsEmpty = true)
        {
            ArgumentNullException.ThrowIfNull(codec);
            return EncodeMatchingValuesInPlaceCore(jsonFilePath, keyPathPatterns, codec.Encode, nullAsEmpty);
        }

        private static int EncodeMatchingValuesInPlaceCore(
            string jsonFilePath,
            IEnumerable<string> keyPathPatterns,
            Func<string, string> encode,
            bool nullAsEmpty)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(jsonFilePath);
            ArgumentNullException.ThrowIfNull(keyPathPatterns);
            ArgumentNullException.ThrowIfNull(encode);

            string[] patterns = keyPathPatterns.ToArray();

            if (patterns.Length == 0 || patterns.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException(
                    "At least one non-empty key-path pattern is required.",
                    nameof(keyPathPatterns));
            }

            if (!File.Exists(jsonFilePath))
            {
                throw new FileNotFoundException("JSON settings file not found.", jsonFilePath);
            }

            string json = File.ReadAllText(jsonFilePath);
            JsonNode? root = JsonNode.Parse(
                json,
                nodeOptions: null,
                documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });

            if (root is null)
            {
                throw new InvalidDataException("Parsed JSON root was null.");
            }

            var matcher = new KeyPathGlobMatcher(patterns);
            int updated = 0;

            WalkAndEncode(
                root,
                currentPath: string.Empty,
                matcher,
                encode,
                nullAsEmpty,
                ref updated);

            if (updated == 0)
            {
                return 0;
            }

            string formattedJson = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            WriteAtomically(jsonFilePath, formattedJson);
            return updated;
        }

        /// <summary>
        /// Encodes string values whose complete configuration paths match one glob pattern.
        /// </summary>
        /// <param name="jsonFilePath">The JSON file that may be changed.</param>
        /// <param name="keyPathPattern">A case-insensitive glob pattern for complete configuration paths.</param>
        /// <param name="codec">The reversible codec applied to matching clear-text values.</param>
        /// <param name="nullAsEmpty">Whether matching JSON <see langword="null"/> values are encoded as empty strings.</param>
        /// <returns>The number of values changed.</returns>
        public static int EncodeMatchingValuesInPlace(
            string jsonFilePath,
            string keyPathPattern,
            JsonSettingsValueCodec codec,
            bool nullAsEmpty = true)
        {
            ArgumentNullException.ThrowIfNull(codec);
            ArgumentException.ThrowIfNullOrWhiteSpace(keyPathPattern);

            return EncodeMatchingValuesInPlaceCore(
                jsonFilePath,
                new[] { keyPathPattern },
                codec.Encode,
                nullAsEmpty);
        }

        private static void WalkAndEncode(
            JsonNode node,
            string currentPath,
            KeyPathGlobMatcher matcher,
            Func<string, string> encode,
            bool nullAsEmpty,
            ref int updated)
        {
            if (node is JsonObject jsonObject)
            {
                foreach (string propertyName in jsonObject.Select(property => property.Key).ToList())
                {
                    JsonNode? propertyValue = jsonObject[propertyName];
                    string propertyPath = CombinePath(currentPath, propertyName);

                    if (matcher.IsMatch(propertyPath))
                    {
                        if (propertyValue is null)
                        {
                            if (nullAsEmpty)
                            {
                                jsonObject[propertyName] = encode(string.Empty);
                                updated++;
                            }
                        }
                        else if (propertyValue is JsonValue && TryGetString(propertyValue, out string? currentValue))
                        {
                            string text = currentValue ?? string.Empty;

                            if (!EncodedConfigurationValueFormat.HasRecognizedWrapper(text))
                            {
                                jsonObject[propertyName] = encode(text);
                                updated++;
                            }
                        }
                    }

                    if (propertyValue is not null && propertyValue is not JsonValue)
                    {
                        WalkAndEncode(
                            propertyValue,
                            propertyPath,
                            matcher,
                            encode,
                            nullAsEmpty,
                            ref updated);
                    }
                }

                return;
            }

            if (node is not JsonArray jsonArray)
            {
                return;
            }

            for (int index = 0; index < jsonArray.Count; index++)
            {
                JsonNode? item = jsonArray[index];
                string itemPath = CombinePath(currentPath, index.ToString());

                if (matcher.IsMatch(itemPath))
                {
                    if (item is null)
                    {
                        if (nullAsEmpty)
                        {
                            jsonArray[index] = encode(string.Empty);
                            updated++;
                        }
                    }
                    else if (item is JsonValue && TryGetString(item, out string? currentValue))
                    {
                        string text = currentValue ?? string.Empty;

                        if (!EncodedConfigurationValueFormat.HasRecognizedWrapper(text))
                        {
                            jsonArray[index] = encode(text);
                            updated++;
                        }
                    }
                }

                if (item is not null && item is not JsonValue)
                {
                    WalkAndEncode(
                        item,
                        itemPath,
                        matcher,
                        encode,
                        nullAsEmpty,
                        ref updated);
                }
            }
        }

        private static string CombinePath(string prefix, string segment)
        {
            return string.IsNullOrEmpty(prefix) ? segment : $"{prefix}:{segment}";
        }

        private static bool TryGetString(JsonNode valueNode, out string? value)
        {
            try
            {
                value = valueNode.GetValue<string?>();
                return true;
            }
            catch (InvalidOperationException)
            {
                value = null;
                return false;
            }
        }

        private static void WriteAtomically(string path, string content)
        {
            string directoryPath = Path.GetDirectoryName(path) ?? ".";
            string temporaryPath = Path.Combine(
                directoryPath,
                $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

            try
            {
                File.WriteAllText(temporaryPath, content);

                if (!File.Exists(path))
                {
                    File.Move(temporaryPath, path);
                    return;
                }

                try
                {
                    File.Replace(temporaryPath, path, destinationBackupFileName: null);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Move(temporaryPath, path, overwrite: true);
                }
                catch (IOException)
                {
                    File.Move(temporaryPath, path, overwrite: true);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private sealed class KeyPathGlobMatcher
        {
            private readonly Regex[] _patterns;

            public KeyPathGlobMatcher(IEnumerable<string> globPatterns)
            {
                _patterns = globPatterns
                    .Select(BuildRegex)
                    .ToArray();
            }

            public bool IsMatch(string keyPath)
            {
                return _patterns.Any(pattern => pattern.IsMatch(keyPath));
            }

            private static Regex BuildRegex(string globPattern)
            {
                string regularExpression = "^" +
                    Regex.Escape(globPattern)
                        .Replace(@"\*", ".*")
                        .Replace(@"\?", ".") +
                    "$";

                return new Regex(
                    regularExpression,
                    RegexOptions.Compiled |
                    RegexOptions.CultureInvariant |
                    RegexOptions.IgnoreCase);
            }
        }
    }
}
