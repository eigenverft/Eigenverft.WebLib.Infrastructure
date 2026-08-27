using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;

using Microsoft.Extensions.DependencyInjection;

namespace Eigenverft.WebLib.Infrastructure.Hosting.SelfHttpWarmup
{
    /// <summary>
    /// Registers optional self-HTTP startup warmup for ASP.NET Core applications.
    /// </summary>
    public static class SelfHttpWarmupServiceCollectionExtensions
    {
        internal const string HttpClientName = "Eigenverft.WebLib.Infrastructure.SelfHttpWarmup";

        /// <summary>
        /// Registers self-HTTP warmup and binds <see cref="SelfHttpWarmupOptions"/> from the
        /// <c>SelfHttpWarmup</c> configuration section.
        /// </summary>
        /// <remarks>
        /// Registration alone does not enable warmup. Set <see cref="SelfHttpWarmupOptions.Enabled"/>
        /// to <see langword="true"/> and configure at least one target URL to opt in through configuration.
        /// </remarks>
        public static IServiceCollection AddSelfHttpWarmup(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services
                .AddOptions<SelfHttpWarmupOptions>()
                .BindConfiguration(SelfHttpWarmupOptions.SectionName);

            services
                .AddHttpClient(HttpClientName, static client =>
                {
                    // Per-request linked cancellation controls the overall timeout. Keeping HttpClient's
                    // own timeout infinite avoids racing two independent timeout mechanisms.
                    client.Timeout = Timeout.InfiniteTimeSpan;
                    client.DefaultRequestVersion = HttpVersion.Version20;
                    client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
                })
                .ConfigurePrimaryHttpMessageHandler(static _ =>
                {
                    var connector = new SelfHttpWarmupConnector(SelfHttpWarmupConnector.DefaultConnectTimeout);

                    return new SocketsHttpHandler
                    {
                        AllowAutoRedirect = false,
                        ConnectTimeout = Timeout.InfiniteTimeSpan,
                        ConnectCallback = connector.ConnectAsync,
                        UseProxy = false,
                    };
                });

            services.AddHostedService<SelfHttpWarmupHostedService>();
            return services;
        }

        /// <summary>
        /// Enables self-HTTP warmup for one absolute HTTP or HTTPS URL.
        /// </summary>
        public static IServiceCollection AddSelfHttpWarmup(
            this IServiceCollection services,
            string targetUrl,
            Action<SelfHttpWarmupOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(targetUrl);

            return services.AddSelfHttpWarmup(
                new[] { targetUrl },
                configure);
        }

        /// <summary>
        /// Enables self-HTTP warmup for one or more absolute HTTP or HTTPS URLs.
        /// </summary>
        public static IServiceCollection AddSelfHttpWarmup(
            this IServiceCollection services,
            IEnumerable<string> targetUrls,
            Action<SelfHttpWarmupOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(targetUrls);

            string[] targets = targetUrls
                .Select(static target => target?.Trim() ?? string.Empty)
                .ToArray();

            if (targets.Length == 0)
            {
                throw new ArgumentException("At least one self-HTTP warmup target URL is required.", nameof(targetUrls));
            }

            for (int index = 0; index < targets.Length; index++)
            {
                if (!IsAbsoluteHttpTarget(targets[index]))
                {
                    throw new ArgumentException(
                        $"Self-HTTP warmup target at index {index} must be an absolute HTTP or HTTPS URL.",
                        nameof(targetUrls));
                }
            }

            services.AddSelfHttpWarmup();
            services.Configure<SelfHttpWarmupOptions>(options =>
            {
                options.Enabled = true;
                options.TargetUrls = targets;
                configure?.Invoke(options);
            });

            return services;
        }

        /// <summary>
        /// Enables self-HTTP warmup and applies code-based options after configuration binding.
        /// </summary>
        public static IServiceCollection AddSelfHttpWarmup(
            this IServiceCollection services,
            Action<SelfHttpWarmupOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            services.AddSelfHttpWarmup();
            services.Configure<SelfHttpWarmupOptions>(options =>
            {
                options.Enabled = true;
                configure(options);
            });

            return services;
        }

        private static bool IsAbsoluteHttpTarget(string target)
        {
            return Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) &&
                   (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
        }
    }
}
