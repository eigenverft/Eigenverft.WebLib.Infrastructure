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
//   - The chain message template is generated dynamically so the numeric indices are separate log parameters.
//     This helps console formatters color numbers consistently.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

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
        /// Logs the configuration provider order (precedence chain) and then warns only for keys that exist in multiple providers.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Intended as a lightweight, startup-only diagnostic. It prints:
        /// </para>
        /// <list type="bullet">
        /// <item><description>The full provider precedence chain (winner first).</description></item>
        /// <item><description>A warning per key that exists in 2+ providers, including the winner-first resolution chain.</description></item>
        /// </list>
        /// <para>
        /// Example:
        /// </para>
        /// <code><![CDATA[
        /// var builder = WebApplication.CreateBuilder(args);
        ///
        /// var startupLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("Startup");
        /// builder.LogConfigurationResolution(startupLogger);
        ///
        /// // ... add services, build, run
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

        /// <summary>
        /// Logs the configuration provider order (precedence chain) and then warns only for keys that exist in multiple providers.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Use this overload when you build a generic host (non-web) via <see cref="HostApplicationBuilder"/>.
        /// </para>
        /// <para>
        /// Example:
        /// </para>
        /// <code><![CDATA[
        /// var builder = Host.CreateApplicationBuilder(args);
        ///
        /// var startupLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("Startup");
        /// builder.LogConfigurationResolution(startupLogger);
        ///
        /// var host = builder.Build();
        /// host.Run();
        /// ]]></code>
        /// </remarks>
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
        /// Prints the chain in winner-first order (highest precedence first). Indices are logged as separate parameters
        /// so console formatters can color them distinctly.
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

            var items = new List<(int Index, string Origin)>(providers.Count);
            for (var i = 0; i < providers.Count; i++)
            {
                var s = (sources is not null && i < sources.Count) ? sources[i] : null;
                items.Add((i, DescribeOrigin(providers[i], s)));
            }

            // Print highest precedence first so the bigger index reads like "higher priority".
            var highestFirst = items.OrderByDescending(x => x.Index).ToList();

            var sb = new StringBuilder(capacity: 256);
            var args = new List<object?>(capacity: Math.Max(1, highestFirst.Count * 2));

            sb.Append("Config precedence (HIGHEST => LOWEST, winner first). ");
            AppendIndexedChainTemplate(sb, args, highestFirst);

            logger.Log(level, sb.ToString(), args.ToArray());
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
                var data = TryGetProviderDataDictionary(providers[i]);
                if (data is null || data.Count == 0)
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

                var winnerIndex = indices.Max();

                // Winner-first chain (HIGH -> LOW).
                var chainItems = indices
                    .OrderByDescending(x => x)
                    .Select(idx =>
                    {
                        var provider = providers[idx];
                        var source = (sources is not null && idx < sources.Count) ? sources[idx] : null;
                        return (Index: idx, Origin: DescribeOrigin(provider, source));
                    })
                    .ToList();

                var sb = new StringBuilder(capacity: 256);
                var args = new List<object?>(capacity: 3 + (chainItems.Count * 2));

                sb.Append("Config key collision on {Key}; chain (winner first): ");
                args.Add(key); // {Key}

                AppendIndexedChainTemplate(sb, args, chainItems);

                sb.Append("; winner is [{WinnerIndex}]");
                args.Add(winnerIndex); // {WinnerIndex}

                logger.Log(level, sb.ToString(), args.ToArray());
            }
        }

        /// <summary>
        /// Appends a chain like "[{Idx0}] {Origin0}  ->  [{Idx1}] {Origin1} ..." and pushes matching args in order.
        /// </summary>
        private static void AppendIndexedChainTemplate(
            StringBuilder sb,
            List<object?> args,
            IReadOnlyList<(int Index, string Origin)> items)
        {
            ArgumentNullException.ThrowIfNull(sb);
            ArgumentNullException.ThrowIfNull(args);
            ArgumentNullException.ThrowIfNull(items);

            for (var i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append("  ->  ");
                }

                // Placeholder names must be unique per position; the VALUE is the real provider index.
                sb.Append("[{Idx").Append(i).Append("}] {Origin").Append(i).Append("}");

                args.Add(items[i].Index);
                args.Add(items[i].Origin);
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
                return string.IsNullOrWhiteSpace(path) ? "json" : $"json:{path}";
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