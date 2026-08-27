using System;
using System.Collections.Generic;

namespace Eigenverft.WebLib.Infrastructure.Hosting.StaticFiles
{
    /// <summary>
    /// Provides predefined groups of MIME mappings that backfill extensions missing from the target framework's
    /// ASP.NET Core static-file defaults.
    /// </summary>
    public static class AdditionalMappings
    {
        private static readonly StaticFileAdditionalMappings Empty = Create();

        /// <summary>
        /// Backfills static-file mappings needed by legacy PWA/Blazor hosting that are still absent from the current
        /// ASP.NET Core defaults.
        /// </summary>
        /// <remarks>
        /// ASP.NET Core already maps <c>.webmanifest</c> and <c>.wasm</c> for the supported target frameworks, so this
        /// group currently adds only <c>.br</c> and <c>.dat</c> as <c>application/octet-stream</c>.
        /// </remarks>
        public static StaticFileAdditionalMappings WebApp { get; } = Create(
            new KeyValuePair<string, string>(".br", "application/octet-stream"),
            new KeyValuePair<string, string>(".dat", "application/octet-stream"));

        /// <summary>
        /// Backfills web-media mappings that differ across the supported ASP.NET Core target frameworks.
        /// </summary>
        /// <remarks>
        /// On <c>net8.0</c>, this adds <c>.avif</c> as <c>image/avif</c>. ASP.NET Core 10 already provides that mapping,
        /// so the group is intentionally empty on <c>net10.0</c>.
        /// </remarks>
        public static StaticFileAdditionalMappings Media { get; } = CreateMediaMappings();

        /// <summary>
        /// Combines multiple predefined mapping groups into one typed group.
        /// </summary>
        /// <param name="groups">The groups to combine.</param>
        /// <returns>A mapping group containing the union of the supplied groups.</returns>
        public static StaticFileAdditionalMappings Combine(params StaticFileAdditionalMappings[] groups)
        {
            ArgumentNullException.ThrowIfNull(groups);

            if (groups.Length == 0)
            {
                return Empty;
            }

            var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (StaticFileAdditionalMappings group in groups)
            {
                if (group is null)
                {
                    throw new ArgumentException("Mapping groups must not contain null values.", nameof(groups));
                }

                foreach (KeyValuePair<string, string> mapping in group.Mappings)
                {
                    if (mappings.TryGetValue(mapping.Key, out string? existing) &&
                        !string.Equals(existing, mapping.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"Conflicting MIME mappings were supplied for extension '{mapping.Key}'.");
                    }

                    mappings[mapping.Key] = mapping.Value;
                }
            }

            return new StaticFileAdditionalMappings(mappings);
        }

        private static StaticFileAdditionalMappings Create(params KeyValuePair<string, string>[] mappings)
        {
            return new StaticFileAdditionalMappings(
                new Dictionary<string, string>(mappings, StringComparer.OrdinalIgnoreCase));
        }

        private static StaticFileAdditionalMappings CreateMediaMappings()
        {
#if NET8_0
            return Create(new KeyValuePair<string, string>(".avif", "image/avif"));
#else
            return Empty;
#endif
        }
    }
}
