using System;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.JsonSettings
{
    /// <summary>
    /// Adds JSON providers that decode values produced by <see cref="JsonSettingsValueEncoders"/>.
    /// </summary>
    public static class DecodedJsonConfigurationExtensions
    {
        /// <summary>
        /// Adds a JSON file and decodes recognized values in memory whenever the provider loads.
        /// </summary>
        /// <param name="builder">The configuration builder receiving the provider.</param>
        /// <param name="path">The JSON file path.</param>
        /// <param name="optional">Whether the file may be absent.</param>
        /// <param name="reloadOnChange">Whether the provider reloads after file changes.</param>
        /// <param name="decodeCodec">
        /// Optional explicit codec used for decoding. When omitted, the built-in parameterless formats are decoded
        /// as before. Parameterized or composed codecs must be supplied explicitly.
        /// </param>
        /// <returns>The same configuration builder for chaining.</returns>
        /// <remarks>
        /// The file remains encoded on disk. Plain values and malformed or unavailable encoded values remain
        /// unchanged in configuration; consumers can therefore report errors in their own configuration domain.
        /// </remarks>
        public static IConfigurationBuilder AddJsonFileWithDecodedValues(
            this IConfigurationBuilder builder,
            string path,
            bool optional = false,
            bool reloadOnChange = false,
            JsonSettingsValueCodec? decodeCodec = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            var source = new DecodingJsonConfigurationSource
            {
                Path = path,
                Optional = optional,
                ReloadOnChange = reloadOnChange,
                DecodeCodec = decodeCodec,
            };

            source.ResolveFileProvider();
            builder.Add(source);
            return builder;
        }

        private sealed class DecodingJsonConfigurationSource : JsonConfigurationSource
        {
            public JsonSettingsValueCodec? DecodeCodec { get; init; }

            public override IConfigurationProvider Build(IConfigurationBuilder builder)
            {
                EnsureDefaults(builder);
                ResolveFileProvider();
                return new DecodingJsonConfigurationProvider(this);
            }
        }

        private sealed class DecodingJsonConfigurationProvider : JsonConfigurationProvider
        {
            private readonly JsonSettingsValueCodec? _decodeCodec;

            public DecodingJsonConfigurationProvider(DecodingJsonConfigurationSource source)
                : base(source)
            {
                _decodeCodec = source.DecodeCodec;
            }

            public override void Load(Stream stream)
            {
                base.Load(stream);

                foreach (string key in Data.Keys.ToList())
                {
                    string? value = Data[key];

                    if (value is null)
                    {
                        continue;
                    }

                    bool decoded = _decodeCodec is null
                        ? EncodedConfigurationValueDecoder.TryDecode(value, out string clearText)
                        : _decodeCodec.TryDecode(value, out clearText);

                    if (decoded)
                    {
                        Data[key] = clearText;
                    }
                }
            }
        }
    }
}
