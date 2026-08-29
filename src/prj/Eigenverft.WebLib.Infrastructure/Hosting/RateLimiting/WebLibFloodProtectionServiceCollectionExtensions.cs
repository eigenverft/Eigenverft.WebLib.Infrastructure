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
    /// <summary>Provides service-registration helpers for WebLib flood protection.</summary>
    public static class WebLibFloodProtectionServiceCollectionExtensions
    {
        private const string ConfigurationSectionName = "FloodProtection";

        /// <summary>
        /// Adds framework-based flood protection using a per-client-IP token bucket, an optional server-wide token bucket,
        /// and an optional global concurrency limiter.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <returns>The original service collection.</returns>
        /// <remarks>
        /// Options are bound from the <c>FloodProtection</c> configuration section over the class defaults and validated on startup.
        /// The consuming application still activates the native middleware with <c>app.UseRateLimiter()</c>. Client IPs are read from
        /// <c>HttpContext.Connection.RemoteIpAddress</c>, so trusted forwarded-header processing must run before rate limiting when a
        /// reverse proxy supplies the real client IP.
        /// </remarks>
        public static IServiceCollection AddFloodProtection(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services
                .AddOptions<WebLibFloodProtectionOptions>()
                .BindConfiguration(ConfigurationSectionName)
                .Validate(static options => options.BurstSize > 0, "BurstSize must be greater than zero.")
                .Validate(static options => options.RequestsPerSecond > 0, "RequestsPerSecond must be greater than zero.")
                .Validate(static options => options.QueueLimit >= 0, "QueueLimit cannot be negative.")
                .Validate(static options => Enum.IsDefined(typeof(MissingClientIpBehavior), options.MissingClientIpBehavior), "MissingClientIpBehavior is invalid.")
                .Validate(static options => options.ServerWide is not null, "ServerWide cannot be null.")
                .Validate(static options => options.ServerWide is null || options.ServerWide.BurstSize >= 0, "ServerWide.BurstSize cannot be negative.")
                .Validate(static options => options.ServerWide is null || options.ServerWide.RequestsPerSecond >= 0, "ServerWide.RequestsPerSecond cannot be negative.")
                .Validate(static options => options.ServerWide is null || options.ServerWide.QueueLimit >= 0, "ServerWide.QueueLimit cannot be negative.")
                .Validate(static options => options.ServerWide is null || !options.ServerWide.Enabled || options.ServerWide.BurstSize > 0, "ServerWide.BurstSize must be greater than zero when ServerWide is enabled.")
                .Validate(static options => options.ServerWide is null || !options.ServerWide.Enabled || options.ServerWide.RequestsPerSecond > 0, "ServerWide.RequestsPerSecond must be greater than zero when ServerWide is enabled.")
                .Validate(static options => options.GlobalConcurrencyLimit is null || options.GlobalConcurrencyLimit > 0, "GlobalConcurrencyLimit must be null or greater than zero.")
                .ValidateOnStart();

            services.AddRateLimiter(static _ => { });
            services.TryAddEnumerable(
                ServiceDescriptor.Transient<IConfigureOptions<RateLimiterOptions>, WebLibFloodProtectionRateLimiterOptionsSetup>());

            return services;
        }

        /// <summary>
        /// Adds flood protection and applies code-based configuration after the <c>FloodProtection</c> configuration section.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="configure">Startup-time configuration applied after configuration binding.</param>
        /// <returns>The original service collection.</returns>
        public static IServiceCollection AddFloodProtection(
            this IServiceCollection services,
            Action<WebLibFloodProtectionOptions>? configure)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddFloodProtection();
            if (configure is not null)
            {
                services.Configure(configure);
            }

            return services;
        }
    }

    internal sealed class WebLibFloodProtectionRateLimiterOptionsSetup : IConfigureOptions<RateLimiterOptions>
    {
        private const string RetryAfterHeaderName = "Retry-After";

        private readonly IOptions<WebLibFloodProtectionOptions> _options;

        public WebLibFloodProtectionRateLimiterOptionsSetup(IOptions<WebLibFloodProtectionOptions> options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public void Configure(RateLimiterOptions rateLimiterOptions)
        {
            ArgumentNullException.ThrowIfNull(rateLimiterOptions);

            WebLibFloodProtectionOptions options = _options.Value;
            PartitionedRateLimiter<HttpContext> floodLimiter = CreateFloodLimiter(options);

            rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rateLimiterOptions.GlobalLimiter = rateLimiterOptions.GlobalLimiter is null
                ? floodLimiter
                : PartitionedRateLimiter.CreateChained(rateLimiterOptions.GlobalLimiter, floodLimiter);

            Func<OnRejectedContext, CancellationToken, ValueTask>? previousOnRejected = rateLimiterOptions.OnRejected;
            rateLimiterOptions.OnRejected = (context, cancellationToken) =>
                OnRejectedAsync(context, cancellationToken, previousOnRejected);
        }

        private static PartitionedRateLimiter<HttpContext> CreateFloodLimiter(WebLibFloodProtectionOptions options)
        {
            var limiters = new List<PartitionedRateLimiter<HttpContext>>(capacity: 3)
            {
                CreatePerIpLimiter(options),
            };

            if (options.ServerWide.Enabled)
            {
                limiters.Add(CreateServerWideTokenBucketLimiter(options.ServerWide));
            }

            if (options.GlobalConcurrencyLimit is int globalConcurrencyLimit)
            {
                limiters.Add(CreateGlobalConcurrencyLimiter(globalConcurrencyLimit));
            }

            return limiters.Count == 1
                ? limiters[0]
                : PartitionedRateLimiter.CreateChained(limiters.ToArray());
        }

        private static PartitionedRateLimiter<HttpContext> CreatePerIpLimiter(WebLibFloodProtectionOptions options)
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
            WebLibFloodProtectionServerWideOptions options)
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
