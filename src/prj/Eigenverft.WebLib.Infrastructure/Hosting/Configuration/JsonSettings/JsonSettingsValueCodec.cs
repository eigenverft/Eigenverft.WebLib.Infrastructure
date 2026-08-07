using System;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.JsonSettings
{
    /// <summary>
    /// Represents one reversible JSON-settings value transformation.
    /// </summary>
    /// <remarks>
    /// A codec may provide protection, obfuscation, or only a storage representation. Those roles are deliberately
    /// distinct: for example, Base64 is useful for representing text or binary-derived data as a string but provides
    /// no confidentiality. Codecs can be composed; encoding runs in declaration order and decoding in reverse order.
    /// </remarks>
    public sealed class JsonSettingsValueCodec
    {
        internal delegate bool TryDecodeDelegate(string encodedValue, out string clearText);

        private readonly Func<string, string> _encode;
        private readonly TryDecodeDelegate _tryDecode;

        internal JsonSettingsValueCodec(
            string name,
            Func<string, string> encode,
            TryDecodeDelegate tryDecode)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(encode);
            ArgumentNullException.ThrowIfNull(tryDecode);

            Name = name;
            _encode = encode;
            _tryDecode = tryDecode;
        }

        /// <summary>
        /// Gets the descriptive codec name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Encodes one value using this codec.
        /// </summary>
        /// <param name="clearText">The value to transform; <see langword="null"/> is treated as empty.</param>
        /// <returns>The encoded value.</returns>
        public string Encode(string? clearText)
        {
            return _encode(clearText ?? string.Empty);
        }

        internal bool TryDecode(string? encodedValue, out string clearText)
        {
            string value = encodedValue ?? string.Empty;
            clearText = value;
            return _tryDecode(value, out clearText);
        }
    }
}
