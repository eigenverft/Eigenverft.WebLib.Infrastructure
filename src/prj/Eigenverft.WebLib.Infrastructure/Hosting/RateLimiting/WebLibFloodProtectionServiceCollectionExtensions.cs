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
using Microsoft.Extensions.Logging;
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
        public static IServiceCollection AddWebLibFloodProtection(
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
                .Validate(static options => options.TokenLimit > 0, "TokenLimit must be greater than zero.")
                .Validate(static options => options.TokensPerPeriod > 0, "TokensPerPeriod must be greater than zero.")
                .Validate(static options => options.ReplenishmentPeriod > TimeSpan.Zero, "ReplenishmentPeriod must be greater than zero.")
                .Validate(static options => options.QueueLimit >= 0, "QueueLimit cannot be negative.")
                .Validate(static options => Enum.IsDefined(typeof(QueueProcessingOrder), options.QueueProcessingOrder), "QueueProcessingOrder is invalid.")
                .Validate(static options => Enum.IsDefined(typeof(MissingClientIpBehavior), options.MissingClientIpBehavior), "MissingClientIpBehavior is invalid.")
                .Validate(static options => options.RejectionStatusCode >= 100 && options.RejectionStatusCode <= 599, "RejectionStatusCode must be a valid HTTP status code.")
                .Validate(static options => options.GlobalConcurrencyPermitLimit is null || options.GlobalConcurrencyPermitLimit > 0, "GlobalConcurrencyPermitLimit must be null or greater than zero.")
                .Validate(static options => options.GlobalConcurrencyQueueLimit >= 0, "GlobalConcurrencyQueueLimit cannot be negative.")
                .Validate(static options => Enum.IsDefined(typeof(QueueProcessingOrder), options.GlobalConcurrencyQueueProcessingOrder), "GlobalConcurrencyQueueProcessingOrder is invalid.")
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
        private const string LoggerCategory = "Eigenverft.WebLib.Infrastructure.Hosting.RateLimiting.FloodProtection";

        private readonly IOptions<WebLibFloodProtectionOptions> _options;

        public WebLibFloodProtectionRateLimiterOptionsSetup(IOptions<WebLibFloodProtectionOptions> options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public void Configure(RateLimiterOptions rateLimiterOptions)
        {
            ArgumentNullException.ThrowIfNull(rateLimiterOptions);

            WebLibFloodProtectionOptions options = _options.Value;
            PartitionedRateLimiter<HttpContext> perIpLimiter = CreatePerIpLimiter(options);

            rateLimiterOptions.RejectionStatusCode = options.RejectionStatusCode;
            rateLimiterOptions.GlobalLimiter = options.GlobalConcurrencyPermitLimit is int globalPermitLimit
                ? PartitionedRateLimiter.CreateChained(
                    perIpLimiter,
                    CreateGlobalConcurrencyLimiter(options, globalPermitLimit))
                : perIpLimiter;

            rateLimiterOptions.OnRejected = (context, cancellationToken) =>
                OnRejectedAsync(context, cancellationToken, options);
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
                        TokenLimit = options.TokenLimit,
                        TokensPerPeriod = options.TokensPerPeriod,
                        ReplenishmentPeriod = options.ReplenishmentPeriod,
                        AutoReplenishment = options.AutoReplenishment,
                        QueueLimit = options.QueueLimit,
                        QueueProcessingOrder = options.QueueProcessingOrder,
                    });
            }, StringComparer.Ordinal);
        }

        private static PartitionedRateLimiter<HttpContext> CreateGlobalConcurrencyLimiter(
            WebLibFloodProtectionOptions options,
            int permitLimit)
        {
            return PartitionedRateLimiter.Create<HttpContext, string>(
                _ => RateLimitPartition.GetConcurrencyLimiter(
                    ClientIpPartitionKey.GlobalConcurrencyPartition,
                    _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        QueueLimit = options.GlobalConcurrencyQueueLimit,
                        QueueProcessingOrder = options.GlobalConcurrencyQueueProcessingOrder,
                    }),
                StringComparer.Ordinal);
        }

        private static ValueTask OnRejectedAsync(
            OnRejectedContext context,
            CancellationToken cancellationToken,
            WebLibFloodProtectionOptions options)
        {
            _ = cancellationToken;

            string? retryAfterSeconds = null;
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter) && retryAfter > TimeSpan.Zero)
            {
                retryAfterSeconds = Math.Max(1D, Math.Ceiling(retryAfter.TotalSeconds))
                    .ToString("0", CultureInfo.InvariantCulture);

                if (options.EmitRetryAfterHeader &&
                    !context.HttpContext.Response.HasStarted &&
                    !context.HttpContext.Response.Headers.ContainsKey(RetryAfterHeaderName))
                {
                    context.HttpContext.Response.Headers[RetryAfterHeaderName] = retryAfterSeconds;
                }
            }

            if (options.LogRejectedRequests)
            {
                ILoggerFactory? loggerFactory = context.HttpContext.RequestServices.GetService<ILoggerFactory>();
                ILogger? logger = loggerFactory?.CreateLogger(LoggerCategory);
                logger?.LogWarning(
                    "Request rejected by WebLib flood protection. ClientPartition={ClientPartition} StatusCode={StatusCode} RetryAfterSeconds={RetryAfterSeconds}",
                    ClientIpPartitionKey.Resolve(context.HttpContext) ??
                        (options.MissingClientIpBehavior == MissingClientIpBehavior.BypassPerIpLimit
                            ? ClientIpPartitionKey.MissingBypassPartition
                            : ClientIpPartitionKey.MissingSharedPartition),
                    options.RejectionStatusCode,
                    retryAfterSeconds);
            }

            return ValueTask.CompletedTask;
        }
    }
}
