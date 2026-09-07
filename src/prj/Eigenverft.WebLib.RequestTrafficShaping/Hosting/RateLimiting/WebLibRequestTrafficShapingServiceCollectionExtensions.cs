using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Eigenverft.WebLib.Infrastructure.Hosting.RateLimiting
{
    /// <summary>Provides service-registration helpers for WebLib request traffic shaping.</summary>
    public static class WebLibRequestTrafficShapingServiceCollectionExtensions
    {
        private const string ConfigurationSectionName = "RequestTrafficShaping";

        /// <summary>
        /// Adds request traffic shaping using a per-client token bucket, a server-wide token bucket, and an optional
        /// global concurrency limiter.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <returns>The original service collection.</returns>
        /// <remarks>
        /// Options are bound from the <c>RequestTrafficShaping</c> configuration section over class defaults and validated
        /// on startup. The consuming application activates ASP.NET Core's native middleware with <c>app.UseRateLimiter()</c>.
        /// Trusted forwarded-header processing must run before rate limiting when a reverse proxy supplies the real client IP.
        /// </remarks>
        public static IServiceCollection AddRequestTrafficShaping(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services
                .AddOptions<WebLibRequestTrafficShapingOptions>()
                .BindConfiguration(ConfigurationSectionName)
                .Validate(static options => options.PerClient is not null, "PerClient cannot be null.")
                .Validate(static options => options.PerClient is null || options.PerClient.BurstSize > 0,
                    "PerClient.BurstSize must be greater than zero.")
                .Validate(static options => options.PerClient is null || options.PerClient.RequestsPerSecond > 0,
                    "PerClient.RequestsPerSecond must be greater than zero.")
                .Validate(static options => options.PerClient is null || options.PerClient.QueueLimit >= 0,
                    "PerClient.QueueLimit cannot be negative.")
                .Validate(static options => options.PerClient is null ||
                    Enum.IsDefined(typeof(MissingClientIpBehavior), options.PerClient.MissingClientIpBehavior),
                    "PerClient.MissingClientIpBehavior is invalid.")
                .Validate(static options => options.PerClient is null ||
                    options.PerClient.BurstSize >= options.PerClient.RequestsPerSecond,
                    "PerClient.BurstSize must be greater than or equal to PerClient.RequestsPerSecond.")
                .Validate(static options => options.ServerWide is not null, "ServerWide cannot be null.")
                .Validate(static options => options.ServerWide is null || options.ServerWide.BurstSize > 0,
                    "ServerWide.BurstSize must be greater than zero.")
                .Validate(static options => options.ServerWide is null || options.ServerWide.RequestsPerSecond > 0,
                    "ServerWide.RequestsPerSecond must be greater than zero.")
                .Validate(static options => options.ServerWide is null || options.ServerWide.QueueLimit >= 0,
                    "ServerWide.QueueLimit cannot be negative.")
                .Validate(static options => options.ServerWide is null ||
                    options.ServerWide.BurstSize >= options.ServerWide.RequestsPerSecond,
                    "ServerWide.BurstSize must be greater than or equal to ServerWide.RequestsPerSecond.")
                .Validate(static options => options.GlobalConcurrencyLimit is null || options.GlobalConcurrencyLimit > 0,
                    "GlobalConcurrencyLimit must be null or greater than zero.")
                .ValidateOnStart();

            services.AddRateLimiter(static _ => { });
            services.TryAddEnumerable(
                ServiceDescriptor.Transient<IConfigureOptions<RateLimiterOptions>, WebLibRequestTrafficShapingRateLimiterOptionsSetup>());

            return services;
        }

        /// <summary>
        /// Adds request traffic shaping and applies code-based configuration after the
        /// <c>RequestTrafficShaping</c> configuration section.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="configure">Startup-time configuration applied after configuration binding.</param>
        /// <returns>The original service collection.</returns>
        public static IServiceCollection AddRequestTrafficShaping(
            this IServiceCollection services,
            Action<WebLibRequestTrafficShapingOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            services.AddRequestTrafficShaping();
            services.Configure(configure);
            return services;
        }
    }

    internal sealed class WebLibRequestTrafficShapingRateLimiterOptionsSetup : IConfigureOptions<RateLimiterOptions>
    {
        private const string RetryAfterHeaderName = "Retry-After";

        private readonly IOptions<WebLibRequestTrafficShapingOptions> _options;

        public WebLibRequestTrafficShapingRateLimiterOptionsSetup(IOptions<WebLibRequestTrafficShapingOptions> options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public void Configure(RateLimiterOptions rateLimiterOptions)
        {
            ArgumentNullException.ThrowIfNull(rateLimiterOptions);

            WebLibRequestTrafficShapingOptions options = _options.Value;
            PartitionedRateLimiter<HttpContext>? shapingLimiter = CreateRequestTrafficShapingLimiter(options);

            rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            if (shapingLimiter is not null)
            {
                rateLimiterOptions.GlobalLimiter = rateLimiterOptions.GlobalLimiter is null
                    ? shapingLimiter
                    : PartitionedRateLimiter.CreateChained(rateLimiterOptions.GlobalLimiter, shapingLimiter);
            }

            Func<OnRejectedContext, CancellationToken, ValueTask>? previousOnRejected = rateLimiterOptions.OnRejected;
            rateLimiterOptions.OnRejected = (context, cancellationToken) =>
                OnRejectedAsync(context, cancellationToken, previousOnRejected);
        }

        private static PartitionedRateLimiter<HttpContext>? CreateRequestTrafficShapingLimiter(
            WebLibRequestTrafficShapingOptions options)
        {
            var limiters = new List<PartitionedRateLimiter<HttpContext>>(capacity: 3);

            if (options.PerClient.Enabled)
            {
                limiters.Add(CreatePerClientLimiter(options.PerClient));
            }

            if (options.ServerWide.Enabled)
            {
                limiters.Add(CreateServerWideTokenBucketLimiter(options.ServerWide));
            }

            if (options.GlobalConcurrencyLimit is int globalConcurrencyLimit)
            {
                limiters.Add(CreateGlobalConcurrencyLimiter(globalConcurrencyLimit));
            }

            return limiters.Count switch
            {
                0 => null,
                1 => limiters[0],
                _ => PartitionedRateLimiter.CreateChained(limiters.ToArray()),
            };
        }

        private static PartitionedRateLimiter<HttpContext> CreatePerClientLimiter(
            WebLibRequestTrafficShapingPerClientOptions options)
        {
            return PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                string? clientKey = ClientIpPartitionKey.Resolve(context);
                if (clientKey is null && options.MissingClientIpBehavior == MissingClientIpBehavior.BypassPerIpLimit)
                {
                    return RateLimitPartition.GetNoLimiter(ClientIpPartitionKey.MissingBypassPartition);
                }

                string partitionKey = clientKey ?? ClientIpPartitionKey.MissingSharedPartition;
                return RateLimitPartition.GetTokenBucketLimiter(
                    partitionKey,
                    _ => CreateTokenBucketOptions(options.BurstSize, options.RequestsPerSecond, options.QueueLimit));
            }, StringComparer.Ordinal);
        }

        private static PartitionedRateLimiter<HttpContext> CreateServerWideTokenBucketLimiter(
            WebLibRequestTrafficShapingServerWideOptions options)
        {
            return PartitionedRateLimiter.Create<HttpContext, string>(
                _ => RateLimitPartition.GetTokenBucketLimiter(
                    ClientIpPartitionKey.ServerWideTokenBucketPartition,
                    _ => CreateTokenBucketOptions(options.BurstSize, options.RequestsPerSecond, options.QueueLimit)),
                StringComparer.Ordinal);
        }

        internal static TokenBucketRateLimiterOptions CreateTokenBucketOptions(
            int burstSize,
            int requestsPerSecond,
            int queueLimit)
        {
            return new TokenBucketRateLimiterOptions
            {
                TokenLimit = burstSize,
                TokensPerPeriod = requestsPerSecond,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                AutoReplenishment = true,
                QueueLimit = queueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            };
        }

        private static PartitionedRateLimiter<HttpContext> CreateGlobalConcurrencyLimiter(int permitLimit)
        {
            return PartitionedRateLimiter.Create<HttpContext, string>(
                _ => RateLimitPartition.GetConcurrencyLimiter(
                    ClientIpPartitionKey.GlobalConcurrencyPartition,
                    _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    }),
                StringComparer.Ordinal);
        }

        private static async ValueTask OnRejectedAsync(
            OnRejectedContext context,
            CancellationToken cancellationToken,
            Func<OnRejectedContext, CancellationToken, ValueTask>? previousOnRejected)
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter) &&
                retryAfter > TimeSpan.Zero &&
                !context.HttpContext.Response.HasStarted &&
                !context.HttpContext.Response.Headers.ContainsKey(RetryAfterHeaderName))
            {
                string retryAfterSeconds = Math.Max(1D, Math.Ceiling(retryAfter.TotalSeconds))
                    .ToString("0", CultureInfo.InvariantCulture);
                context.HttpContext.Response.Headers[RetryAfterHeaderName] = retryAfterSeconds;
            }

            if (previousOnRejected is not null)
            {
                await previousOnRejected(context, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
