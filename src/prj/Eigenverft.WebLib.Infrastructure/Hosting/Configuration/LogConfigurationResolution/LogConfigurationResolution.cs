// -----------------------------------------------------------------------------
// ConfigurationPrecedenceDiagnosticsExtensions.cs
//
// Minimal configuration diagnostics:
//   1) Log provider precedence order once (single line, explicit direction).
//   2) Warn only for keys that exist in 2+ providers (collisions), including the full resolution chain.
//
// Notes:
//   - "Winner" is the highest-precedence provider that contains the key (last provider wins).
//   - Uses reflection to read provider "Data" dictionaries (diagnostics-only).
//   - Logs keys only (no values) to avoid leaking secrets.
//   - Resolution chains name provider origins directly so precedence is readable without internal provider indices.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Logging;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.LogConfigurationResolution
{
    /// <summary>
    /// Minimal diagnostics for configuration provider order and key collisions.
    /// </summary>
    public static class ConfigurationPrecedenceDiagnosticsExtensions
    {
        /// <summary>
        /// Logs configuration-provider precedence and reports keys shadowed by higher-precedence providers.
        /// Call this as late as possible after the configuration-provider stack is complete and before <c>Build()</c>.
        /// </summary>
        /// <param name="builder">The web application builder whose current configuration-provider stack is inspected.</param>
        /// <param name="logger">The logger receiving structured precedence and collision messages. Configuration values are never logged.</param>
        /// <param name="orderLogLevel">The level used for the provider-precedence message. The default is <see cref="LogLevel.Information"/>.</param>
        /// <param name="collisionLogLevel">The level used for collision and incomplete-scan messages. The default is <see cref="LogLevel.Warning"/>.</param>
        /// <returns>The same <paramref name="builder"/> instance for chaining.</returns>
        /// <remarks>
        /// Add all declarative configuration providers before calling this method so the reported order represents the
        /// effective startup configuration. Prefer calling it before injecting runtime-only values that are not part of
        /// the intended provider-precedence model.
        /// <para>
        /// Example:
        /// </para>
        /// <code><![CDATA[
        /// var builder = WebApplication.CreateBuilder(args);
        ///
        /// // Add/reset/load the complete configuration-provider stack first.
        /// builder.Configuration.AddJsonFile("appsettings.json");
        /// builder.Configuration.AddEnvironmentVariables();
        ///
        /// var startupLogger = LoggerFactory.Create(logging => logging.AddConsole()).CreateLogger("Startup");
        /// builder.LogConfigurationResolution(startupLogger);
        ///
        /// var app = builder.Build();
        /// ]]></code>
        /// </remarks>
        public static WebApplicationBuilder LogConfigurationResolution(
            this WebApplicationBuilder builder,
            ILogger logger,
            LogLevel orderLogLevel = LogLevel.Information,
            LogLevel collisionLogLevel = LogLevel.Warning)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(logger);

            LogProviderOrder(builder.Configuration, logger, orderLogLevel);
            LogKeyCollisions(builder.Configuration, logger, collisionLogLevel);

            return builder;
        }

        // Future generic-host overload intentionally remains disabled until it is part of the supported public API.
        //public static HostApplicationBuilder LogConfigurationResolution(
        //    this HostApplicationBuilder builder,
        //    ILogger logger,
        //    LogLevel orderLogLevel = LogLevel.Information,
        //    LogLevel collisionLogLevel = LogLevel.Warning)
        //{
        //    ArgumentNullException.ThrowIfNull(builder);
        //    ArgumentNullException.ThrowIfNull(logger);

        //    LogProviderOrder(builder.Configuration, logger, orderLogLevel);
        //    LogKeyCollisions(builder.Configuration, logger, collisionLogLevel);

        //    return builder;
        //}

        /// <summary>
        /// Logs the configuration provider order (precedence chain) once, as a single line with explicit direction.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Prints provider origins in highest-to-lowest precedence order without exposing internal provider indices.
        /// </para>
        /// <para>
        /// Example:
        /// </para>
        /// <code><![CDATA[
        /// ConfigurationPrecedenceDiagnosticsExtensions.LogProviderOrder(builder.Configuration, logger);
        /// ]]></code>
        /// </remarks>
        public static void LogProviderOrder(IConfiguration configuration, ILogger logger, LogLevel level = LogLevel.Information)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(logger);

            if (configuration is not IConfigurationRoot root)
            {
                logger.Log(level, "Config provider order dump skipped: IConfigurationRoot not available.");
                return;
            }

            var providers = root.Providers.ToList();
            var sources = TryGetSources(configuration);

            var origins = new List<string>(providers.Count);
            for (var i = 0; i < providers.Count; i++)
            {
                var source = (sources is not null && i < sources.Count) ? sources[i] : null;
                origins.Add(DescribeOrigin(providers[i], source));
            }

            origins.Reverse();

            logger.Log(
                level,
                "Config precedence (highest -> lowest): {Resolution}",
                string.Join(" -> ", origins));
        }

        /// <summary>
        /// Warns only for keys that exist in 2+ providers and shows the resolution chain (winner first).
        /// </summary>
        /// <remarks>
        /// <para>
        /// This emits a single summary line and then one warning per colliding key.
        /// The chain is printed in winner-first order (highest precedence to lowest precedence).
        /// </para>
        /// <para>
        /// Example:
        /// </para>
        /// <code><![CDATA[
        /// ConfigurationPrecedenceDiagnosticsExtensions.LogKeyCollisions(builder.Configuration, logger);
        /// ]]></code>
        /// </remarks>
        public static void LogKeyCollisions(IConfiguration configuration, ILogger logger, LogLevel level = LogLevel.Warning)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(logger);

            if (configuration is not IConfigurationRoot root)
            {
                logger.Log(level, "Config key collision scan skipped: IConfigurationRoot not available.");
                return;
            }

            var providers = root.Providers.ToList();
            var sources = TryGetSources(configuration);

            // key -> provider indices containing the key
            var keyToProviders = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < providers.Count; i++)
            {
                var provider = providers[i];
                var source = (sources is not null && i < sources.Count) ? sources[i] : null;
                var data = TryGetProviderDataDictionary(provider);
                if (data is null)
                {
                    logger.Log(
                        level,
                        "Configuration collision scan incomplete; provider {Provider} could not be inspected.",
                        DescribeOrigin(provider, source));
                    continue;
                }

                if (data.Count == 0)
                {
                    continue;
                }

                foreach (var key in data.Keys)
                {
                    if (IsExcludedCollisionKey(key))
                    {
                        continue;
                    }

                    if (!keyToProviders.TryGetValue(key, out var list))
                    {
                        list = new List<int>(capacity: 2);
                        keyToProviders[key] = list;
                    }

                    list.Add(i);
                }
            }

            var collisions = keyToProviders
                .Where(kvp => kvp.Value.Count >= 2)
                .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (collisions.Count == 0)
            {
                return;
            }

            logger.Log(level, "Configuration key collisions found: {Count}.", collisions.Count);

            foreach (var c in collisions)
            {
                var key = c.Key;
                var indices = c.Value;

                var chainOrigins = indices
                    .OrderByDescending(x => x)
                    .Select(idx =>
                    {
                        var provider = providers[idx];
                        var source = (sources is not null && idx < sources.Count) ? sources[idx] : null;
                        return DescribeOrigin(provider, source);
                    })
                    .ToList();

                logger.Log(
                    level,
                    "Config key collision on {Key}; winner {Winner} shadows {Shadowed}",
                    key,
                    chainOrigins[0],
                    string.Join(" shadows ", chainOrigins.Skip(1)));
            }
        }

        private static List<IConfigurationSource>? TryGetSources(IConfiguration configuration)
        {
            try
            {
                if (configuration is IConfigurationBuilder builder)
                {
                    return builder.Sources.ToList();
                }
            }
            catch
            {
                // Diagnostics only.
            }

            return null;
        }

        private static IDictionary<string, string?>? TryGetProviderDataDictionary(IConfigurationProvider provider)
        {
            try
            {
                var t = provider.GetType();
                while (t is not null)
                {
                    var prop = t.GetProperty("Data", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (prop is not null)
                    {
                        return prop.GetValue(provider) as IDictionary<string, string?>;
                    }

                    t = t.BaseType;
                }
            }
            catch
            {
                // Diagnostics only.
            }

            return null;
        }

        private static string DescribeOrigin(IConfigurationProvider provider, IConfigurationSource? source)
        {
            if (source is JsonConfigurationSource json)
            {
                var path = json.Path ?? string.Empty;
                return string.IsNullOrWhiteSpace(path) ? "json" : $"json:{Path.GetFileName(path)}";
            }

            var typeName = provider.GetType().Name;

            if (typeName.Contains("EnvironmentVariables", StringComparison.OrdinalIgnoreCase))
            {
                return "envars";
            }

            if (typeName.Contains("CommandLine", StringComparison.OrdinalIgnoreCase))
            {
                return "args";
            }

            if (typeName.Contains("Memory", StringComparison.OrdinalIgnoreCase) ||
                typeName.Contains("InMemory", StringComparison.OrdinalIgnoreCase))
            {
                return "memory";
            }

            return typeName;
        }

        /// <summary>
        /// Keys that should be excluded from collision diagnostics (case-insensitive).
        /// </summary>
        private static readonly HashSet<string> CollisionKeyExclusions = new(StringComparer.OrdinalIgnoreCase)
        {
            "$schema",
        };

        /// <summary>
        /// Determines whether a configuration key should be excluded from collision diagnostics.
        /// </summary>
        /// <param name="key">The configuration key.</param>
        /// <returns>
        /// <c>true</c> if the key should be excluded; otherwise <c>false</c>.
        /// </returns>
        private static bool IsExcludedCollisionKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return true;
            }

            // Configuration keys are hierarchical using ConfigurationPath.KeyDelimiter (typically ":").
            // Exclude "$schema" both at root and when it appears as the last segment (e.g. "MySection:$schema").
            var delimiter = ConfigurationPath.KeyDelimiter;
            var lastDelimiter = key.LastIndexOf(delimiter, StringComparison.Ordinal);

            var lastSegment = lastDelimiter < 0
                ? key
                : key[(lastDelimiter + delimiter.Length)..];

            return CollisionKeyExclusions.Contains(lastSegment);
        }
    }
}