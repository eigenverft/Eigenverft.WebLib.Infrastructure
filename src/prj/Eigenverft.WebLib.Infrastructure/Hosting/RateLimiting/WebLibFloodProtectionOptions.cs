using System;

namespace Eigenverft.WebLib.Infrastructure.Hosting.RateLimiting
{
    /// <summary>
    /// Configures the WebLib convenience layer over ASP.NET Core rate limiting for generic request flood protection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The per-client defaults are intentionally only a practical starting point. They are not universal security boundaries and
    /// should be load-tested and tuned for the consuming application and its expected traffic profile.
    /// </para>
    /// <para>
    /// The primary limiter is a token bucket partitioned by the normalized client IP address. An optional server-wide token bucket
    /// can smooth the aggregate request rate for the server instance, and the existing optional global concurrency limiter remains
    /// available as a separate guard for simultaneously active work.
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

        /// <summary>Gets or sets the optional server-wide request-rate token-bucket settings.</summary>
        /// <remarks>
        /// This limiter is disabled by default for compatibility. When enabled, <see cref="WebLibFloodProtectionServerWideOptions.BurstSize"/>
        /// and <see cref="WebLibFloodProtectionServerWideOptions.RequestsPerSecond"/> must be configured explicitly.
        /// </remarks>
        public WebLibFloodProtectionServerWideOptions ServerWide { get; set; } = new();

        /// <summary>Gets or sets the optional whole-application concurrency limit.</summary>
        /// <remarks>
        /// <see langword="null"/> disables the global concurrency limiter, which is the default. This limiter controls simultaneously
        /// active work and remains semantically independent from the per-IP and server-wide token buckets.
        /// </remarks>
        public int? GlobalConcurrencyLimit { get; set; }
    }

    /// <summary>Configures the optional server-wide request-rate token bucket.</summary>
    /// <remarks>
    /// <para>
    /// The server-wide limiter is intentionally opt-in. Its numeric defaults are zero so enabling it without explicit burst and
    /// sustained-rate values fails options validation instead of silently imposing an arbitrary production limit.
    /// </para>
    /// <para>
    /// The bounded queue is processed by the native limiter in <c>OldestFirst</c> order. This is FIFO arrival fairness, not
    /// per-client fairness; one client can therefore occupy multiple positions in the shared queue.
    /// </para>
    /// </remarks>
    public sealed class WebLibFloodProtectionServerWideOptions
    {
        /// <summary>Gets or sets whether the server-wide token bucket participates in the limiter chain.</summary>
        public bool Enabled { get; set; }

        /// <summary>Gets or sets the maximum aggregate burst capacity for the server instance.</summary>
        /// <remarks>Must be greater than zero when <see cref="Enabled"/> is <see langword="true"/>.</remarks>
        public int BurstSize { get; set; }

        /// <summary>Gets or sets the aggregate sustained replenishment rate, in requests per second.</summary>
        /// <remarks>Must be greater than zero when <see cref="Enabled"/> is <see langword="true"/>.</remarks>
        public int RequestsPerSecond { get; set; }

        /// <summary>Gets or sets the maximum cumulative request count queued by the shared server-wide limiter.</summary>
        /// <remarks>
        /// The default is zero. Larger values may be used to smooth short aggregate spikes; queued requests are processed
        /// oldest-first by the native token bucket and the queue does not provide per-client fairness.
        /// </remarks>
        public int QueueLimit { get; set; }
    }

    /// <summary>Defines how the per-IP limiter behaves when no client IP is available on the connection.</summary>
    public enum MissingClientIpBehavior
    {
        /// <summary>Places all requests without a client IP into one shared token-bucket partition.</summary>
        SharedPartition = 0,

        /// <summary>
        /// Bypasses only the per-IP token bucket for requests without a client IP. The optional server-wide token bucket and
        /// global concurrency limiter still apply.
        /// </summary>
        BypassPerIpLimit = 1,
    }
}
