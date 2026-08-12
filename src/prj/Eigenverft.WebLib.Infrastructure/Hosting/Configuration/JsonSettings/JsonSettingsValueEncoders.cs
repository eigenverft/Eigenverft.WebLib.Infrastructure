using System;
using System.Collections.Generic;
using System.Reflection;

using Eigenverft.NetLib.Infrastructure.Security.MachineBinding;
using Eigenverft.NetLib.Infrastructure.Text;
using Eigenverft.NetLib.Infrastructure.Transformations;

using Eigenverft.WebLib.Infrastructure.Transformations;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.JsonSettings
{
    /// <summary>
    /// Produces self-describing persisted JSON-settings values and codecs by framing reusable reversible string transforms.
    /// </summary>
    /// <remarks>
    /// <see cref="ReversibleStringTransforms"/> owns the reusable value operations. This type remains the authority for the
    /// JSON-settings persisted wrapper tokens, codec composition, V1 default layout, compatibility, and migration semantics.
    /// </remarks>
    public static class JsonSettingsValueEncoders
    {
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
            EncodedConfigurationValueKind.Base64,
            ReversibleStringTransforms.Base64);

        /// <summary>
        /// Gets the Base92JsonSafe representation codec backed by <see cref="Base92JsonSafeEncoder"/>.
        /// </summary>
        /// <remarks>
        /// This is a representation and analysis-friction layer, not cryptographic protection. It can hide immediately
        /// recognizable inner wrapper text from trivial inspection, but it adds no secret or cryptographic boundary. The
        /// standalone Base92 implementation owns the alphabet and base conversion; this settings codec only adds the
        /// self-describing settings wrapper.
        /// </remarks>
        public static JsonSettingsValueCodec Base92JsonSafe { get; } = new(
            "Base92JsonSafe",
            EncodedConfigurationValueKind.Base92JsonSafe,
            ReversibleStringTransforms.Base92JsonSafe);

        /// <summary>
        /// Gets the ROT13 obfuscation codec.
        /// </summary>
        /// <remarks>
        /// ROT13 is deliberately weak obfuscation and an analysis-friction layer, not cryptographic protection. It can
        /// disrupt trivial string matching or first-pass inspection, but it adds no secret factor or cryptographic boundary.
        /// </remarks>
        public static JsonSettingsValueCodec Rot13 { get; } = new(
            "Rot13",
            EncodedConfigurationValueKind.Rot13,
            ReversibleStringTransforms.Rot13);

        /// <summary>
        /// Creates a Caesar-shift obfuscation codec for ASCII letters.
        /// </summary>
        /// <param name="shift">The letter shift. Values are normalized modulo 26.</param>
        /// <returns>A self-describing Caesar codec.</returns>
        /// <remarks>
        /// Caesar shifting is deliberately weak obfuscation and an analysis-friction layer; it provides no cryptographic
        /// protection. The normalized shift is persisted in the encoded payload, so the parameter is not secret. Its value is
        /// limited to adding small extra work to trivial inspection while remaining generically reversible without application-
        /// specific state.
        /// </remarks>
        public static JsonSettingsValueCodec Caesar(int shift)
        {
            int normalizedShift = NormalizeCaesarShift(shift);

            return new JsonSettingsValueCodec(
                $"Caesar({normalizedShift})",
                EncodedConfigurationValueKind.Caesar,
                ReversibleStringTransforms.Caesar(normalizedShift));
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
            EncodedConfigurationValueKind.DpapiMachine,
            ReversibleStringTransforms.DpapiMachine);

        /// <summary>
        /// Gets the Windows DPAPI LocalMachine codec with a Base64Url payload.
        /// </summary>
        /// <remarks>
        /// LocalMachine binds the payload to the Windows machine, not to an administrator or individual user. Windows allows
        /// another user on the same machine to unprotect it; the value of this layer is the machine-context requirement.
        /// </remarks>
        public static JsonSettingsValueCodec DpapiMachineBase64Url { get; } = new(
            "DpapiMachineBase64Url",
            EncodedConfigurationValueKind.DpapiMachineBase64Url,
            ReversibleStringTransforms.DpapiMachineBase64Url);

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
                EncodedConfigurationValueKind.AesPassword,
                ReversibleStringTransforms.AesPassword(password));
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
            return new JsonSettingsValueCodec(
                "PhysicalMachineBoundAes",
                EncodedConfigurationValueKind.AesPassword,
                ReversibleStringTransforms.PhysicalMachineBoundAes());
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
        /// This overload is intended for normal application use. The key-ring directory is durable settings state; Data
        /// Protection owns the individual key file names and may create multiple files as keys rotate. Back up the complete
        /// key ring with protected settings and retain old keys while persisted values may still depend on them. Losing or
        /// deleting keys can make existing settings permanently unreadable. The application discriminator defaults to the
        /// entry assembly name; use the explicit overload when values must survive an application rename. A conventional
        /// Eigenverft host can keep this state in its separate <c>AppState</c> directory to reduce accidental co-exposure.
        /// Moving the same key ring to another machine is sufficient to use this codec there unless another composed layer adds
        /// machine binding. Directory separation is not an ACL boundary, and this codec does not configure an additional
        /// at-rest encryptor for the key-ring files. A missing directory is created automatically; on an existing installation
        /// an unexpectedly new or empty key ring should therefore be treated as lost state, not as a successful migration.
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
        /// Callers that need persisted values to survive application renames or that deliberately share a key ring should use
        /// this overload and keep both <paramref name="applicationName"/> and <paramref name="purpose"/> stable. Changing either
        /// value makes previously protected payloads unavailable to the new protector. The key ring is durable settings state:
        /// retain and back up all keys that may still protect persisted values. This codec contains no additional machine
        /// binding by itself and can be composed with other codecs when that property is required.
        /// </remarks>
        public static JsonSettingsValueCodec DataProtection(
            string keyDirectoryPath,
            string applicationName,
            string purpose)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(keyDirectoryPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
            ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

            return new JsonSettingsValueCodec(
                $"DataProtection({applicationName})",
                EncodedConfigurationValueKind.DataProtection,
                AspNetDataProtectionStringTransforms.DataProtection(
                    keyDirectoryPath,
                    applicationName,
                    purpose));
        }

        /// <summary>
        /// Creates the platform-neutral V1 default layered settings codec.
        /// </summary>
        /// <param name="password">The caller-supplied visible-ASCII password used by the application AES layer.</param>
        /// <param name="keyDirectoryPath">The persistent ASP.NET Core Data Protection key-ring directory.</param>
        /// <returns>
        /// A codec equivalent to <c>Compose(Rot13, Caesar(13), DataProtection(keyDirectoryPath), PhysicalMachineBoundAes(), AesPassword(password), Base92JsonSafe)</c>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The V1 default deliberately combines protection factors with low-cost friction layers. Data Protection requires the
        /// persistent key ring, PhysicalMachineBoundAes requires the source system/platform identity, and AesPassword requires
        /// the caller-supplied password. ROT13 and Caesar provide only reversible obfuscation/analysis friction, while
        /// Base92JsonSafe is a representation layer that also removes immediately recognizable inner wrapper text. These
        /// friction layers are not cryptographic security boundaries and must not be counted as independent secret factors.
        /// Their purpose is only to add small extra work to trivial inspection and automated first-pass analysis.
        /// </para>
        /// <para>
        /// The physical-machine binding is a lightweight additional recovery hurdle, not a hardware-backed secret. An attacker
        /// that collected the source machine's platform identity can reproduce that factor. A sufficiently compromised running
        /// process can also observe passwords and clear values. This pipeline is defense in depth and analysis friction, not an
        /// absolute security boundary.
        /// </para>
        /// <para>
        /// This method defines the V1 persisted pipeline contract and must not be changed silently. A future default layout must
        /// use an explicit new version or provide backward decoding/migration for V1 values.
        /// </para>
        /// <para>
        /// The Data Protection key ring is durable settings state: back it up with the protected settings and retain old keys
        /// while any persisted setting may still reference them. Losing or deleting the key ring can make existing values
        /// permanently unreadable. The convenience DataProtection overload creates a missing directory; on an existing
        /// installation an unexpectedly empty/new key ring therefore indicates lost state rather than a recoverable migration.
        /// Hosts should normally keep the key ring in the separate AppState directory rather than next to application settings
        /// or general application data. The default application discriminator is derived from the entry assembly name; callers
        /// that require values to survive an application rename should compose the explicit DataProtection overload instead.
        /// </para>
        /// </remarks>
        /// <exception cref="PlatformNotSupportedException">The current operating system is not supported for physical machine binding.</exception>
        /// <exception cref="InvalidOperationException">No valid system/platform UUID is available for physical machine binding.</exception>
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
            return Base64.Encode(clearText);
        }

        internal static bool TryDecodeBase92JsonSafePayload(string payload, out string clearText)
        {
            return ReversibleStringTransforms.Base92JsonSafe.TryReverse(payload, out clearText);
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
            return DpapiMachine.Encode(clearText);
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
            return DpapiMachineBase64Url.Encode(clearText);
        }

        internal static bool TryDecodeRot13Payload(string payload, out string clearText)
        {
            return ReversibleStringTransforms.Rot13.TryReverse(payload, out clearText);
        }

        internal static bool TryDecodeCaesarPayload(string payload, out string clearText)
        {
            return ReversibleStringTransforms.TryReverseCaesarPayload(payload, out clearText);
        }

        private static int NormalizeCaesarShift(int shift)
        {
            int normalized = shift % 26;
            return normalized < 0 ? normalized + 26 : normalized;
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

        internal static bool TryDecodeBase64Payload(string payload, out string clearText)
        {
            return ReversibleStringTransforms.Base64.TryReverse(payload, out clearText);
        }

        internal static bool TryDecodeDpapiBase64Payload(string payload, out string clearText)
        {
            return ReversibleStringTransforms.DpapiMachine.TryReverse(payload, out clearText);
        }

        internal static bool TryDecodeDpapiBase64UrlPayload(string payload, out string clearText)
        {
            return ReversibleStringTransforms.DpapiMachineBase64Url.TryReverse(payload, out clearText);
        }

        private static string NormalizeReadablePassword(string password, string parameterName)
        {
            return ReversibleStringTransforms.NormalizeReadablePassword(password, parameterName);
        }

        private static string NormalizeReadablePassword(byte[] passwordBytes, string parameterName)
        {
            return ReversibleStringTransforms.NormalizeReadablePassword(passwordBytes, parameterName);
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
            if (!EncodingToToken.TryGetValue(encoding, out string? token))
            {
                throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "The encoding kind has no registered persisted token.");
            }

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
    internal static class EncodedConfigurationValueDecoder
    {
        public static bool TryDecode(string? value, out string clearText)
        {
            string original = value ?? string.Empty;
            string current = original;
            bool changed = false;

            while (TryDecodeSingle(current, out string next))
            {
                changed = true;
                current = next;
            }

            // A recognized wrapper that remains means the complete nested value could not be decoded with the generic
            // context (for example AES/Data Protection, malformed payload data, or a platform-specific protection failure).
            // Roll back all successfully removed outer layers rather than exposing a partial representation.
            if (EncodedConfigurationValueFormat.HasRecognizedWrapper(current))
            {
                clearText = original;
                return false;
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
