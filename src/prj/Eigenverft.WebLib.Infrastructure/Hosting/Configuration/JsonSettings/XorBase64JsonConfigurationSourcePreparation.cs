using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.JsonSettings
{
    /// <summary>
    /// Demonstrates JSON source preparation by decoding selected <c>xor1:</c> Base64 values with one XOR byte.
    /// </summary>
    /// <remarks>
    /// XOR is intentionally not a security boundary. This implementation exists as a small deterministic preparation example and
    /// test consumer for the generic source-preparation contract.
    /// </remarks>
    public sealed class XorBase64JsonConfigurationSourcePreparation : IJsonConfigurationSourcePreparation
    {
        private const string Prefix = "xor1:";
        private readonly byte _key;
        private readonly Regex[] _patterns;

        /// <summary>Creates a preparation for the supplied non-zero XOR byte and configuration-key glob patterns.</summary>
        public XorBase64JsonConfigurationSourcePreparation(byte key, params string[] keyPathPatterns)
        {
            if (key == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(key), "The XOR key must be non-zero.");
            }

            ArgumentNullException.ThrowIfNull(keyPathPatterns);
            if (keyPathPatterns.Length == 0)
            {
                throw new ArgumentException("At least one key-path pattern is required.", nameof(keyPathPatterns));
            }

            _key = key;
            _patterns = keyPathPatterns.Select(CreatePattern).ToArray();
        }

        /// <summary>Encodes one clear-text value into the persisted demonstration format understood by this preparation.</summary>
        public string EncodeValue(string clearText)
        {
            ArgumentNullException.ThrowIfNull(clearText);
            byte[] bytes = Encoding.UTF8.GetBytes(clearText);
            ApplyXor(bytes, _key);
            return Prefix + Convert.ToBase64String(bytes);
        }

        /// <inheritdoc />
        public void Prepare(JsonConfigurationSourcePreparationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            foreach (string key in context.Values.Keys.ToArray())
            {
                if (!Matches(key))
                {
                    continue;
                }

                string? value = context.Values[key];
                if (value is null || !value.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                byte[] bytes;
                try
                {
                    bytes = Convert.FromBase64String(value[Prefix.Length..]);
                }
                catch (FormatException exception)
                {
                    throw new FormatException($"The XOR value for configuration key '{key}' is not valid Base64.", exception);
                }

                ApplyXor(bytes, _key);
                context.Values[key] = Encoding.UTF8.GetString(bytes);
            }
        }

        private bool Matches(string key)
        {
            return _patterns.Any(pattern => pattern.IsMatch(key));
        }

        private static Regex CreatePattern(string pattern)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
            string regex = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*", StringComparison.Ordinal)
                .Replace("\\?", ".", StringComparison.Ordinal) + "$";
            return new Regex(regex, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        }

        private static void ApplyXor(byte[] bytes, byte key)
        {
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] ^= key;
            }
        }
    }
}
