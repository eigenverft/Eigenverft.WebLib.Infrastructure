using System;
using System.Collections.Generic;
using System.Linq;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.JsonSettings
{
    /// <summary>
    /// Represents one reusable candidate-preparation bundle that can be assigned to JSON configuration registrations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the application-facing wrapper around the lower-level <see cref="IJsonConfigurationSourcePreparation"/>
    /// extension contract. A bundle may adapt one reversible value codec, compose several candidate preparations, or wrap a
    /// custom preparation supplied by an application. It never owns source selection, file persistence, or provider publication.
    /// </para>
    /// <para>
    /// Candidate preparation is an in-memory load concern. Built-in codec-backed preparations decode already encoded values in
    /// an isolated parsed snapshot; they do not encode, rewrite, migrate, or otherwise modify the source file on disk. Any clear
    /// value produced by a protection codec necessarily exists in process memory after successful preparation and must not be
    /// treated as protected from a sufficiently compromised running process.
    /// </para>
    /// </remarks>
    public sealed class JsonConfigurationCandidatePreparation : IJsonConfigurationSourcePreparation
    {
        private readonly IJsonConfigurationSourcePreparation _inner;

        internal JsonConfigurationCandidatePreparation(string name, IJsonConfigurationSourcePreparation inner)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(inner);

            Name = name;
            _inner = inner;
        }

        /// <summary>Gets the descriptive name of this reusable candidate-preparation bundle.</summary>
        public string Name { get; }

        /// <inheritdoc />
        public void Prepare(JsonConfigurationSourcePreparationContext context)
        {
            _inner.Prepare(context);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// Creates reusable JSON candidate preparations from the same value codecs and common bundles exposed by
    /// <see cref="JsonSettingsValueEncoders"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="JsonSettingsValueEncoders"/> remains the authority for the public persisted-codec contract: wrapper formats,
    /// codec composition, parameter validation, compatibility, and migration semantics. These helpers adapt those existing
    /// codecs to the generic candidate-preparation boundary; they deliberately do not duplicate the underlying reversible
    /// value transformations or their security behavior.
    /// </para>
    /// <para>
    /// A codec-backed preparation scans the parsed candidate snapshot and replaces a value only when its selected codec can
    /// completely decode that value. Plain values and values belonging to another codec remain unchanged. A composed codec is
    /// transactional from the candidate's perspective: when an inner stage cannot be reversed with the supplied password,
    /// Data Protection context, machine context, or persisted stage order, the original encoded value remains intact rather than
    /// exposing a partially unwrapped intermediate value. No generic fallback is attempted for an explicitly selected codec.
    /// </para>
    /// <para>
    /// The security character of each shortcut is exactly the security character of the underlying codec. Representation and
    /// analysis-friction codecs such as Base64, Base92JsonSafe, ROT13, and Caesar are not cryptographic protection and must not be
    /// counted as independent secret factors. Machine-binding shortcuts add machine-context requirements, not hardware-backed
    /// secrecy. Password- and key-ring-based protection remains bounded by how those inputs and the running process are protected.
    /// </para>
    /// </remarks>
    public static class JsonConfigurationCandidatePreparations
    {
        /// <summary>Decodes values produced by <see cref="JsonSettingsValueEncoders.Base64"/>.</summary>
        /// <remarks>
        /// Base64 is a storage/representation encoding. It may obscure a value visually, but it provides no cryptographic
        /// protection. This preparation only removes the explicit self-describing Base64 settings wrapper in memory; it does not
        /// treat Base64 used internally to serialize another codec's binary payload as an independent protection stage.
        /// </remarks>
        public static JsonConfigurationCandidatePreparation Base64 { get; } =
            Decode(JsonSettingsValueEncoders.Base64);

        /// <summary>Decodes values produced by <see cref="JsonSettingsValueEncoders.Base92JsonSafe"/>.</summary>
        /// <remarks>
        /// Base92JsonSafe is a representation and analysis-friction layer, not cryptographic protection. It may hide immediately
        /// recognizable inner wrapper text from trivial inspection, but it adds no secret or cryptographic boundary.
        /// </remarks>
        public static JsonConfigurationCandidatePreparation Base92JsonSafe { get; } =
            Decode(JsonSettingsValueEncoders.Base92JsonSafe);

        /// <summary>Decodes values produced by <see cref="JsonSettingsValueEncoders.Rot13"/>.</summary>
        /// <remarks>
        /// ROT13 is deliberately weak obfuscation and analysis friction. It may disrupt trivial string matching or first-pass
        /// inspection, but it provides no cryptographic protection and adds no secret factor.
        /// </remarks>
        public static JsonConfigurationCandidatePreparation Rot13 { get; } =
            Decode(JsonSettingsValueEncoders.Rot13);

        /// <summary>Creates a candidate preparation for the parameterized Caesar value codec.</summary>
        /// <param name="shift">The letter shift. Values are normalized modulo 26 by the underlying codec.</param>
        /// <returns>A preparation that decodes values created with the same normalized Caesar shift.</returns>
        /// <remarks>
        /// Caesar shifting is deliberately weak obfuscation and analysis friction; it provides no cryptographic protection. The
        /// normalized shift is persisted in the encoded payload and is not secret. Its purpose is limited to adding small extra
        /// work to trivial inspection while remaining generically reversible without application-specific secret state.
        /// </remarks>
        public static JsonConfigurationCandidatePreparation Caesar(int shift)
        {
            return Decode(JsonSettingsValueEncoders.Caesar(shift));
        }

        /// <summary>Decodes values produced by the Windows DPAPI LocalMachine codec.</summary>
        /// <remarks>
        /// LocalMachine binds the payload to the Windows machine, not to an administrator or individual user. Windows permits
        /// another user on the same machine to unprotect a LocalMachine payload; the security value of this layer is the
        /// originating machine-context requirement. Such values are intentionally non-portable across machines.
        /// </remarks>
        public static JsonConfigurationCandidatePreparation DpapiMachine { get; } =
            Decode(JsonSettingsValueEncoders.DpapiMachine);

        /// <summary>Decodes values produced by the Windows DPAPI LocalMachine Base64Url codec.</summary>
        /// <remarks>
        /// LocalMachine binds the payload to the Windows machine, not to an administrator or individual user. Another user on the
        /// same machine may be able to unprotect it. Base64Url is only the persisted representation around the DPAPI bytes and
        /// does not add another protection factor. The underlying persisted token remains compatible with the historical
        /// DPAPI-machine Base64Url naming used by the existing codec implementation.
        /// </remarks>
        public static JsonConfigurationCandidatePreparation DpapiMachineBase64Url { get; } =
            Decode(JsonSettingsValueEncoders.DpapiMachineBase64Url);

        /// <summary>Creates a candidate preparation for password-derived AES-GCM values.</summary>
        /// <param name="password">The non-empty visible-ASCII password used by the existing AES codec.</param>
        /// <returns>A preparation carrying the same parameterized decode context as the corresponding value codec.</returns>
        /// <remarks>
        /// The security of this backend is bounded by how the caller obtains and protects the supplied password. The underlying
        /// codec captures the password for its lifetime, so callers should assume it is recoverable from a sufficiently
        /// compromised running process or from static analysis when embedded directly in the consuming executable. The existing
        /// codec uses a versioned AES-GCM payload; its internal Base64Url fields are storage representations for binary payload
        /// parts, not additional protection layers. Future KDF/cipher changes require a deliberate new persisted payload version
        /// or migration. This preparation neither changes nor migrates that format.
        /// </remarks>
        public static JsonConfigurationCandidatePreparation AesPassword(string password)
        {
            return Decode(JsonSettingsValueEncoders.AesPassword(password));
        }

        /// <summary>Creates a candidate preparation for password-derived AES-GCM values from visible ASCII password bytes.</summary>
        /// <param name="passwordAsciiBytes">
        /// Visible ASCII bytes representing the same password text accepted by <see cref="AesPassword(string)"/>.
        /// </param>
        /// <returns>A preparation equivalent to using the represented visible-ASCII password string.</returns>
        /// <remarks>
        /// This overload can avoid placing a clear password in the assembly string-literal table, but it is only a small
        /// static-analysis obstacle and is not a secrecy boundary. The bytes and normalization logic remain recoverable from the
        /// executable. The underlying codec deliberately rejects bytes outside visible ASCII 0x21 through 0x7E so accidental
        /// binary values cannot silently create a different password context.
        /// </remarks>
        public static JsonConfigurationCandidatePreparation AesPassword(byte[] passwordAsciiBytes)
        {
            return Decode(JsonSettingsValueEncoders.AesPassword(passwordAsciiBytes));
        }

        /// <summary>Creates a candidate preparation for values protected by the physical-machine-bound AES shortcut.</summary>
        /// <returns>A preparation bound to the current Windows, Linux, or macOS system/platform UUID context.</returns>
        /// <remarks>
        /// <para>
        /// This is lightweight machine binding, not a hardware-backed secret. Its intended value is to make application-directory-
        /// only theft insufficient for offline decoding on another machine unless the attacker also collected the source machine's
        /// platform identity. An attacker with sufficient access to the source machine can read the same identity and reproduce
        /// the fingerprint.
        /// </para>
        /// <para>
        /// The persisted value remains an ordinary versioned AES-password payload. Decoding therefore requires the same
        /// machine-derived codec context that was used to encode it; this preparation adds no migration mechanism of its own.
        /// The lightweight fingerprint source intentionally requires no broader hardware/CIM inventory package.
        /// </para>
        /// </remarks>
        /// <exception cref="PlatformNotSupportedException">The current operating system is not supported by the machine binding.</exception>
        /// <exception cref="InvalidOperationException">No valid system/platform UUID is available.</exception>
        public static JsonConfigurationCandidatePreparation PhysicalMachineBoundAes()
        {
            return Decode(JsonSettingsValueEncoders.PhysicalMachineBoundAes());
        }

        /// <summary>Creates a candidate preparation for the default ASP.NET Core Data Protection codec.</summary>
        /// <param name="keyDirectoryPath">The persistent file-system key-ring directory.</param>
        /// <returns>
        /// A preparation using the entry assembly name as application discriminator and the library's stable JSON-settings
        /// purpose, exactly like <see cref="JsonSettingsValueEncoders.DataProtection(string)"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The key-ring directory is durable settings state. Data Protection owns the individual key file names and may create
        /// multiple files as keys rotate. Back up the complete key ring with protected settings and retain old keys while any
        /// persisted value may still depend on them. Losing or deleting keys can make existing settings permanently unreadable.
        /// </para>
        /// <para>
        /// The default application discriminator follows the entry assembly name. Use the explicit overload when protected values
        /// must survive an application rename. Moving the same key ring to another machine is sufficient to use this codec there
        /// unless another composed protection layer adds machine binding. Directory separation is not an ACL boundary, and this
        /// codec does not configure an additional at-rest encryptor for key-ring files.
        /// </para>
        /// <para>
        /// A missing key-ring directory may be created by the underlying codec. On an existing installation an unexpectedly new or
        /// empty key ring should therefore be treated as lost durable state, not as a successful migration. Conventional hosts
        /// should normally keep this state separate from application settings, for example in an AppState directory.
        /// </para>
        /// </remarks>
        public static JsonConfigurationCandidatePreparation DataProtection(string keyDirectoryPath)
        {
            return Decode(JsonSettingsValueEncoders.DataProtection(keyDirectoryPath));
        }

        /// <summary>Creates a candidate preparation for Data Protection with explicit application and purpose isolation.</summary>
        /// <param name="keyDirectoryPath">The persistent file-system key-ring directory.</param>
        /// <param name="applicationName">The stable logical application discriminator for the key ring.</param>
        /// <param name="purpose">The stable purpose isolating these JSON-settings values from other protected data.</param>
        /// <returns>A preparation backed by the specified Data Protection context.</returns>
        /// <remarks>
        /// Keep both <paramref name="applicationName"/> and <paramref name="purpose"/> stable for as long as persisted values must
        /// remain readable. Changing either makes those values unavailable to the new protector. The key ring is durable state and
        /// must retain all keys that may still protect persisted settings. Data Protection alone adds no machine binding; callers
        /// that require it must use a codec composition that deliberately adds that property.
        /// </remarks>
        public static JsonConfigurationCandidatePreparation DataProtection(
            string keyDirectoryPath,
            string applicationName,
            string purpose)
        {
            return Decode(JsonSettingsValueEncoders.DataProtection(keyDirectoryPath, applicationName, purpose));
        }

        /// <summary>Creates the platform-neutral V1 default candidate preparation.</summary>
        /// <param name="password">The caller-supplied visible-ASCII password used by the application AES layer.</param>
        /// <param name="keyDirectoryPath">The persistent ASP.NET Core Data Protection key-ring directory.</param>
        /// <returns>
        /// A preparation decoding values produced by the exact V1 default codec defined by
        /// <see cref="JsonSettingsValueEncoders.Default(string,string)"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The exact underlying V1 encoding order is <c>Rot13 -&gt; Caesar(13) -&gt; DataProtection -&gt;
        /// PhysicalMachineBoundAes -&gt; AesPassword -&gt; Base92JsonSafe</c>; decoding is performed by that codec in reverse order.
        /// This candidate facade adapts the complete codec as one step and must not reconstruct or reorder those stages.
        /// </para>
        /// <para>
        /// The V1 default deliberately combines protection factors with low-cost friction layers. Data Protection requires the
        /// persistent key ring, PhysicalMachineBoundAes requires the source system/platform identity, and AesPassword requires the
        /// caller-supplied password. ROT13 and Caesar provide only reversible obfuscation/analysis friction; Base92JsonSafe is a
        /// representation layer that also removes immediately recognizable inner wrapper text. Those friction layers are not
        /// cryptographic security boundaries and must not be counted as independent secret factors.
        /// </para>
        /// <para>
        /// Physical-machine binding is a lightweight recovery hurdle, not a hardware-backed secret. An attacker who collected the
        /// source machine's platform identity can reproduce that factor. A sufficiently compromised running process can also
        /// observe passwords and decoded clear values. The pipeline is defense in depth and analysis friction, not an absolute
        /// security boundary.
        /// </para>
        /// <para>
        /// This shortcut adapts the existing V1 persisted pipeline contract and must remain exactly compatible with it. The stage
        /// order, passwords, Data Protection isolation, or default layout must not be changed silently. A future default layout
        /// requires an explicit new persisted version or deliberate backward decoding/migration in the codec layer.
        /// </para>
        /// <para>
        /// The Data Protection key ring is durable settings state: back it up with the protected settings and retain old keys while
        /// persisted values may depend on them. An unexpectedly empty/new key ring on an existing installation indicates lost
        /// state rather than successful migration. The default application discriminator derives from the entry assembly name;
        /// callers that require values to survive an application rename should build an explicit codec using the stable
        /// DataProtection overload and adapt it with <see cref="Decode(JsonSettingsValueCodec)"/>.
        /// </para>
        /// </remarks>
        /// <exception cref="PlatformNotSupportedException">The current operating system is not supported for physical machine binding.</exception>
        /// <exception cref="InvalidOperationException">No valid system/platform UUID is available for physical machine binding.</exception>
        public static JsonConfigurationCandidatePreparation Default(string password, string keyDirectoryPath)
        {
            return Decode(JsonSettingsValueEncoders.Default(password, keyDirectoryPath));
        }

        /// <summary>Creates the platform-neutral V1 default candidate preparation from visible ASCII password bytes.</summary>
        /// <param name="passwordAsciiBytes">Visible ASCII bytes representing the password text.</param>
        /// <param name="keyDirectoryPath">The persistent ASP.NET Core Data Protection key-ring directory.</param>
        /// <returns>The same default preparation as the equivalent string password.</returns>
        /// <remarks>
        /// This overload exists for embedded application material when a caller wants to avoid a clear password string literal.
        /// It does not make that password secret from executable analysis. All security, durable-state, machine-binding, V1
        /// compatibility, and migration remarks documented on <see cref="Default(string,string)"/> apply equally here.
        /// </remarks>
        public static JsonConfigurationCandidatePreparation Default(byte[] passwordAsciiBytes, string keyDirectoryPath)
        {
            return Decode(JsonSettingsValueEncoders.Default(passwordAsciiBytes, keyDirectoryPath));
        }

        /// <summary>Creates the Windows V1 default candidate preparation.</summary>
        /// <param name="password">The caller-supplied visible-ASCII password used by the application AES layer.</param>
        /// <param name="keyDirectoryPath">The persistent ASP.NET Core Data Protection key-ring directory.</param>
        /// <returns>
        /// A preparation decoding the exact Windows default produced by
        /// <see cref="JsonSettingsValueEncoders.DefaultWindows(string,string)"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The exact persisted relationship is equivalent to <c>Compose(Default(password, keyDirectoryPath),
        /// DpapiMachineBase64Url)</c>; the outer DPAPI layer is therefore removed first during decode.
        /// </para>
        /// <para>
        /// The Windows default adds DPAPI LocalMachine outside the complete platform-neutral V1 default. LocalMachine is machine
        /// scope, not user or administrator isolation: Windows permits another user on the same machine to unprotect a
        /// LocalMachine payload. The intended additional requirement is access to the originating Windows machine context, not
        /// elevated privileges. Base64Url is a representation of the DPAPI payload, not another protection factor. The underlying
        /// implementation invokes Windows DPAPI through the operating-system API and does not depend on a separate
        /// System.Security.Cryptography.ProtectedData package.
        /// </para>
        /// <para>
        /// All durable key-ring, password, machine-binding, process-compromise, V1 compatibility, and migration remarks on
        /// <see cref="Default(string,string)"/> also apply. This preparation adapts the existing persisted codec and contains no
        /// independent cryptographic construction or migration logic.
        /// </para>
        /// </remarks>
        /// <exception cref="PlatformNotSupportedException">The underlying Windows DPAPI context is unavailable.</exception>
        public static JsonConfigurationCandidatePreparation DefaultWindows(string password, string keyDirectoryPath)
        {
            return Decode(JsonSettingsValueEncoders.DefaultWindows(password, keyDirectoryPath));
        }

        /// <summary>Creates the Windows V1 default candidate preparation from visible ASCII password bytes.</summary>
        /// <param name="passwordAsciiBytes">Visible ASCII bytes representing the password text.</param>
        /// <param name="keyDirectoryPath">The persistent ASP.NET Core Data Protection key-ring directory.</param>
        /// <returns>The same Windows default preparation as the equivalent string password.</returns>
        /// <remarks>
        /// Avoiding a clear string literal is only a small static-analysis obstacle, not a secrecy boundary. All Windows machine-
        /// scope, durable-state, V1 compatibility, and migration remarks on <see cref="DefaultWindows(string,string)"/> apply.
        /// </remarks>
        public static JsonConfigurationCandidatePreparation DefaultWindows(byte[] passwordAsciiBytes, string keyDirectoryPath)
        {
            return Decode(JsonSettingsValueEncoders.DefaultWindows(passwordAsciiBytes, keyDirectoryPath));
        }

        /// <summary>Creates the DPAPI-machine-scope then ROT13 codec shortcut as one candidate preparation.</summary>
        /// <returns>A preparation adapting <see cref="JsonSettingsValueEncoders.DpapiWithRot13"/>.</returns>
        /// <remarks>
        /// The shortcut contains no independent transformation logic. In the persisted codec, encoding applies DPAPI first and
        /// ROT13 second; decoding therefore applies ROT13 first and DPAPI second. ROT13 adds analysis friction only and is not an
        /// additional cryptographic factor. DPAPI retains its LocalMachine—not user/admin—isolation semantics.
        /// </remarks>
        public static JsonConfigurationCandidatePreparation DpapiWithRot13()
        {
            return Decode(JsonSettingsValueEncoders.DpapiWithRot13());
        }

        /// <summary>Creates the DPAPI-machine-scope then Caesar codec shortcut as one candidate preparation.</summary>
        /// <param name="shift">The Caesar letter shift; values are normalized modulo 26 by the underlying codec.</param>
        /// <returns>A preparation adapting <see cref="JsonSettingsValueEncoders.DpapiWithCaesar(int)"/>.</returns>
        /// <remarks>
        /// The shortcut contains no independent transformation logic. In the persisted codec, encoding applies DPAPI first and
        /// Caesar second; decoding therefore applies Caesar first and DPAPI second. Caesar adds analysis friction only; the shift
        /// is not a secret, and DPAPI retains its LocalMachine—not user/admin—isolation semantics.
        /// </remarks>
        public static JsonConfigurationCandidatePreparation DpapiWithCaesar(int shift)
        {
            return Decode(JsonSettingsValueEncoders.DpapiWithCaesar(shift));
        }

        /// <summary>
        /// Adapts any existing reversible JSON-settings value codec to candidate preparation.
        /// </summary>
        /// <param name="codec">The complete reversible codec context used to decode matching persisted values.</param>
        /// <returns>A decode-only in-memory candidate preparation backed by that codec.</returns>
        /// <remarks>
        /// <para>
        /// The supplied codec is authoritative. Parameterized or composed values require the same relevant context that encoded
        /// them, including passwords, Data Protection application/purpose isolation, machine context, and composed stage order.
        /// When complete decoding fails, the original encoded value is retained; this adapter does not fall back to generic
        /// decoding and does not expose a partially removed outer layer from a composed value.
        /// </para>
        /// <para>
        /// This method performs no encoding and no file rewrite. Changing codec parameters or composition is therefore not a
        /// migration strategy. Persisted migration remains an explicit responsibility of the codec/file-writing layer.
        /// </para>
        /// </remarks>
        public static JsonConfigurationCandidatePreparation Decode(JsonSettingsValueCodec codec)
        {
            ArgumentNullException.ThrowIfNull(codec);
            return new JsonConfigurationCandidatePreparation(
                $"Decode({codec.Name})",
                new CodecPreparation(codec));
        }

        /// <summary>
        /// Wraps one custom low-level preparation in the reusable application-facing candidate-preparation type.
        /// </summary>
        /// <param name="name">A stable descriptive name for diagnostics and composed bundle descriptions.</param>
        /// <param name="preparation">The low-level candidate preparation implementation.</param>
        /// <returns>The existing candidate bundle when already wrapped; otherwise a new named wrapper.</returns>
        /// <remarks>
        /// The wrapper does not sandbox or roll back arbitrary external side effects performed by custom code. Custom
        /// preparations remain subject to the <see cref="IJsonConfigurationSourcePreparation"/> contract: operate only on the
        /// supplied isolated candidate, be safe for repeated/concurrent invocation, and use exceptions to reject a candidate.
        /// </remarks>
        public static JsonConfigurationCandidatePreparation From(
            string name,
            IJsonConfigurationSourcePreparation preparation)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(preparation);
            return preparation is JsonConfigurationCandidatePreparation candidate
                ? candidate
                : new JsonConfigurationCandidatePreparation(name, preparation);
        }

        /// <summary>
        /// Composes candidate preparations into one reusable bundle. Steps execute in declaration order.
        /// </summary>
        /// <param name="preparations">Candidate-level operations in execution order.</param>
        /// <returns>One reusable bundle executing every supplied preparation in order.</returns>
        /// <remarks>
        /// <para>
        /// Candidate composition is intentionally different from <see cref="JsonSettingsValueEncoders.Compose"/>. This method
        /// composes complete candidate-level operations in execution order, for example decode, then validate, then normalize.
        /// Codec composition still belongs to <see cref="JsonSettingsValueEncoders.Compose"/>, whose encoding order is the
        /// declaration order and whose decoding order is the reverse. Its nested wrappers describe stages but are not a migration
        /// manifest: changing passwords, Data Protection isolation, transform parameters, or stage order requires deliberate
        /// backward decoding or migration. The built-in Default/DefaultWindows helpers therefore adapt the already-composed value
        /// codec rather than rebuilding its individual stages as candidate preparations.
        /// </para>
        /// <para>
        /// Composition is not a transaction over arbitrary external side effects. If a later preparation throws, the JSON owner
        /// rejects the isolated candidate and leaves published provider/runtime state unchanged, but it cannot undo side effects a
        /// custom preparation performed outside the supplied candidate dictionary.
        /// </para>
        /// </remarks>
        public static JsonConfigurationCandidatePreparation Compose(
            params IJsonConfigurationSourcePreparation[] preparations)
        {
            ArgumentNullException.ThrowIfNull(preparations);
            if (preparations.Length == 0)
            {
                throw new ArgumentException("At least one candidate preparation is required.", nameof(preparations));
            }

            var steps = new IJsonConfigurationSourcePreparation[preparations.Length];
            for (int index = 0; index < preparations.Length; index++)
            {
                steps[index] = preparations[index] ??
                    throw new ArgumentException($"Candidate preparation at index {index} is null.", nameof(preparations));
            }

            string name = string.Join(
                " -> ",
                steps.Select(step => step is JsonConfigurationCandidatePreparation candidate
                    ? candidate.Name
                    : step.GetType().Name));

            return new JsonConfigurationCandidatePreparation(
                name,
                new CompositePreparation(steps));
        }

        private sealed class CodecPreparation : IJsonConfigurationSourcePreparation
        {
            private readonly JsonSettingsValueCodec _codec;

            public CodecPreparation(JsonSettingsValueCodec codec)
            {
                _codec = codec;
            }

            public void Prepare(JsonConfigurationSourcePreparationContext context)
            {
                ArgumentNullException.ThrowIfNull(context);

                foreach (string key in context.Values.Keys.ToArray())
                {
                    string? value = context.Values[key];
                    if (value is not null && _codec.TryDecode(value, out string clearText))
                    {
                        context.Values[key] = clearText;
                    }
                }
            }
        }

        private sealed class CompositePreparation : IJsonConfigurationSourcePreparation
        {
            private readonly IReadOnlyList<IJsonConfigurationSourcePreparation> _steps;

            public CompositePreparation(IReadOnlyList<IJsonConfigurationSourcePreparation> steps)
            {
                _steps = steps;
            }

            public void Prepare(JsonConfigurationSourcePreparationContext context)
            {
                ArgumentNullException.ThrowIfNull(context);
                JsonConfigurationSourcePreparationPipeline.Apply(
                    context.SourcePath,
                    context.Values,
                    _steps);
            }
        }
    }
}
