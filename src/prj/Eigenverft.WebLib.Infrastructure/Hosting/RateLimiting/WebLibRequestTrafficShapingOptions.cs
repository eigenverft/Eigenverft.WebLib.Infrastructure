using System;

namespace Eigenverft.WebLib.Infrastructure.Hosting.RateLimiting
{
    /// <summary>
    /// Configures WebLib request traffic shaping on top of ASP.NET Core's native rate-limiters.
    /// </summary>
    /// <remarks>
    /// The per-client token bucket is enabled by default to preserve the existing request-shaping baseline. The server-wide
    /// token bucket is opt-in so no new aggregate production rate is imposed implicitly. The optional global concurrency
    /// limit remains an independent third dimension for simultaneously active work.
    /// </remarks>
    public sealed class WebLibRequestTrafficShapingOptions
    {
        /// <summary>Gets or sets the per-client token-bucket settings.</summary>
        public WebLibRequestTrafficShapingPerClientOptions PerClient { get; set; } = new();

        /// <summary>Gets or sets the optional server-wide token-bucket settings.</summary>
        public WebLibRequestTrafficShapingServerWideOptions ServerWide { get; set; } = new();

        /// <summary>Gets or sets the optional whole-application concurrency limit.</summary>
        /// <remarks>
        /// <see langword="null"/> disables the concurrency limiter, which is the default. This limiter is orthogonal to
        /// both request-rate token buckets and therefore remains effective even when either token-bucket layer is disabled.
        /// </remarks>
        public int? GlobalConcurrencyLimit { get; set; }
    }

    /// <summary>Configures the per-client request-rate token bucket partitioned by normalized client IP address.</summary>
    public sealed class WebLibRequestTrafficShapingPerClientOptions
    {
        /// <summary>Gets or sets whether the per-client token bucket participates in the limiter chain.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Gets or sets the maximum burst capacity for one client-IP partition.</summary>
        /// <remarks>The default of 40 permits short bursts above the sustained request rate.</remarks>
        public int BurstSize { get; set; } = 40;

        /// <summary>Gets or sets the sustained replenishment rate per client-IP partition, in requests per second.</summary>
        public int RequestsPerSecond { get; set; } = 10;

        /// <summary>Gets or sets the maximum queued request count per client-IP partition.</summary>
        /// <remarks>Queued requests are processed oldest-first by the native token bucket. The default is 20.</remarks>
        public int QueueLimit { get; set; } = 20;

        /// <summary>Gets or sets how requests without a resolved client IP are handled by this per-client layer.</summary>
        public MissingClientIpBehavior MissingClientIpBehavior { get; set; } = MissingClientIpBehavior.SharedPartition;
    }

    /// <summary>Configures the optional server-wide request-rate token bucket.</summary>
    /// <remarks>
    /// The server-wide limiter is intentionally opt-in. Its numeric defaults are zero so enabling it without explicit burst
    /// and sustained-rate values fails validation instead of silently imposing an arbitrary production limit. Its bounded
    /// queue is native <c>OldestFirst</c> FIFO arrival ordering, not per-client fairness.
    /// </remarks>
    public sealed class WebLibRequestTrafficShapingServerWideOptions
    {
        /// <summary>Gets or sets whether the server-wide token bucket participates in the limiter chain.</summary>
        public bool Enabled { get; set; }

        /// <summary>Gets or sets the maximum aggregate burst capacity for the server instance.</summary>
        public int BurstSize { get; set; }

        /// <summary>Gets or sets the aggregate sustained replenishment rate, in requests per second.</summary>
        public int RequestsPerSecond { get; set; }

        /// <summary>Gets or sets the maximum request count queued by the shared server-wide token bucket.</summary>
        public int QueueLimit { get; set; }
    }

    /// <summary>Defines how the per-client limiter behaves when no client IP is available on the connection.</summary>
    public enum MissingClientIpBehavior
    {
        /// <summary>Places all requests without a client IP into one shared token-bucket partition.</summary>
        SharedPartition = 0,

        /// <summary>
        /// Bypasses only the per-client token bucket for requests without a client IP. Server-wide shaping and optional
        /// global concurrency limiting still apply.
        /// </summary>
        BypassPerIpLimit = 1,
    }
}
