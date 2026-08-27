using System;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Http;

namespace Eigenverft.WebLib.Infrastructure.Hosting.RateLimiting
{
    /// <summary>
    /// Configures the WebLib convenience layer over ASP.NET Core rate limiting for generic request flood protection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The defaults are intentionally only a practical starting point. They are not universal security boundaries and
    /// should be load-tested and tuned for the consuming application and its expected traffic profile.
    /// </para>
    /// <para>
    /// The primary limiter is a token bucket partitioned by the normalized client IP address. An optional global
    /// concurrency limiter can be enabled as an additional whole-application guard.
    /// </para>
    /// </remarks>
    public sealed class WebLibFloodProtectionOptions
    {
        /// <summary>
        /// Gets or sets the maximum number of tokens that may accumulate for one client-IP partition.
        /// </summary>
        /// <remarks>The default of 40 permits short bursts above the sustained replenishment rate.</remarks>
        public int TokenLimit { get; set; } = 40;

        /// <summary>
        /// Gets or sets how many tokens are added to each client-IP partition per replenishment period.
        /// </summary>
        /// <remarks>With the default one-second period, the default sustained rate is approximately 10 requests/second.</remarks>
        public int TokensPerPeriod { get; set; } = 10;

        /// <summary>Gets or sets the token replenishment period.</summary>
        public TimeSpan ReplenishmentPeriod { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Gets or sets whether token buckets replenish automatically.
        /// </summary>
        /// <remarks>
        /// Leave enabled for normal use. Disabling automatic replenishment makes each partition a finite token budget
        /// because this convenience layer does not expose a manual replenishment handle.
        /// </remarks>
        public bool AutoReplenishment { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum queued permit count per client-IP partition after its immediately available tokens are exhausted.
        /// </summary>
        /// <remarks>When the queue is full, the framework rejects the request instead of adding unbounded waiting work.</remarks>
        public int QueueLimit { get; set; } = 20;

        /// <summary>Gets or sets how queued requests are ordered for each client-IP token bucket.</summary>
        public QueueProcessingOrder QueueProcessingOrder { get; set; } = QueueProcessingOrder.OldestFirst;

        /// <summary>Gets or sets how requests without a resolved client IP are partitioned.</summary>
        public MissingClientIpBehavior MissingClientIpBehavior { get; set; } = MissingClientIpBehavior.SharedPartition;

        /// <summary>Gets or sets the HTTP status code returned when the ASP.NET Core rate-limiting middleware rejects a request.</summary>
        public int RejectionStatusCode { get; set; } = StatusCodes.Status429TooManyRequests;

        /// <summary>
        /// Gets or sets whether WebLib writes a <c>Retry-After</c> header when the rejected framework lease provides retry metadata.
        /// </summary>
        public bool EmitRetryAfterHeader { get; set; } = true;

        /// <summary>Gets or sets whether rejected requests are logged at warning level.</summary>
        public bool LogRejectedRequests { get; set; } = true;

        /// <summary>
        /// Gets or sets the optional whole-application concurrency permit limit.
        /// </summary>
        /// <remarks><see langword="null"/> disables the global concurrency limiter, which is the default.</remarks>
        public int? GlobalConcurrencyPermitLimit { get; set; }

        /// <summary>Gets or sets the queue limit for the optional global concurrency limiter.</summary>
        public int GlobalConcurrencyQueueLimit { get; set; } = 0;

        /// <summary>Gets or sets how queued requests are ordered by the optional global concurrency limiter.</summary>
        public QueueProcessingOrder GlobalConcurrencyQueueProcessingOrder { get; set; } = QueueProcessingOrder.OldestFirst;
    }

    /// <summary>Defines how the per-IP limiter behaves when no client IP is available on the connection.</summary>
    public enum MissingClientIpBehavior
    {
        /// <summary>Places all requests without a client IP into one shared token-bucket partition.</summary>
        SharedPartition = 0,

        /// <summary>
        /// Bypasses only the per-IP token bucket for requests without a client IP. The optional global concurrency limiter still applies.
        /// </summary>
        BypassPerIpLimit = 1,
    }
}
