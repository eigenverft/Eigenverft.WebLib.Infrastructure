using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.JsonSettings
{
    /// <summary>Adds JSON configuration providers whose isolated snapshots pass through generic source preparations.</summary>
    public static class PreparedJsonConfigurationExtensions
    {
        /// <summary>
        /// Adds one JSON file whose parsed snapshot is prepared before it replaces provider state.
        /// </summary>
        public static IConfigurationBuilder AddPreparedJsonFile(
            this IConfigurationBuilder builder,
            string path,
            IEnumerable<IJsonConfigurationSourcePreparation> sourcePreparations,
            bool optional = false,
            bool reloadOnChange = false)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(sourcePreparations);

            IJsonConfigurationSourcePreparation[] preparations = sourcePreparations.ToArray();
            if (preparations.Any(preparation => preparation is null))
            {
                throw new ArgumentException("Source preparations cannot contain null entries.", nameof(sourcePreparations));
            }

            var source = new PreparedJsonConfigurationSource
            {
                Path = path,
                Optional = optional,
                ReloadOnChange = reloadOnChange,
                SourcePreparations = preparations,
            };

            source.ResolveFileProvider();
            builder.Add(source);
            return builder;
        }

        private sealed class PreparedJsonConfigurationSource : JsonConfigurationSource
        {
            public IReadOnlyList<IJsonConfigurationSourcePreparation> SourcePreparations { get; init; } =
                Array.Empty<IJsonConfigurationSourcePreparation>();

            public override IConfigurationProvider Build(IConfigurationBuilder builder)
            {
                EnsureDefaults(builder);
                ResolveFileProvider();
                return new PreparedJsonConfigurationProvider(this);
            }
        }

        private sealed class PreparedJsonConfigurationProvider : JsonConfigurationProvider
        {
            private readonly string _sourcePath;
            private readonly IReadOnlyList<IJsonConfigurationSourcePreparation> _sourcePreparations;

            public PreparedJsonConfigurationProvider(PreparedJsonConfigurationSource source)
                : base(source)
            {
                _sourcePath = source.Path ?? "prepared-json";
                _sourcePreparations = source.SourcePreparations;
            }

            public override void Load(Stream stream)
            {
                IDictionary<string, string?> candidateData = ParseSnapshot(stream);
                JsonConfigurationSourcePreparationPipeline.Apply(
                    _sourcePath,
                    candidateData,
                    _sourcePreparations);

                Data = new Dictionary<string, string?>(candidateData, StringComparer.OrdinalIgnoreCase);
            }

            private static IDictionary<string, string?> ParseSnapshot(Stream stream)
            {
                var parser = new SnapshotJsonConfigurationProvider();
                return parser.Parse(stream);
            }

            private sealed class SnapshotJsonConfigurationProvider : JsonConfigurationProvider
            {
                public SnapshotJsonConfigurationProvider()
                    : base(new JsonConfigurationSource
                    {
                        Path = "prepared-json-snapshot.json",
                        Optional = false,
                        ReloadOnChange = false,
                    })
                {
                }

                public IDictionary<string, string?> Parse(Stream stream)
                {
                    base.Load(stream);
                    return new Dictionary<string, string?>(Data, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
    }
}
