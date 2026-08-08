using System;

using Eigenverft.WebLib.Infrastructure.Transformations;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.JsonSettings
{
    /// <summary>
    /// Represents one self-describing persisted JSON-settings value codec built around a reversible string operation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The neutral reversible operation is separate from this type. This codec owns JSON-settings persistence concerns:
    /// recognized wrapper framing, persisted kind/version compatibility, complete decode semantics, and nested codec composition.
    /// </para>
    /// <para>
    /// A codec may frame a transform that provides protection, obfuscation, or only a storage representation. Those roles are
    /// deliberately distinct: for example, Base64 provides no confidentiality. Codec composition encodes in declaration order
    /// and decodes in reverse while preserving each stage's persisted wrapper.
    /// </para>
    /// </remarks>
    public sealed class JsonSettingsValueCodec
    {
        internal delegate bool TryDecodeDelegate(string encodedValue, out string clearText);

        private readonly Func<string, string> _encode;
        private readonly TryDecodeDelegate _tryDecode;

        internal JsonSettingsValueCodec(
            string name,
            EncodedConfigurationValueKind persistedKind,
            ReversibleStringTransform transform)
            : this(
                name,
                clearText => EncodedConfigurationValueFormat.Wrap(persistedKind, transform.Apply(clearText)),
                (string encodedValue, out string clearText) =>
                {
                    clearText = encodedValue;
                    if (!EncodedConfigurationValueFormat.TryUnwrap(
                            encodedValue,
                            out EncodedConfigurationValueKind actualKind,
                            out string payload) ||
                        actualKind != persistedKind ||
                        !transform.TryReverse(payload, out string reversed))
                    {
                        return false;
                    }

                    clearText = reversed;
                    return true;
                })
        {
            ArgumentNullException.ThrowIfNull(transform);
        }

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

        /// <summary>
        /// Attempts to reverse this codec for one encoded value.
        /// </summary>
        /// <param name="encodedValue">The encoded value; <see langword="null"/> is treated as empty.</param>
        /// <param name="clearText">
        /// Receives the decoded clear text on success. On failure, receives the original encoded value so callers can
        /// preserve unavailable or mismatched protected data without partially unwrapping it.
        /// </param>
        /// <returns><see langword="true"/> when the complete codec reverses successfully; otherwise <see langword="false"/>.</returns>
        /// <remarks>
        /// Composed codecs are transactional from the caller's perspective: if any inner stage cannot be reversed, this
        /// method returns <see langword="false"/> and restores <paramref name="encodedValue"/> rather than exposing an
        /// intermediate representation. This also makes the API suitable for explicit decode-and-rewrite migrations.
        /// </remarks>
        public bool TryDecode(string? encodedValue, out string clearText)
        {
            string value = encodedValue ?? string.Empty;

            if (_tryDecode(value, out string decodedValue))
            {
                clearText = decodedValue;
                return true;
            }

            clearText = value;
            return false;
        }
    }
}
