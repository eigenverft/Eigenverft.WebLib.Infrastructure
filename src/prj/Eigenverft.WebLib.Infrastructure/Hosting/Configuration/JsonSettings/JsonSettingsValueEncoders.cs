using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

using Eigenverft.WebLib.Infrastructure.Security.MachineBinding;
using Eigenverft.WebLib.Infrastructure.Security.Protection;
using Eigenverft.WebLib.Infrastructure.Text;

using Microsoft.AspNetCore.DataProtection;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.JsonSettings
{
    /// <summary>
    /// Produces self-describing encoded values and reusable codecs for JSON configuration files.
    /// </summary>
    public static class JsonSettingsValueEncoders
    {
        private const int AesSaltSize = 16;
        private const int AesNonceSize = 12;
        private const int AesTagSize = 16;
        private const int AesKeySize = 32;
        private const int AesPbkdf2Iterations = 100_000;
        private const string AesPayloadVersion = "v1";
        private const string DefaultDataProtectionPurpose =
            "Eigenverft.WebLib.Infrastructure.JsonSettings.ValueProtection.v1";

        /// <summary>
        /// Gets the Base64 representation codec.
        /// </summary>
        /// <remarks>
        /// Base64 is a storage/representation encoding. It may obscure a value visually, but it provides no
        /// cryptographic protection. This explicit codec is separate from internal Base64/Base64Url use that merely
        /// serializes binary payload parts produced by another codec.
        /// </remarks>
        public static JsonSettingsValueCodec Base64 { get; } = new(
            "Base64",
            EncodeBase64,
            TryDecodeBase64Value);

        /// <summary>
        /// Gets the Base92JsonSafe representation codec backed by <see cref="Base92JsonSafeEncoder"/>.
        /// </summary>
        /// <remarks>
        /// This is representation/obfuscation only, not cryptographic protection. The standalone Base92 implementation
        /// owns the alphabet and base conversion; this settings codec only adds the self-describing settings wrapper.
        /// </remarks>
        public static JsonSettingsValueCodec Base92JsonSafe { get; } = new(
            "Base92JsonSafe",
            EncodeBase92JsonSafe,
            TryDecodeBase92JsonSafeValue);

        /// <summary>
        /// Gets the ROT13 obfuscation codec.
        /// </summary>
        /// <remarks>
        /// ROT13 is deliberately weak obfuscation, not cryptographic protection. It exists as a lightweight codec
        /// for exercising composition without introducing another protection backend.
        /// </remarks>
        public static JsonSettingsValueCodec Rot13 { get; } = new(
            "Rot13",
            EncodeRot13,
            TryDecodeRot13Value);

        /// <summary>
        /// Creates a Caesar-shift obfuscation codec for ASCII letters.
        /// </summary>
        /// <param name="shift">The letter shift. Values are normalized modulo 26.</param>
        /// <returns>A self-describing Caesar codec.</returns>
        /// <remarks>
        /// Caesar shifting is deliberately weak obfuscation and provides no cryptographic protection. The normalized
        /// shift is persisted in the encoded payload so the parameter is not secret and the generic decoder can reverse
        /// the transformation without application-specific state.
        /// </remarks>
        public static JsonSettingsValueCodec Caesar(int shift)
        {
            int normalizedShift = NormalizeCaesarShift(shift);

            return new JsonSettingsValueCodec(
                $"Caesar({normalizedShift})",
                clearText => EncodeCaesar(clearText, normalizedShift),
                (string encodedValue, out string clearText) =>
                    TryDecodeCaesarValue(encodedValue, normalizedShift, out clearText));
        }

        /// <summary>
        /// Gets the Windows DPAPI LocalMachine codec with a Base64 payload.
        /// </summary>
        /// <remarks>
        /// LocalMachine binds the payload to the Windows machine, not to an administrator or individual user. Windows allows
        /// another user on the same machine to unprotect it; the value of this layer is the machine-context requirement.
        /// </remarks>
        public static JsonSettingsValueCodec DpapiMachine { get; } = new(
            "DpapiMachine",
            EncodeDpapiMachine,
            TryDecodeDpapiBase64Value);

        /// <summary>
        /// Gets the Windows DPAPI LocalMachine codec with a Base64Url payload.
        /// </summary>
        /// <remarks>
        /// LocalMachine binds the payload to the Windows machine, not to an administrator or individual user. Windows allows
        /// another user on the same machine to unprotect it; the value of this layer is the machine-context requirement.
        /// </remarks>
        public static JsonSettingsValueCodec DpapiMachineBase64Url { get; } = new(
            "DpapiMachineBase64Url",
            EncodeDpapiMachineBase64Url,
            TryDecodeDpapiBase64UrlValue);

        /// <summary>
        /// Creates a password-derived AES-GCM protection codec.
        /// </summary>
        /// <param name="password">The non-empty visible-ASCII password used to derive the AES key.</param>
        /// <returns>A codec that captures the normalized password for both encoding and decoding.</returns>
        /// <remarks>
        /// This backend exists primarily to prove parameterized and composable protection backends. Its security is
        /// bounded by how the caller obtains and protects the supplied password. The codec captures that password for its
        /// lifetime, so callers should assume it is recoverable from a sufficiently compromised process or from static
        /// analysis when it is embedded directly in the consuming executable.
        /// </remarks>
        public static JsonSettingsValueCodec AesPassword(string password)
        {
            password = NormalizeReadablePassword(password, nameof(password));

            return new JsonSettingsValueCodec(
                "AesPassword",
                clearText => EncodeAesPassword(clearText, password),
                (string encodedValue, out string clearText) =>
                    TryDecodeAesPasswordValue(encodedValue, password, out clearText));
        }

        /// <summary>
        /// Creates the same password-derived AES-GCM codec from visible ASCII password bytes.
        /// </summary>
        /// <param name="passwordAsciiBytes">
        /// The visible ASCII representation of the password. For example, <c>"hello"</c> and
        /// <c>{ 0x68, 0x65, 0x6C, 0x6C, 0x6F }</c> describe the same password and therefore the same AES context.
        /// </param>
        /// <returns>A codec equivalent to calling <see cref="AesPassword(string)"/> with the represented ASCII text.</returns>
        /// <remarks>
        /// This overload exists so callers can embed a password without placing its clear text in the assembly string-literal
        /// table. It is only a small static-analysis obstacle, not a secrecy boundary: the bytes and this normalization logic
        /// remain recoverable from the executable. Bytes outside visible ASCII (0x21 through 0x7E) are rejected deliberately
        /// so accidental values such as 0x00 or 0xFF cannot silently produce a different password.
        /// </remarks>
        public static JsonSettingsValueCodec AesPassword(byte[] passwordAsciiBytes)
        {
            return AesPassword(NormalizeReadablePassword(passwordAsciiBytes, nameof(passwordAsciiBytes)));
        }

        /// <summary>
        /// Creates an AES-GCM codec whose password material is derived from the current machine's V1 platform fingerprint.
        /// </summary>
        /// <returns>A codec bound to the current Windows, Linux, or macOS system/platform UUID.</returns>
        /// <exception cref="PlatformNotSupportedException">The current operating system is not supported.</exception>
        /// <exception cref="InvalidOperationException">
        /// The current operating system is supported, but no valid system/platform UUID is available.
        /// </exception>
        /// <remarks>
        /// <para>
        /// This is a lightweight machine-binding shortcut, not a hardware-backed secret. It is equivalent to creating an
        /// AES password codec from <see cref="PhysicalMachineBinding.GetFingerprint"/>. Its intended value is to make an
        /// application-directory-only theft insufficient for offline decoding on another machine unless the attacker also
        /// collected the source machine's platform identity. An attacker with sufficient access to the source machine can
        /// read the same identity and reproduce the fingerprint.
        /// </para>
        /// <para>
        /// No additional management package is required. If broader hardware/CIM inventory becomes useful later,
        /// Microsoft.Management.Infrastructure may be evaluated separately; that is intentionally outside this shortcut's
        /// scope. The encoded payload remains an ordinary versioned <see cref="AesPassword(string)"/> payload, so callers must use
        /// this same machine-bound codec context when decoding.
        /// </para>
        /// </remarks>
        public static JsonSettingsValueCodec PhysicalMachineBoundAes()
        {
            JsonSettingsValueCodec aes = AesPassword(PhysicalMachineBinding.GetFingerprint());

            return new JsonSettingsValueCodec(
                "PhysicalMachineBoundAes",
                aes.Encode,
                aes.TryDecode);
        }

        /// <summary>
        /// Creates an ASP.NET Core Data Protection codec backed by a persistent file-system key ring.
        /// </summary>
        /// <param name="keyDirectoryPath">The directory in which ASP.NET Core Data Protection stores its key ring.</param>
        /// <returns>
        /// A codec using the entry assembly name as its application discriminator and the library's stable JSON-settings
        /// purpose string.
        /// </returns>
        /// <remarks>
        /// This overload is intended for normal application use. The key-ring directory is the persistent backend; Data
        /// Protection owns the individual key file names and may create multiple files as keys rotate. The application
        /// discriminator defaults to the entry assembly name and is not a file name. A conventional Eigenverft host can use
        /// its separate <c>AppState</c> directory for this path to reduce accidental co-exposure with settings or application
        /// data. Moving the same key ring to another machine is sufficient to use this codec there unless another composed
        /// protection layer, such as DPAPI, adds a machine-bound requirement. The directory separation itself is not an ACL
        /// boundary, and this codec does not configure an additional at-rest encryptor for the key-ring files.
        /// </remarks>
        public static JsonSettingsValueCodec DataProtection(string keyDirectoryPath)
        {
            return DataProtection(
                keyDirectoryPath,
                ResolveDefaultDataProtectionApplicationName(),
                DefaultDataProtectionPurpose);
        }

        /// <summary>
        /// Creates an ASP.NET Core Data Protection codec with explicit application and purpose isolation.
        /// </summary>
        /// <param name="keyDirectoryPath">The directory in which ASP.NET Core Data Protection stores its key ring.</param>
        /// <param name="applicationName">The stable logical application discriminator for the key ring.</param>
        /// <param name="purpose">The stable purpose that isolates JSON-settings payloads from other protected data.</param>
        /// <returns>A codec backed by the specified persistent Data Protection key ring.</returns>
        /// <remarks>
        /// Callers that need persisted values to survive application renames or that deliberately share a key ring should
        /// use this overload and keep both <paramref name="applicationName"/> and <paramref name="purpose"/> stable.
        /// Changing either value makes previously protected payloads unavailable to the new protector. This codec contains
        /// no additional machine binding by itself and can be composed with other codecs when that property is required.
        /// </remarks>
        public static JsonSettingsValueCodec DataProtection(
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

            return new JsonSettingsValueCodec(
                $"DataProtection({applicationName})",
                clearText => EncodeDataProtection(clearText, protector),
                (string encodedValue, out string clearText) =>
                    TryDecodeDataProtectionValue(encodedValue, protector, out clearText));
        }

        /// <summary>
        /// Creates the platform-neutral default layered settings codec.
        /// </summary>
        /// <param name="password">The caller-supplied visible-ASCII password used by the application AES layer.</param>
        /// <param name="keyDirectoryPath">The persistent ASP.NET Core Data Protection key-ring directory.</param>
        /// <returns>
        /// A codec equivalent to <c>Compose(Rot13, Caesar(13), DataProtection(keyDirectoryPath), PhysicalMachineBoundAes(), AesPassword(password), Base92JsonSafe)</c>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// This default deliberately combines different roles rather than presenting every layer as cryptographic security.
        /// ROT13 and Caesar are obfuscation; Base92JsonSafe is representation. Data Protection requires the persistent key
        /// ring, PhysicalMachineBoundAes requires the source system/platform identity, and AesPassword requires the
        /// caller-supplied password. The intended effect is that copying only the application directory or only one related
        /// artifact is insufficient for straightforward offline recovery on another machine.
        /// </para>
        /// <para>
        /// The physical-machine binding is a lightweight additional recovery hurdle, not a hardware-backed secret. An
        /// attacker that collected the source machine's platform identity can reproduce that factor. Likewise, a sufficiently
        /// compromised running process can observe passwords and clear values. This pipeline is defense in depth, not a claim
        /// that any one software-only layer is an absolute security boundary.
        /// </para>
        /// <para>
        /// The exact stage order is a persisted-format contract. Changing this default later requires explicit migration or
        /// backward-decoding support. Hosts should normally place the Data Protection key ring in the separate AppState
        /// directory rather than next to application settings or general application data.
        /// </para>
        /// </remarks>
        public static JsonSettingsValueCodec Default(string password, string keyDirectoryPath)
        {
            password = NormalizeReadablePassword(password, nameof(password));

            return Compose(
                Rot13,
                Caesar(13),
                DataProtection(keyDirectoryPath),
                PhysicalMachineBoundAes(),
                AesPassword(password),
                Base92JsonSafe);
        }

        /// <summary>
        /// Creates <see cref="Default(string,string)"/> from the visible-ASCII byte representation of the same password.
        /// </summary>
        /// <param name="passwordAsciiBytes">Visible ASCII bytes representing the password text.</param>
        /// <param name="keyDirectoryPath">The persistent ASP.NET Core Data Protection key-ring directory.</param>
        /// <returns>The same default codec as the equivalent string password.</returns>
        /// <remarks>
        /// This overload is intended for embedded application material where a consumer wants to avoid a clear password as
        /// a .NET string literal. It does not make the password secret from executable analysis.
        /// </remarks>
        public static JsonSettingsValueCodec Default(byte[] passwordAsciiBytes, string keyDirectoryPath)
        {
            return Default(
                NormalizeReadablePassword(passwordAsciiBytes, nameof(passwordAsciiBytes)),
                keyDirectoryPath);
        }

        /// <summary>
        /// Creates the Windows default by adding DPAPI LocalMachine protection outside the platform-neutral default.
        /// </summary>
        /// <param name="password">The caller-supplied visible-ASCII password used by the application AES layer.</param>
        /// <param name="keyDirectoryPath">The persistent ASP.NET Core Data Protection key-ring directory.</param>
        /// <returns>A codec equivalent to <c>Compose(Default(password, keyDirectoryPath), DpapiMachineBase64Url)</c>.</returns>
        /// <remarks>
        /// This shortcut adds a Windows DPAPI LocalMachine requirement without changing the semantics of
        /// <see cref="Default(string,string)"/> on other platforms. LocalMachine is machine scope, not user or administrator
        /// isolation: Windows permits another user on the same machine to unprotect a LocalMachine payload. The intended extra
        /// requirement is therefore access to the originating Windows machine context, not elevated privileges. DPAPI is
        /// invoked through the Windows operating-system API; the library does not require the
        /// System.Security.Cryptography.ProtectedData NuGet package.
        /// </remarks>
        /// <exception cref="PlatformNotSupportedException">Encoding is attempted on a non-Windows platform.</exception>
        public static JsonSettingsValueCodec DefaultWindows(string password, string keyDirectoryPath)
        {
            return Compose(Default(password, keyDirectoryPath), DpapiMachineBase64Url);
        }

        /// <summary>
        /// Creates <see cref="DefaultWindows(string,string)"/> from the visible-ASCII byte representation of the same password.
        /// </summary>
        /// <param name="passwordAsciiBytes">Visible ASCII bytes representing the password text.</param>
        /// <param name="keyDirectoryPath">The persistent ASP.NET Core Data Protection key-ring directory.</param>
        /// <returns>The same Windows default codec as the equivalent string password.</returns>
        public static JsonSettingsValueCodec DefaultWindows(byte[] passwordAsciiBytes, string keyDirectoryPath)
        {
            return DefaultWindows(
                NormalizeReadablePassword(passwordAsciiBytes, nameof(passwordAsciiBytes)),
                keyDirectoryPath);
        }

        /// <summary>
        /// Creates the common DPAPI-machine-scope then ROT13 pipeline as a concise call-site shortcut.
        /// </summary>
        /// <returns>A codec equivalent to <c>Compose(DpapiMachineBase64Url, Rot13)</c>.</returns>
        /// <remarks>
        /// Shortcuts contain no independent encoding logic. Their only purpose is to keep application startup code
        /// concise while retaining <see cref="Compose"/> as the canonical pipeline implementation. Encoding applies
        /// DPAPI first and ROT13 second; decoding therefore applies ROT13 first and DPAPI second.
        /// </remarks>
        public static JsonSettingsValueCodec DpapiWithRot13()
        {
            return Compose(DpapiMachineBase64Url, Rot13);
        }

        /// <summary>
        /// Creates the common DPAPI-machine-scope then Caesar pipeline as a concise call-site shortcut.
        /// </summary>
        /// <param name="shift">The Caesar letter shift; values are normalized modulo 26.</param>
        /// <returns>A codec equivalent to <c>Compose(DpapiMachineBase64Url, Caesar(shift))</c>.</returns>
        /// <remarks>
        /// This shortcut contains no independent encoding logic. Encoding applies DPAPI first and Caesar second;
        /// decoding therefore applies Caesar first and DPAPI second.
        /// </remarks>
        public static JsonSettingsValueCodec DpapiWithCaesar(int shift)
        {
            return Compose(DpapiMachineBase64Url, Caesar(shift));
        }

        /// <summary>
        /// Composes codecs into one reversible pipeline.
        /// </summary>
        /// <param name="codecs">The codecs in encoding order.</param>
        /// <returns>A codec that encodes from first to last and decodes from last to first.</returns>
        /// <remarks>
        /// Composition keeps transformation roles independent. A protection codec such as AES can therefore be combined
        /// with representation or obfuscation codecs without creating combination-specific enum values or changing the JSON
        /// provider. Encoding uses the declared order and decoding requires the same parameterized codec context in reverse.
        /// The nested wrappers describe individual stages, but <see cref="Compose"/> is not a migration manifest: changing
        /// passwords, Data Protection isolation, or stage order requires deliberate backward decoding or migration.
        /// </remarks>
        public static JsonSettingsValueCodec Compose(params JsonSettingsValueCodec[] codecs)
        {
            ArgumentNullException.ThrowIfNull(codecs);

            if (codecs.Length == 0)
            {
                throw new ArgumentException("At least one codec is required.", nameof(codecs));
            }

            var pipeline = new JsonSettingsValueCodec[codecs.Length];
            for (int index = 0; index < codecs.Length; index++)
            {
                pipeline[index] = codecs[index] ??
                    throw new ArgumentException($"Codec at index {index} is null.", nameof(codecs));
            }

            string[] names = new string[pipeline.Length];
            for (int index = 0; index < pipeline.Length; index++)
            {
                names[index] = pipeline[index].Name;
            }

            return new JsonSettingsValueCodec(
                string.Join(" -> ", names),
                clearText => EncodePipeline(clearText, pipeline),
                (string encodedValue, out string clearText) =>
                    TryDecodePipeline(encodedValue, pipeline, out clearText));
        }

        /// <summary>
        /// Encodes UTF-8 text as Base64.
        /// </summary>
        /// <param name="clearText">The value to encode; <see langword="null"/> is treated as empty.</param>
        /// <returns>A wrapped Base64 value understood by the decoding JSON provider.</returns>
        /// <remarks>Base64 is an encoding, not encryption, and does not protect sensitive data.</remarks>
        public static string EncodeBase64(string? clearText)
        {
            string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(clearText ?? string.Empty));
            return EncodedConfigurationValueFormat.Wrap(EncodedConfigurationValueKind.Base64, payload);
        }

        private static string EncodeBase92JsonSafe(string clearText)
        {
            string payload = Base92JsonSafeEncoder.Encode(Encoding.UTF8.GetBytes(clearText));
            return EncodedConfigurationValueFormat.Wrap(EncodedConfigurationValueKind.Base92JsonSafe, payload);
        }

        private static bool TryDecodeBase92JsonSafeValue(string encodedValue, out string clearText)
        {
            clearText = encodedValue;
            return EncodedConfigurationValueFormat.TryUnwrap(
                    encodedValue,
                    out EncodedConfigurationValueKind encoding,
                    out string payload) &&
                encoding == EncodedConfigurationValueKind.Base92JsonSafe &&
                TryDecodeBase92JsonSafePayload(payload, out clearText);
        }

        internal static bool TryDecodeBase92JsonSafePayload(string payload, out string clearText)
        {
            clearText = string.Empty;

            if (!Base92JsonSafeEncoder.TryDecode(payload, out byte[] bytes))
            {
                return false;
            }

            clearText = Encoding.UTF8.GetString(bytes);
            return true;
        }

        /// <summary>
        /// Protects UTF-8 text with Windows DPAPI machine scope and stores the payload as Base64.
        /// </summary>
        /// <param name="clearText">The value to protect; <see langword="null"/> is treated as empty.</param>
        /// <returns>A wrapped DPAPI value understood by the decoding JSON provider.</returns>
        /// <exception cref="PlatformNotSupportedException">The current platform is not Windows.</exception>
        /// <remarks>
        /// Machine-scope DPAPI values are tied to the Windows machine that created them and are not portable
        /// configuration secrets.
        /// </remarks>
        public static string EncodeDpapiMachine(string? clearText)
        {
            byte[] protectedBytes = DpapiMachineProtection.Protect(Encoding.UTF8.GetBytes(clearText ?? string.Empty));
            string payload = Convert.ToBase64String(protectedBytes);
            return EncodedConfigurationValueFormat.Wrap(EncodedConfigurationValueKind.DpapiMachine, payload);
        }

        /// <summary>
        /// Protects UTF-8 text with Windows DPAPI machine scope and stores the payload as unpadded Base64Url.
        /// </summary>
        /// <param name="clearText">The value to protect; <see langword="null"/> is treated as empty.</param>
        /// <returns>A wrapped DPAPI value understood by the decoding JSON provider.</returns>
        /// <exception cref="PlatformNotSupportedException">The current platform is not Windows.</exception>
        /// <remarks>
        /// This is the correctly named equivalent of the historical
        /// <c>EncodeDpapiMachineBase64</c> API. The persisted token remains compatible with existing values.
        /// Machine-scope DPAPI values are tied to the Windows machine that created them.
        /// </remarks>
        public static string EncodeDpapiMachineBase64Url(string? clearText)
        {
            byte[] protectedBytes = DpapiMachineProtection.Protect(Encoding.UTF8.GetBytes(clearText ?? string.Empty));
            string payload = Base64Url.Encode(protectedBytes);
            return EncodedConfigurationValueFormat.Wrap(
                EncodedConfigurationValueKind.DpapiMachineBase64Url,
                payload);
        }

        private static string EncodeRot13(string clearText)
        {
            return EncodedConfigurationValueFormat.Wrap(
                EncodedConfigurationValueKind.Rot13,
                ApplyCaesar(clearText, 13));
        }

        private static bool TryDecodeRot13Value(string encodedValue, out string clearText)
        {
            clearText = encodedValue;
            return EncodedConfigurationValueFormat.TryUnwrap(
                    encodedValue,
                    out EncodedConfigurationValueKind encoding,
                    out string payload) &&
                encoding == EncodedConfigurationValueKind.Rot13 &&
                TryDecodeRot13Payload(payload, out clearText);
        }
        internal static bool TryDecodeRot13Payload(string payload, out string clearText)
        {
            clearText = ApplyCaesar(payload, 13);
            return true;
        }

        private static string EncodeCaesar(string clearText, int shift)
        {
            string payload = string.Concat(
                shift.ToString(CultureInfo.InvariantCulture),
                ":",
                ApplyCaesar(clearText, shift));
            return EncodedConfigurationValueFormat.Wrap(EncodedConfigurationValueKind.Caesar, payload);
        }

        private static bool TryDecodeCaesarValue(
            string encodedValue,
            int expectedShift,
            out string clearText)
        {
            clearText = encodedValue;

            return EncodedConfigurationValueFormat.TryUnwrap(
                    encodedValue,
                    out EncodedConfigurationValueKind encoding,
                    out string payload) &&
                encoding == EncodedConfigurationValueKind.Caesar &&
                TryDecodeCaesarPayload(payload, out int encodedShift, out clearText) &&
                encodedShift == expectedShift;
        }

        internal static bool TryDecodeCaesarPayload(string payload, out string clearText)
        {
            return TryDecodeCaesarPayload(payload, out _, out clearText);
        }

        private static bool TryDecodeCaesarPayload(
            string payload,
            out int encodedShift,
            out string clearText)
        {
            encodedShift = 0;
            clearText = string.Empty;
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

            clearText = ApplyCaesar(payload.Substring(delimiterIndex + 1), -encodedShift);
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

        private static string EncodeDataProtection(string clearText, IDataProtector protector)
        {
            string protectedPayload = protector.Protect(clearText);
            return EncodedConfigurationValueFormat.Wrap(
                EncodedConfigurationValueKind.DataProtection,
                protectedPayload);
        }

        private static bool TryDecodeDataProtectionValue(
            string encodedValue,
            IDataProtector protector,
            out string clearText)
        {
            clearText = encodedValue;

            if (!EncodedConfigurationValueFormat.TryUnwrap(
                    encodedValue,
                    out EncodedConfigurationValueKind encoding,
                    out string payload) ||
                encoding != EncodedConfigurationValueKind.DataProtection)
            {
                return false;
            }

            try
            {
                clearText = protector.Unprotect(payload);
                return true;
            }
            catch (CryptographicException)
            {
                clearText = encodedValue;
                return false;
            }
        }

        private static string ResolveDefaultDataProtectionApplicationName()
        {
            return Assembly.GetEntryAssembly()?.GetName().Name
                ?? AppDomain.CurrentDomain.FriendlyName;
        }

        private static string EncodePipeline(string clearText, JsonSettingsValueCodec[] codecs)
        {
            string current = clearText;

            foreach (JsonSettingsValueCodec codec in codecs)
            {
                current = codec.Encode(current);
            }

            return current;
        }

        private static bool TryDecodePipeline(
            string encodedValue,
            JsonSettingsValueCodec[] codecs,
            out string clearText)
        {
            string current = encodedValue;

            for (int index = codecs.Length - 1; index >= 0; index--)
            {
                if (!codecs[index].TryDecode(current, out string next))
                {
                    clearText = encodedValue;
                    return false;
                }

                current = next;
            }

            clearText = current;
            return true;
        }

        private static string EncodeAesPassword(string clearText, string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(AesSaltSize);
            byte[] nonce = RandomNumberGenerator.GetBytes(AesNonceSize);
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                AesPbkdf2Iterations,
                HashAlgorithmName.SHA256,
                AesKeySize);
            byte[] clearBytes = Encoding.UTF8.GetBytes(clearText);
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

            // Base64Url here is only the storage representation for binary AES payload parts. It is not the
            // user-selectable Base64 codec and therefore does not add a pipeline transformation step.
            // The explicit version is part of the persisted AES payload contract. Future KDF/cipher changes can add a new
            // version without silently making previously written values undecodable.
            string payload = string.Join(
                ".",
                AesPayloadVersion,
                Base64Url.Encode(salt),
                Base64Url.Encode(nonce),
                Base64Url.Encode(tag),
                Base64Url.Encode(cipherBytes));

            return EncodedConfigurationValueFormat.Wrap(EncodedConfigurationValueKind.AesPassword, payload);
        }

        private static bool TryDecodeBase64Value(string encodedValue, out string clearText)
        {
            clearText = encodedValue;
            return EncodedConfigurationValueFormat.TryUnwrap(
                    encodedValue,
                    out EncodedConfigurationValueKind encoding,
                    out string payload) &&
                encoding == EncodedConfigurationValueKind.Base64 &&
                TryDecodeBase64Payload(payload, out clearText);
        }

        private static bool TryDecodeDpapiBase64Value(string encodedValue, out string clearText)
        {
            clearText = encodedValue;
            return EncodedConfigurationValueFormat.TryUnwrap(
                    encodedValue,
                    out EncodedConfigurationValueKind encoding,
                    out string payload) &&
                encoding == EncodedConfigurationValueKind.DpapiMachine &&
                TryDecodeDpapiBase64Payload(payload, out clearText);
        }

        private static bool TryDecodeDpapiBase64UrlValue(string encodedValue, out string clearText)
        {
            clearText = encodedValue;
            return EncodedConfigurationValueFormat.TryUnwrap(
                    encodedValue,
                    out EncodedConfigurationValueKind encoding,
                    out string payload) &&
                encoding == EncodedConfigurationValueKind.DpapiMachineBase64Url &&
                TryDecodeDpapiBase64UrlPayload(payload, out clearText);
        }

        private static bool TryDecodeAesPasswordValue(
            string encodedValue,
            string password,
            out string clearText)
        {
            clearText = encodedValue;

            if (!EncodedConfigurationValueFormat.TryUnwrap(
                    encodedValue,
                    out EncodedConfigurationValueKind encoding,
                    out string payload) ||
                encoding != EncodedConfigurationValueKind.AesPassword)
            {
                return false;
            }

            string[] parts = payload.Split('.', StringSplitOptions.None);
            if (parts.Length != 5 ||
                !string.Equals(parts[0], AesPayloadVersion, StringComparison.Ordinal) ||
                !Base64Url.TryDecode(parts[1], out byte[] salt) || salt.Length != AesSaltSize ||
                !Base64Url.TryDecode(parts[2], out byte[] nonce) || nonce.Length != AesNonceSize ||
                !Base64Url.TryDecode(parts[3], out byte[] tag) || tag.Length != AesTagSize ||
                !Base64Url.TryDecode(parts[4], out byte[] cipherBytes))
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
                clearText = Encoding.UTF8.GetString(clearBytes);
                return true;
            }
            catch (CryptographicException)
            {
                clearText = encodedValue;
                return false;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        internal static bool TryDecodeBase64Payload(string payload, out string clearText)
        {
            clearText = string.Empty;

            try
            {
                clearText = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        internal static bool TryDecodeDpapiBase64Payload(string payload, out string clearText)
        {
            clearText = string.Empty;

            try
            {
                return TryUnprotect(Convert.FromBase64String(payload), out clearText);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        internal static bool TryDecodeDpapiBase64UrlPayload(string payload, out string clearText)
        {
            clearText = string.Empty;
            return Base64Url.TryDecode(payload, out byte[] protectedBytes) &&
                TryUnprotect(protectedBytes, out clearText);
        }

        private static string NormalizeReadablePassword(string password, string parameterName)
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

        private static string NormalizeReadablePassword(byte[] passwordBytes, string parameterName)
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

        private static bool TryUnprotect(byte[] protectedBytes, out string clearText)
        {
            clearText = string.Empty;

            if (!DpapiMachineProtection.TryUnprotect(protectedBytes, out byte[] clearBytes))
            {
                return false;
            }

            clearText = Encoding.UTF8.GetString(clearBytes);
            return true;
        }
    }

    internal enum EncodedConfigurationValueKind
    {
        Base64 = 0,
        DpapiMachine = 1,
        DpapiMachineBase64Url = 2,
        AesPassword = 3,
        Rot13 = 4,
        Caesar = 5,
        Base92JsonSafe = 6,
        DataProtection = 7,
    }

    internal static class EncodedConfigurationValueFormat
    {
        private const string Prefix = "enc:";

        private static readonly IReadOnlyDictionary<EncodedConfigurationValueKind, string> EncodingToToken =
            new Dictionary<EncodedConfigurationValueKind, string>
            {
                { EncodedConfigurationValueKind.Base64, "q7m2n4" },
                { EncodedConfigurationValueKind.DpapiMachine, "x1p9d0" },
                { EncodedConfigurationValueKind.DpapiMachineBase64Url, "k4v8s2" },
                { EncodedConfigurationValueKind.AesPassword, "a3s6p1" },
                { EncodedConfigurationValueKind.Rot13, "r1t3o7" },
                { EncodedConfigurationValueKind.Caesar, "c4e5s2" },
                { EncodedConfigurationValueKind.Base92JsonSafe, "b9j2s7" },
                { EncodedConfigurationValueKind.DataProtection, "d7p4r8" },
            };

        private static readonly IReadOnlyDictionary<string, EncodedConfigurationValueKind> TokenToEncoding =
            new Dictionary<string, EncodedConfigurationValueKind>(StringComparer.OrdinalIgnoreCase)
            {
                { "q7m2n4", EncodedConfigurationValueKind.Base64 },
                { "x1p9d0", EncodedConfigurationValueKind.DpapiMachine },
                { "k4v8s2", EncodedConfigurationValueKind.DpapiMachineBase64Url },
                { "a3s6p1", EncodedConfigurationValueKind.AesPassword },
                { "r1t3o7", EncodedConfigurationValueKind.Rot13 },
                { "c4e5s2", EncodedConfigurationValueKind.Caesar },
                { "b9j2s7", EncodedConfigurationValueKind.Base92JsonSafe },
                { "d7p4r8", EncodedConfigurationValueKind.DataProtection },
            };

        public static string Wrap(EncodedConfigurationValueKind encoding, string? payload)
        {
            string token = EncodingToToken.TryGetValue(encoding, out string? knownToken)
                ? knownToken
                : encoding.ToString().ToLowerInvariant();

            return $"{Prefix}{token}:{payload ?? string.Empty}";
        }

        public static bool TryUnwrap(
            string? value,
            out EncodedConfigurationValueKind encoding,
            out string payload)
        {
            encoding = default;
            payload = string.Empty;

            if (value is null || !value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string remainder = value.Substring(Prefix.Length);
            int delimiterIndex = remainder.IndexOf(':');

            if (delimiterIndex <= 0)
            {
                return false;
            }

            string token = remainder.Substring(0, delimiterIndex);
            payload = remainder.Substring(delimiterIndex + 1);

            if (TokenToEncoding.TryGetValue(token, out encoding))
            {
                return true;
            }

            if (Enum.TryParse(token, ignoreCase: true, out encoding))
            {
                return true;
            }

            // Historical enum name used before Base64Url was named accurately.
            if (string.Equals(token, "DpapiMachineBase64", StringComparison.OrdinalIgnoreCase))
            {
                encoding = EncodedConfigurationValueKind.DpapiMachineBase64Url;
                return true;
            }

            encoding = default;
            return false;
        }

        public static bool HasRecognizedWrapper(string? value)
        {
            return TryUnwrap(value, out _, out _);
        }
    }

    internal static class Base64Url
    {
        public static string Encode(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            return Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        public static bool TryDecode(string? value, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();

            if (value is null)
            {
                return false;
            }

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
    internal static class EncodedConfigurationValueDecoder
    {
        public static bool TryDecode(string? value, out string clearText)
        {
            clearText = value ?? string.Empty;
            string current = value ?? string.Empty;
            bool changed = false;

            for (int depth = 0; depth < 5; depth++)
            {
                if (!TryDecodeSingle(current, out string next))
                {
                    break;
                }

                changed = true;
                current = next;
            }

            clearText = current;
            return changed;
        }

        private static bool TryDecodeSingle(string value, out string clearText)
        {
            clearText = value;

            if (!EncodedConfigurationValueFormat.TryUnwrap(value, out EncodedConfigurationValueKind encoding, out string payload))
            {
                return false;
            }

            return encoding switch
            {
                EncodedConfigurationValueKind.Base64 => JsonSettingsValueEncoders.TryDecodeBase64Payload(payload, out clearText),
                EncodedConfigurationValueKind.DpapiMachine => JsonSettingsValueEncoders.TryDecodeDpapiBase64Payload(payload, out clearText),
                EncodedConfigurationValueKind.DpapiMachineBase64Url => JsonSettingsValueEncoders.TryDecodeDpapiBase64UrlPayload(payload, out clearText),
                EncodedConfigurationValueKind.Rot13 => JsonSettingsValueEncoders.TryDecodeRot13Payload(payload, out clearText),
                EncodedConfigurationValueKind.Caesar => JsonSettingsValueEncoders.TryDecodeCaesarPayload(payload, out clearText),
                EncodedConfigurationValueKind.Base92JsonSafe => JsonSettingsValueEncoders.TryDecodeBase92JsonSafePayload(payload, out clearText),
                // Parameterized protection codecs require explicit context and stay encoded otherwise.
                EncodedConfigurationValueKind.AesPassword => false,
                EncodedConfigurationValueKind.DataProtection => false,
                _ => false,
            };
        }
    }
}
