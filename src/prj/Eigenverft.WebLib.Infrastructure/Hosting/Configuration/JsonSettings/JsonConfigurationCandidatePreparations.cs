using System;
using System.Collections.Generic;
using System.Linq;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.JsonSettings
{
    /// <summary>
    /// Represents one reusable candidate-preparation bundle that can be assigned to JSON configuration registrations.
    /// </summary>
    /// <remarks>
    /// This is the application-facing wrapper around the lower-level <see cref="IJsonConfigurationSourcePreparation"/>
    /// extension contract. A bundle may adapt one reversible value codec, compose several candidate preparations, or wrap a
    /// custom preparation supplied by an application. It never owns source selection or provider publication.
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
    /// The existing value-encoder API remains the persisted-format authority. These helpers adapt those codecs to the generic
    /// candidate-preparation boundary; they do not duplicate cryptographic or encoding implementations. A codec-backed
    /// preparation scans the parsed candidate snapshot and replaces values only when the selected codec can completely decode
    /// them. Plain values and encoded values belonging to a different codec remain unchanged, matching the explicit-codec
    /// behavior of the existing decoded JSON providers.
    /// </remarks>
    public static class JsonConfigurationCandidatePreparations
    {
        /// <summary>Decodes values produced by <see cref="JsonSettingsValueEncoders.Base64"/>.</summary>
        public static JsonConfigurationCandidatePreparation Base64 { get; } =
            Decode(JsonSettingsValueEncoders.Base64);

        /// <summary>Decodes values produced by <see cref="JsonSettingsValueEncoders.Base92JsonSafe"/>.</summary>
        public static JsonConfigurationCandidatePreparation Base92JsonSafe { get; } =
            Decode(JsonSettingsValueEncoders.Base92JsonSafe);

        /// <summary>Decodes values produced by <see cref="JsonSettingsValueEncoders.Rot13"/>.</summary>
        public static JsonConfigurationCandidatePreparation Rot13 { get; } =
            Decode(JsonSettingsValueEncoders.Rot13);

        /// <summary>Creates a candidate preparation for the parameterized Caesar value codec.</summary>
        public static JsonConfigurationCandidatePreparation Caesar(int shift)
        {
            return Decode(JsonSettingsValueEncoders.Caesar(shift));
        }

        /// <summary>Decodes values produced by the Windows DPAPI LocalMachine codec.</summary>
        public static JsonConfigurationCandidatePreparation DpapiMachine { get; } =
            Decode(JsonSettingsValueEncoders.DpapiMachine);

        /// <summary>Decodes values produced by the Windows DPAPI LocalMachine Base64Url codec.</summary>
        public static JsonConfigurationCandidatePreparation DpapiMachineBase64Url { get; } =
            Decode(JsonSettingsValueEncoders.DpapiMachineBase64Url);

        /// <summary>Creates a candidate preparation for password-derived AES values.</summary>
        public static JsonConfigurationCandidatePreparation AesPassword(string password)
        {
            return Decode(JsonSettingsValueEncoders.AesPassword(password));
        }

        /// <summary>Creates a candidate preparation for password-derived AES values from visible ASCII password bytes.</summary>
        public static JsonConfigurationCandidatePreparation AesPassword(byte[] passwordAsciiBytes)
        {
            return Decode(JsonSettingsValueEncoders.AesPassword(passwordAsciiBytes));
        }

        /// <summary>Creates a candidate preparation for values protected by the physical-machine-bound AES shortcut.</summary>
        public static JsonConfigurationCandidatePreparation PhysicalMachineBoundAes()
        {
            return Decode(JsonSettingsValueEncoders.PhysicalMachineBoundAes());
        }

        /// <summary>Creates a candidate preparation for the default ASP.NET Core Data Protection codec.</summary>
        public static JsonConfigurationCandidatePreparation DataProtection(string keyDirectoryPath)
        {
            return Decode(JsonSettingsValueEncoders.DataProtection(keyDirectoryPath));
        }

        /// <summary>Creates a candidate preparation for Data Protection with explicit application and purpose isolation.</summary>
        public static JsonConfigurationCandidatePreparation DataProtection(
            string keyDirectoryPath,
            string applicationName,
            string purpose)
        {
            return Decode(JsonSettingsValueEncoders.DataProtection(keyDirectoryPath, applicationName, purpose));
        }

        /// <summary>Creates the platform-neutral V1 default candidate preparation.</summary>
        public static JsonConfigurationCandidatePreparation Default(string password, string keyDirectoryPath)
        {
            return Decode(JsonSettingsValueEncoders.Default(password, keyDirectoryPath));
        }

        /// <summary>Creates the platform-neutral V1 default candidate preparation from visible ASCII password bytes.</summary>
        public static JsonConfigurationCandidatePreparation Default(byte[] passwordAsciiBytes, string keyDirectoryPath)
        {
            return Decode(JsonSettingsValueEncoders.Default(passwordAsciiBytes, keyDirectoryPath));
        }

        /// <summary>Creates the Windows V1 default candidate preparation.</summary>
        public static JsonConfigurationCandidatePreparation DefaultWindows(string password, string keyDirectoryPath)
        {
            return Decode(JsonSettingsValueEncoders.DefaultWindows(password, keyDirectoryPath));
        }

        /// <summary>Creates the Windows V1 default candidate preparation from visible ASCII password bytes.</summary>
        public static JsonConfigurationCandidatePreparation DefaultWindows(byte[] passwordAsciiBytes, string keyDirectoryPath)
        {
            return Decode(JsonSettingsValueEncoders.DefaultWindows(passwordAsciiBytes, keyDirectoryPath));
        }

        /// <summary>Creates the DPAPI-machine then ROT13 shortcut as one candidate preparation.</summary>
        public static JsonConfigurationCandidatePreparation DpapiWithRot13()
        {
            return Decode(JsonSettingsValueEncoders.DpapiWithRot13());
        }

        /// <summary>Creates the DPAPI-machine then Caesar shortcut as one candidate preparation.</summary>
        public static JsonConfigurationCandidatePreparation DpapiWithCaesar(int shift)
        {
            return Decode(JsonSettingsValueEncoders.DpapiWithCaesar(shift));
        }

        /// <summary>
        /// Adapts any existing reversible JSON-settings value codec to candidate preparation.
        /// </summary>
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
        /// <remarks>
        /// Candidate composition is intentionally different from <see cref="JsonSettingsValueEncoders.Compose"/>: this method
        /// composes complete candidate-level operations in execution order. Codec composition still belongs to
        /// <see cref="JsonSettingsValueEncoders.Compose"/>, whose decoding order is the reverse of its encoding order. The
        /// built-in Default/DefaultWindows helpers therefore adapt the already-composed value codec rather than rebuilding its
        /// individual stages as candidate preparations.
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
