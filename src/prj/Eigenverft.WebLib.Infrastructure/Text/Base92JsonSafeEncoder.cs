using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Eigenverft.WebLib.Infrastructure.Text
{
    /// <summary>
    /// Encodes arbitrary bytes with a canonical base-92 representation that can appear inside a JSON string without
    /// JSON string escaping.
    /// </summary>
    /// <remarks>
    /// The alphabet contains every printable ASCII character from U+0021 through U+007E except double quote
    /// (<c>"</c>) and backslash (<c>\</c>). Space is excluded by starting at U+0021. The resulting 92 symbols are:
    /// <c>!#$%&amp;'()*+,-./0123456789:;&lt;=&gt;?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[]^_`abcdefghijklmnopqrstuvwxyz{|}~</c>.
    ///
    /// Encoding interprets the non-leading-zero input bytes as one unsigned big-endian base-256 integer and converts
    /// it to base 92. Each leading zero byte is represented by one leading <c>!</c>, the zero-valued alphabet symbol.
    /// Empty input encodes as an empty string and no padding is used.
    ///
    /// This type provides representation only. Base92JsonSafe does not provide cryptographic protection and should not
    /// be treated as encryption. JSON-safe here means JSON-string-safe; characters such as <c>&lt;</c>, <c>&gt;</c>,
    /// <c>&amp;</c>, apostrophe, and slash remain part of the alphabet and may require separate handling in other contexts.
    /// </remarks>
    public static class Base92JsonSafeEncoder
    {
        /// <summary>
        /// Gets the canonical 92-character alphabet in digit order from zero through 91.
        /// </summary>
        public const string Alphabet = "!#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[]^_`abcdefghijklmnopqrstuvwxyz{|}~";

        private const int Radix = 92;
        private static readonly int[] CharacterToValue = CreateCharacterToValueMap();

        /// <summary>
        /// Encodes bytes using the canonical Base92JsonSafe representation.
        /// </summary>
        /// <param name="bytes">The bytes to encode.</param>
        /// <returns>The Base92JsonSafe string.</returns>
        public static string Encode(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            if (bytes.Length == 0)
            {
                return string.Empty;
            }

            int leadingZeroCount = 0;
            while (leadingZeroCount < bytes.Length && bytes[leadingZeroCount] == 0)
            {
                leadingZeroCount++;
            }

            if (leadingZeroCount == bytes.Length)
            {
                return new string(Alphabet[0], leadingZeroCount);
            }

            var value = new BigInteger(
                bytes.AsSpan(leadingZeroCount),
                isUnsigned: true,
                isBigEndian: true);
            var reversedDigits = new List<char>();

            while (value > BigInteger.Zero)
            {
                value = BigInteger.DivRem(value, Radix, out BigInteger remainder);
                reversedDigits.Add(Alphabet[(int)remainder]);
            }

            var result = new StringBuilder(leadingZeroCount + reversedDigits.Count);
            result.Append(Alphabet[0], leadingZeroCount);

            for (int index = reversedDigits.Count - 1; index >= 0; index--)
            {
                result.Append(reversedDigits[index]);
            }

            return result.ToString();
        }

        /// <summary>
        /// Decodes one canonical Base92JsonSafe string.
        /// </summary>
        /// <param name="value">The encoded value.</param>
        /// <returns>The decoded bytes.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
        /// <exception cref="FormatException"><paramref name="value"/> contains a character outside the alphabet.</exception>
        public static byte[] Decode(string value)
        {
            ArgumentNullException.ThrowIfNull(value);

            if (!TryDecode(value, out byte[] bytes))
            {
                throw new FormatException("The value is not valid canonical Base92JsonSafe data.");
            }

            return bytes;
        }

        /// <summary>
        /// Attempts to decode one canonical Base92JsonSafe string.
        /// </summary>
        /// <param name="value">The encoded value.</param>
        /// <param name="bytes">Receives the decoded bytes on success.</param>
        /// <returns><see langword="true"/> when decoding succeeds; otherwise <see langword="false"/>.</returns>
        public static bool TryDecode(string? value, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();

            if (value is null)
            {
                return false;
            }

            if (value.Length == 0)
            {
                return true;
            }

            int leadingZeroCount = 0;
            while (leadingZeroCount < value.Length && value[leadingZeroCount] == Alphabet[0])
            {
                leadingZeroCount++;
            }

            BigInteger numericValue = BigInteger.Zero;
            for (int index = leadingZeroCount; index < value.Length; index++)
            {
                char character = value[index];
                if (character >= CharacterToValue.Length || CharacterToValue[character] < 0)
                {
                    return false;
                }

                numericValue = (numericValue * Radix) + CharacterToValue[character];
            }

            byte[] numericBytes = numericValue.IsZero
                ? Array.Empty<byte>()
                : numericValue.ToByteArray(isUnsigned: true, isBigEndian: true);

            bytes = new byte[leadingZeroCount + numericBytes.Length];
            numericBytes.CopyTo(bytes, leadingZeroCount);
            return true;
        }

        private static int[] CreateCharacterToValueMap()
        {
            var map = new int[128];
            Array.Fill(map, -1);

            for (int index = 0; index < Alphabet.Length; index++)
            {
                map[Alphabet[index]] = index;
            }

            return map;
        }
    }
}
