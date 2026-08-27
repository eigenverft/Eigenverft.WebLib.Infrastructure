using System;
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
        /// <summary>
        /// Adds framework-based flood protection using a per-client-IP token bucket and an optional global concurrency limiter.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="configure">Optional startup-time configuration for the WebLib convenience options.</param>
        /// <returns>The original service collection.</returns>
        /// <remarks>
        /// This method registers ASP.NET Core rate-limiting services. The consuming application still activates the native
        /// middleware with <c>app.UseRateLimiter()</c>. Client IPs are read from <c>HttpContext.Connection.RemoteIpAddress</c>,
        /// so trusted forwarded-header processing must run before rate limiting when a reverse proxy supplies the real client IP.
        /// </remarks>
        public static IServiceCollection AddFloodProtection(
            this IServiceCollection services,
            Action<WebLibFloodProtectionOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            OptionsBuilder<WebLibFloodProtectionOptions> optionsBuilder = services.AddOptions<WebLibFloodProtectionOptions>();
            if (configure is not null)
            {
                optionsBuilder.Configure(configure);
            }

            optionsBuilder
                .Validate(static options => options.BurstSize > 0, "BurstSize must be greater than zero.")
                .Validate(static options => options.RequestsPerSecond > 0, "RequestsPerSecond must be greater than zero.")
                .Validate(static options => options.QueueLimit >= 0, "QueueLimit cannot be negative.")
                .Validate(static options => Enum.IsDefined(typeof(MissingClientIpBehavior), options.MissingClientIpBehavior), "MissingClientIpBehavior is invalid.")
                .Validate(static options => options.GlobalConcurrencyLimit is null || options.GlobalConcurrencyLimit > 0, "GlobalConcurrencyLimit must be null or greater than zero.")
                .ValidateOnStart();

            services.AddRateLimiter(static _ => { });
            services.TryAddEnumerable(
                ServiceDescriptor.Transient<IConfigureOptions<RateLimiterOptions>, WebLibFloodProtectionRateLimiterOptionsSetup>());

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
            PartitionedRateLimiter<HttpContext> perIpLimiter = CreatePerIpLimiter(options);
            return options.GlobalConcurrencyLimit is int globalConcurrencyLimit
                ? PartitionedRateLimiter.CreateChained(
                    perIpLimiter,
                    CreateGlobalConcurrencyLimiter(globalConcurrencyLimit))
                : perIpLimiter;
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
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = options.BurstSize,
                        TokensPerPeriod = options.RequestsPerSecond,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                        AutoReplenishment = true,
                        QueueLimit = options.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    });
            }, StringComparer.Ordinal);
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
