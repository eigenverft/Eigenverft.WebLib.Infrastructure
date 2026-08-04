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
        /// <returns>The same configuration builder for chaining.</returns>
        /// <remarks>
        /// The file remains encoded on disk. Plain values and malformed or unavailable encoded values remain
        /// unchanged in configuration; consumers can therefore report errors in their own configuration domain.
        /// </remarks>
        public static IConfigurationBuilder AddJsonFileWithDecodedValues(
            this IConfigurationBuilder builder,
            string path,
            bool optional = false,
            bool reloadOnChange = false)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            var source = new DecodingJsonConfigurationSource
            {
                Path = path,
                Optional = optional,
                ReloadOnChange = reloadOnChange,
            };

            source.ResolveFileProvider();
            builder.Add(source);
            return builder;
        }

        private sealed class DecodingJsonConfigurationSource : JsonConfigurationSource
        {
            public override IConfigurationProvider Build(IConfigurationBuilder builder)
            {
                EnsureDefaults(builder);
                ResolveFileProvider();
                return new DecodingJsonConfigurationProvider(this);
            }
        }

        private sealed class DecodingJsonConfigurationProvider : JsonConfigurationProvider
        {
            public DecodingJsonConfigurationProvider(JsonConfigurationSource source)
                : base(source)
            {
            }

            public override void Load(Stream stream)
            {
                base.Load(stream);

                foreach (string key in Data.Keys.ToList())
                {
                    string? value = Data[key];

                    if (value is not null &&
                        EncodedConfigurationValueDecoder.TryDecode(value, out string clearText))
                    {
                        Data[key] = clearText;
                    }
                }
            }
        }
    }
}
