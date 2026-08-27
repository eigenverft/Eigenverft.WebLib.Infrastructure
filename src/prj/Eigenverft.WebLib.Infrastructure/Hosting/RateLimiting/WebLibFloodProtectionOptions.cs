using System;

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
        /// <summary>Gets or sets the maximum burst capacity for one client-IP partition.</summary>
        /// <remarks>The default of 40 permits short bursts above the sustained request rate.</remarks>
        public int BurstSize { get; set; } = 40;

        /// <summary>Gets or sets the sustained token replenishment rate per client-IP partition, in requests per second.</summary>
        public int RequestsPerSecond { get; set; } = 10;

        /// <summary>
        /// Gets or sets the maximum queued request count per client-IP partition after its immediately available tokens are exhausted.
        /// </summary>
        /// <remarks>
        /// Queued requests are processed oldest-first. When the queue is full, the framework rejects the request instead of
        /// adding unbounded waiting work.
        /// </remarks>
        public int QueueLimit { get; set; } = 20;

        /// <summary>Gets or sets how requests without a resolved client IP are partitioned.</summary>
        public MissingClientIpBehavior MissingClientIpBehavior { get; set; } = MissingClientIpBehavior.SharedPartition;

        /// <summary>Gets or sets the optional whole-application concurrency limit.</summary>
        /// <remarks>
        /// <see langword="null"/> disables the global concurrency limiter, which is the default. The global limiter does
        /// not add another queue; the bounded per-IP queue remains the only request queue configured by this convenience layer.
        /// </remarks>
        public int? GlobalConcurrencyLimit { get; set; }
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
