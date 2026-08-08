using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using Eigenverft.WebLib.Infrastructure.Security.MachineBinding;
using Eigenverft.WebLib.Infrastructure.Security.Protection;
using Eigenverft.WebLib.Infrastructure.Text;

using Microsoft.AspNetCore.DataProtection;

namespace Eigenverft.WebLib.Infrastructure.Transformations
{
    /// <summary>
    /// Represents one reversible transformation from a string to another string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This type is intentionally independent from JSON configuration, persisted wrapper tokens, configuration providers and
    /// source switching. A transform owns only the reversible value operation. Callers decide how transformed strings are
    /// selected, framed, persisted, published or migrated.
    /// </para>
    /// <para>
    /// A transform may provide cryptographic protection, machine binding, obfuscation or only a storage representation. Those
    /// roles are deliberately distinct. For example, Base64 and Base92 are representations, while ROT13 and Caesar are only
    /// analysis-friction layers; none of them is a confidentiality boundary.
    /// </para>
    /// <para>
    /// <see cref="TryReverse"/> is transactional from the caller's perspective. On failure it returns the original transformed
    /// value rather than a partially reversed intermediate representation. Composed transforms preserve the same rule.
    /// </para>
    /// </remarks>
    public sealed class ReversibleStringTransform
    {
        internal delegate bool TryReverseDelegate(string transformedValue, out string originalValue);

        private readonly Func<string, string> _apply;
        private readonly TryReverseDelegate _tryReverse;

        internal ReversibleStringTransform(
            string name,
            Func<string, string> apply,
            TryReverseDelegate tryReverse)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(apply);
            ArgumentNullException.ThrowIfNull(tryReverse);

            Name = name;
            _apply = apply;
            _tryReverse = tryReverse;
        }

        /// <summary>Gets the descriptive transform name.</summary>
        public string Name { get; }

        /// <summary>Applies this transform to one string.</summary>
        /// <param name="value">The source value; <see langword="null"/> is treated as empty.</param>
        /// <returns>The transformed value.</returns>
        public string Apply(string? value)
        {
            return _apply(value ?? string.Empty);
        }

        /// <summary>Attempts to reverse this transform for one transformed string.</summary>
        /// <param name="transformedValue">The transformed value; <see langword="null"/> is treated as empty.</param>
        /// <param name="originalValue">
        /// Receives the original value on success. On failure, receives <paramref name="transformedValue"/> unchanged.
        /// </param>
        /// <returns><see langword="true"/> when the complete transform reverses successfully; otherwise <see langword="false"/>.</returns>
        public bool TryReverse(string? transformedValue, out string originalValue)
        {
            string value = transformedValue ?? string.Empty;
            if (_tryReverse(value, out string reversed))
            {
                originalValue = reversed;
                return true;
            }

            originalValue = value;
            return false;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>Creates reusable reversible string transformations.</summary>
    /// <remarks>
    /// These transformations are intentionally persistence-neutral. They do not add JSON-settings <c>enc:</c> wrappers and they
    /// do not decide which configuration keys are transformed. Persisted format/version ownership remains with the caller that
    /// frames the transformed payload, such as <c>JsonSettingsValueCodec</c>.
    /// </remarks>
    public static class ReversibleStringTransforms
    {
        private const int AesSaltSize = 16;
        private const int AesNonceSize = 12;
        private const int AesTagSize = 16;
        private const int AesKeySize = 32;
        private const int AesPbkdf2Iterations = 100_000;
        private const string AesPayloadVersion = "v1";
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>Gets a UTF-8 to Base64 representation transform.</summary>
        /// <remarks>
        /// Base64 is a storage representation only. It may obscure text visually but provides no cryptographic protection.
        /// </remarks>
        public static ReversibleStringTransform Base64 { get; } = new(
            "Base64",
            value => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)),
            TryReverseBase64);

        /// <summary>Gets the JSON-safe Base92 representation transform.</summary>
        /// <remarks>
        /// Base92JsonSafe is a representation and analysis-friction layer, not cryptographic protection. It may hide immediately
        /// recognizable inner text from trivial inspection but adds no secret or cryptographic boundary.
        /// </remarks>
        public static ReversibleStringTransform Base92JsonSafe { get; } = new(
            "Base92JsonSafe",
            value => Base92JsonSafeEncoder.Encode(Encoding.UTF8.GetBytes(value)),
            TryReverseBase92JsonSafe);

        /// <summary>Gets the ROT13 obfuscation transform.</summary>
        /// <remarks>
        /// ROT13 is deliberately weak obfuscation and analysis friction. It can disrupt trivial string matching but provides no
        /// cryptographic protection and adds no secret factor.
        /// </remarks>
        public static ReversibleStringTransform Rot13 { get; } = new(
            "Rot13",
            value => ApplyCaesar(value, 13),
            (string value, out string original) =>
            {
                original = ApplyCaesar(value, 13);
                return true;
            });

        /// <summary>Creates a Caesar-shift obfuscation transform for ASCII letters.</summary>
        /// <param name="shift">The letter shift. Values are normalized modulo 26.</param>
        /// <returns>A reversible Caesar transform.</returns>
        /// <remarks>
        /// Caesar shifting is deliberately weak obfuscation and analysis friction; it provides no cryptographic protection. The
        /// normalized shift is carried in the transformed value so it is not secret. Carrying the shift also allows callers that
        /// know the expected transform to reject a payload produced with a different shift.
        /// </remarks>
        public static ReversibleStringTransform Caesar(int shift)
        {
            int normalizedShift = NormalizeCaesarShift(shift);
            return new ReversibleStringTransform(
                $"Caesar({normalizedShift})",
                value => string.Concat(
                    normalizedShift.ToString(CultureInfo.InvariantCulture),
                    ":",
                    ApplyCaesar(value, normalizedShift)),
                (string value, out string original) =>
                {
                    if (!TryReverseCaesarPayload(value, out int encodedShift, out original) ||
                        encodedShift != normalizedShift)
                    {
                        original = value;
                        return false;
                    }

                    return true;
                });
        }

        /// <summary>Gets the Windows DPAPI LocalMachine transform represented as Base64.</summary>
        /// <remarks>
        /// LocalMachine binds protected bytes to the Windows machine, not to an administrator or individual user. Windows permits
        /// another user on the same machine to unprotect a LocalMachine payload. The security value is therefore the originating
        /// machine-context requirement. Base64 is only the string representation for the protected bytes.
        /// </remarks>
        public static ReversibleStringTransform DpapiMachine { get; } = new(
            "DpapiMachine",
            value => Convert.ToBase64String(DpapiMachineProtection.Protect(Encoding.UTF8.GetBytes(value))),
            TryReverseDpapiBase64);

        /// <summary>Gets the Windows DPAPI LocalMachine transform represented as unpadded Base64Url.</summary>
        /// <remarks>
        /// LocalMachine is machine scope, not user/admin isolation. Base64Url is only the persisted string representation around
        /// DPAPI bytes and is not another protection factor.
        /// </remarks>
        public static ReversibleStringTransform DpapiMachineBase64Url { get; } = new(
            "DpapiMachineBase64Url",
            value => EncodeBase64Url(DpapiMachineProtection.Protect(Encoding.UTF8.GetBytes(value))),
            TryReverseDpapiBase64Url);

        /// <summary>Creates a password-derived AES-GCM transform.</summary>
        /// <param name="password">The non-empty visible-ASCII password used to derive the AES key.</param>
        /// <returns>A parameterized AES-GCM transform.</returns>
        /// <remarks>
        /// <para>
        /// Security is bounded by how the caller obtains and protects the password. The transform captures that password for its
        /// lifetime, so callers should assume it is recoverable from a sufficiently compromised process or from static analysis
        /// when embedded directly in the consuming executable.
        /// </para>
        /// <para>
        /// The transformed payload is explicitly versioned. Its Base64Url fields are only representations of binary AES payload
        /// parts. Future KDF/cipher changes require a deliberate new payload version or explicit migration.
        /// </para>
        /// </remarks>
        public static ReversibleStringTransform AesPassword(string password)
        {
            password = NormalizeReadablePassword(password, nameof(password));
            return new ReversibleStringTransform(
                "AesPassword",
                value => ApplyAesPassword(value, password),
                (string value, out string original) => TryReverseAesPassword(value, password, out original));
        }

        /// <summary>Creates the same AES-GCM transform from visible ASCII password bytes.</summary>
        /// <param name="passwordAsciiBytes">Visible ASCII bytes representing the password.</param>
        /// <returns>The same transform as the equivalent string password.</returns>
        /// <remarks>
        /// This overload can avoid a clear password in the assembly string-literal table, but it is only a small static-analysis
        /// obstacle and is not a secrecy boundary. The bytes remain recoverable from the executable. Bytes outside visible ASCII
        /// 0x21 through 0x7E are rejected deliberately.
        /// </remarks>
        public static ReversibleStringTransform AesPassword(byte[] passwordAsciiBytes)
        {
            return AesPassword(NormalizeReadablePassword(passwordAsciiBytes, nameof(passwordAsciiBytes)));
        }

        /// <summary>Creates an AES-GCM transform whose password material is derived from the current platform fingerprint.</summary>
        /// <remarks>
        /// This is lightweight machine binding, not a hardware-backed secret. It is intended to make application-directory-only
        /// theft insufficient for offline reversal on another machine unless the source platform identity was also collected. An
        /// attacker with sufficient source-machine access can read the same identity and reproduce the fingerprint.
        /// </remarks>
        public static ReversibleStringTransform PhysicalMachineBoundAes()
        {
            ReversibleStringTransform aes = AesPassword(PhysicalMachineBinding.GetFingerprint());
            return new ReversibleStringTransform("PhysicalMachineBoundAes", aes.Apply, aes.TryReverse);
        }

        /// <summary>Creates an ASP.NET Core Data Protection transform with explicit application and purpose isolation.</summary>
        /// <param name="keyDirectoryPath">The persistent Data Protection key-ring directory.</param>
        /// <param name="applicationName">The stable logical application discriminator.</param>
        /// <param name="purpose">The stable purpose isolating this protected data from other Data Protection uses.</param>
        /// <returns>A transform backed by the specified persistent Data Protection key ring.</returns>
        /// <remarks>
        /// <para>
        /// The key ring is durable state. Back up the complete ring with protected values and retain old keys while data may still
        /// depend on them. Losing keys can make existing transformed values permanently unavailable.
        /// </para>
        /// <para>
        /// Keep <paramref name="applicationName"/> and <paramref name="purpose"/> stable for as long as values must remain
        /// reversible. Changing either makes values unavailable to the new protector. Data Protection alone adds no machine
        /// binding; moving the same key ring and isolation context to another machine is sufficient unless another transform adds
        /// a machine-context requirement.
        /// </para>
        /// <para>
        /// Directory separation is not an ACL boundary and this helper does not configure an additional at-rest encryptor for the
        /// key-ring files. A missing directory is created; on an existing installation an unexpectedly new or empty ring should
        /// therefore be treated as lost state, not as successful migration.
        /// </para>
        /// </remarks>
        public static ReversibleStringTransform DataProtection(
            string keyDirectoryPath,
            string applicationName,
            string purpose)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(keyDirectoryPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
            ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

            string fullKeyDirectoryPath = Path.GetFullPath(keyDirectoryPath);
            Directory.CreateDirectory(fullKeyDirectoryPath);

            IDataProtectionProvider provider = DataProtectionProvider.Create(
                new DirectoryInfo(fullKeyDirectoryPath),
                builder => builder.SetApplicationName(applicationName));
            IDataProtector protector = provider.CreateProtector(purpose);

            return new ReversibleStringTransform(
                $"DataProtection({applicationName})",
                protector.Protect,
                (string value, out string original) => TryReverseDataProtection(value, protector, out original));
        }

        /// <summary>Composes reversible transforms into one transform.</summary>
        /// <param name="transforms">Transforms in forward execution order.</param>
        /// <returns>A transform that applies from first to last and reverses from last to first.</returns>
        /// <remarks>
        /// This is pure value-level composition. It does not add persistence framing between stages. Callers that require nested
        /// persisted wrappers or stage-specific migration metadata must compose at their persistence/codec layer instead.
        /// </remarks>
        public static ReversibleStringTransform Compose(params ReversibleStringTransform[] transforms)
        {
            ArgumentNullException.ThrowIfNull(transforms);
            if (transforms.Length == 0)
            {
                throw new ArgumentException("At least one transform is required.", nameof(transforms));
            }

            var pipeline = new ReversibleStringTransform[transforms.Length];
            for (int index = 0; index < transforms.Length; index++)
            {
                pipeline[index] = transforms[index] ??
                    throw new ArgumentException($"Transform at index {index} is null.", nameof(transforms));
            }

            string[] names = new string[pipeline.Length];
            for (int index = 0; index < pipeline.Length; index++)
            {
                names[index] = pipeline[index].Name;
            }

            return new ReversibleStringTransform(
                string.Join(" -> ", names),
                value => ApplyPipeline(value, pipeline),
                (string value, out string original) => TryReversePipeline(value, pipeline, out original));
        }

        internal static string NormalizeReadablePassword(string password, string parameterName)
        {
            ArgumentException.ThrowIfNullOrEmpty(password, parameterName);

            for (int index = 0; index < password.Length; index++)
            {
                char value = password[index];
                if (value < '!' || value > '~')
                {
                    throw new ArgumentException(
                        $"Password character at index {index} is U+{(int)value:X4}; only visible ASCII characters U+0021 through U+007E are allowed.",
                        parameterName);
                }
            }

            return password;
        }

        internal static string NormalizeReadablePassword(byte[] passwordBytes, string parameterName)
        {
            ArgumentNullException.ThrowIfNull(passwordBytes, parameterName);
            if (passwordBytes.Length == 0)
            {
                throw new ArgumentException("Password byte representation must not be empty.", parameterName);
            }

            for (int index = 0; index < passwordBytes.Length; index++)
            {
                byte value = passwordBytes[index];
                if (value < 0x21 || value > 0x7E)
                {
                    throw new ArgumentException(
                        $"Password byte at index {index} is 0x{value:X2}; only visible ASCII bytes 0x21 through 0x7E are allowed.",
                        parameterName);
                }
            }

            return Encoding.ASCII.GetString(passwordBytes);
        }

        internal static bool TryReverseCaesarPayload(string payload, out string original)
        {
            return TryReverseCaesarPayload(payload, out _, out original);
        }

        private static bool TryReverseCaesarPayload(string payload, out int encodedShift, out string original)
        {
            encodedShift = 0;
            original = payload;
            int delimiterIndex = payload.IndexOf(':');

            if (delimiterIndex <= 0 ||
                !int.TryParse(
                    payload.Substring(0, delimiterIndex),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out encodedShift) ||
                encodedShift < 0 ||
                encodedShift >= 26)
            {
                return false;
            }

            original = ApplyCaesar(payload.Substring(delimiterIndex + 1), -encodedShift);
            return true;
        }

        private static bool TryReverseBase64(string value, out string original)
        {
            original = value;
            try
            {
                original = StrictUtf8.GetString(Convert.FromBase64String(value));
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        private static bool TryReverseBase92JsonSafe(string value, out string original)
        {
            original = value;
            if (!Base92JsonSafeEncoder.TryDecode(value, out byte[] bytes))
            {
                return false;
            }

            try
            {
                original = StrictUtf8.GetString(bytes);
                return true;
            }
            catch (DecoderFallbackException)
            {
                original = value;
                return false;
            }
        }

        private static bool TryReverseDpapiBase64(string value, out string original)
        {
            original = value;
            try
            {
                return TryUnprotect(Convert.FromBase64String(value), value, out original);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static bool TryReverseDpapiBase64Url(string value, out string original)
        {
            original = value;
            return TryDecodeBase64Url(value, out byte[] protectedBytes) &&
                TryUnprotect(protectedBytes, value, out original);
        }

        private static bool TryUnprotect(byte[] protectedBytes, string originalTransformedValue, out string original)
        {
            original = originalTransformedValue;
            if (!DpapiMachineProtection.TryUnprotect(protectedBytes, out byte[] clearBytes))
            {
                return false;
            }

            try
            {
                original = StrictUtf8.GetString(clearBytes);
                return true;
            }
            catch (DecoderFallbackException)
            {
                original = originalTransformedValue;
                return false;
            }
        }

        private static string ApplyAesPassword(string value, string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(AesSaltSize);
            byte[] nonce = RandomNumberGenerator.GetBytes(AesNonceSize);
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                AesPbkdf2Iterations,
                HashAlgorithmName.SHA256,
                AesKeySize);
            byte[] clearBytes = Encoding.UTF8.GetBytes(value);
            byte[] cipherBytes = new byte[clearBytes.Length];
            byte[] tag = new byte[AesTagSize];

            try
            {
                using var aes = new AesGcm(key, AesTagSize);
                aes.Encrypt(nonce, clearBytes, cipherBytes, tag);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }

            return string.Join(
                ".",
                AesPayloadVersion,
                EncodeBase64Url(salt),
                EncodeBase64Url(nonce),
                EncodeBase64Url(tag),
                EncodeBase64Url(cipherBytes));
        }

        private static bool TryReverseAesPassword(string value, string password, out string original)
        {
            original = value;
            string[] parts = value.Split('.', StringSplitOptions.None);
            if (parts.Length != 5 ||
                !string.Equals(parts[0], AesPayloadVersion, StringComparison.Ordinal) ||
                !TryDecodeBase64Url(parts[1], out byte[] salt) || salt.Length != AesSaltSize ||
                !TryDecodeBase64Url(parts[2], out byte[] nonce) || nonce.Length != AesNonceSize ||
                !TryDecodeBase64Url(parts[3], out byte[] tag) || tag.Length != AesTagSize ||
                !TryDecodeBase64Url(parts[4], out byte[] cipherBytes))
            {
                return false;
            }

            byte[] key = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                AesPbkdf2Iterations,
                HashAlgorithmName.SHA256,
                AesKeySize);
            byte[] clearBytes = new byte[cipherBytes.Length];

            try
            {
                using var aes = new AesGcm(key, AesTagSize);
                aes.Decrypt(nonce, cipherBytes, tag, clearBytes);
                original = StrictUtf8.GetString(clearBytes);
                return true;
            }
            catch (CryptographicException)
            {
                original = value;
                return false;
            }
            catch (DecoderFallbackException)
            {
                original = value;
                return false;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        private static bool TryReverseDataProtection(string value, IDataProtector protector, out string original)
        {
            original = value;
            try
            {
                original = protector.Unprotect(value);
                return true;
            }
            catch (CryptographicException)
            {
                original = value;
                return false;
            }
        }

        private static string ApplyPipeline(string value, ReversibleStringTransform[] transforms)
        {
            string current = value;
            foreach (ReversibleStringTransform transform in transforms)
            {
                current = transform.Apply(current);
            }

            return current;
        }

        private static bool TryReversePipeline(
            string value,
            ReversibleStringTransform[] transforms,
            out string original)
        {
            string current = value;
            for (int index = transforms.Length - 1; index >= 0; index--)
            {
                if (!transforms[index].TryReverse(current, out string next))
                {
                    original = value;
                    return false;
                }

                current = next;
            }

            original = current;
            return true;
        }

        private static int NormalizeCaesarShift(int shift)
        {
            int normalized = shift % 26;
            return normalized < 0 ? normalized + 26 : normalized;
        }

        private static string ApplyCaesar(string value, int shift)
        {
            int normalizedShift = NormalizeCaesarShift(shift);
            char[] characters = value.ToCharArray();

            for (int index = 0; index < characters.Length; index++)
            {
                char character = characters[index];
                if (character is >= 'a' and <= 'z')
                {
                    characters[index] = (char)('a' + ((character - 'a' + normalizedShift) % 26));
                }
                else if (character is >= 'A' and <= 'Z')
                {
                    characters[index] = (char)('A' + ((character - 'A' + normalizedShift) % 26));
                }
            }

            return new string(characters);
        }

        private static string EncodeBase64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        private static bool TryDecodeBase64Url(string value, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            try
            {
                string padded = value.Replace('-', '+').Replace('_', '/');
                int remainder = padded.Length % 4;
                if (remainder != 0)
                {
                    padded = padded.PadRight(padded.Length + 4 - remainder, '=');
                }

                bytes = Convert.FromBase64String(padded);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
