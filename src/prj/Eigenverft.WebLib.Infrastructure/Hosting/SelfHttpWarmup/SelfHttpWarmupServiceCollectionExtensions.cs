using System;
using System.Net;
using System.Net.Http;
using System.Threading;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
        /// to <see langword="true"/> and configure at least one target URL to opt in.
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
                .ConfigurePrimaryHttpMessageHandler(static serviceProvider =>
                {
                    SelfHttpWarmupOptions options = serviceProvider
                        .GetRequiredService<IOptions<SelfHttpWarmupOptions>>()
                        .Value;

                    TimeSpan connectTimeout = options.ConnectTimeout > TimeSpan.Zero
                        ? options.ConnectTimeout
                        : TimeSpan.FromSeconds(1);

                    var connector = new SelfHttpWarmupConnector(connectTimeout);

                    return new SocketsHttpHandler
                    {
                        AllowAutoRedirect = false,
                        ConnectTimeout = Timeout.InfiniteTimeSpan,
                        ConnectCallback = connector.ConnectAsync,
                    };
                });

            services.AddHostedService<SelfHttpWarmupHostedService>();
            return services;
        }

        /// <summary>
        /// Registers self-HTTP warmup, binds configuration, and applies code-based options afterwards.
        /// </summary>
        public static IServiceCollection AddSelfHttpWarmup(
            this IServiceCollection services,
            Action<SelfHttpWarmupOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            services.AddSelfHttpWarmup();
            services.Configure(configure);
            return services;
        }
    }
}
