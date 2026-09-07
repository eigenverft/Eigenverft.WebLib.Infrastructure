using System;

namespace Eigenverft.WebLib.Infrastructure.Hosting.RateLimiting
{
    /// <summary>
    /// Configures WebLib request traffic shaping on top of ASP.NET Core's native rate-limiters.
    /// </summary>
    /// <remarks>
    /// The per-client and server-wide token buckets are both enabled by default with finite WebLib starting values. These
    /// values are infrastructure defaults, not capacity guarantees, and consumers should tune them after representative
    /// load testing. The optional global concurrency limit remains an independent third dimension for active work.
    /// </remarks>
    public sealed class WebLibRequestTrafficShapingOptions
    {
        /// <summary>Gets or sets the per-client token-bucket settings.</summary>
        public WebLibRequestTrafficShapingPerClientOptions PerClient { get; set; } = new();

        /// <summary>Gets or sets the server-wide token-bucket settings.</summary>
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

    /// <summary>Configures the server-wide request-rate token bucket.</summary>
    /// <remarks>
    /// The server-wide limiter is enabled by default with WebLib starting values of 10,000 sustained requests per second,
    /// a 10,000-request burst capacity, and a 10,000-request bounded queue. These values are not a capacity guarantee and
    /// should be tuned for the consumer after representative load testing. The queue uses native <c>OldestFirst</c> FIFO
    /// arrival ordering and does not provide per-client fairness.
    /// </remarks>
    public sealed class WebLibRequestTrafficShapingServerWideOptions
    {
        /// <summary>Gets or sets whether the server-wide token bucket participates in the limiter chain.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Gets or sets the maximum aggregate burst capacity for the server instance.</summary>
        /// <remarks>The WebLib starting default is 10,000 requests.</remarks>
        public int BurstSize { get; set; } = 10_000;

        /// <summary>Gets or sets the aggregate sustained replenishment rate, in requests per second.</summary>
        /// <remarks>The WebLib starting default is 10,000 requests per second.</remarks>
        public int RequestsPerSecond { get; set; } = 10_000;

        /// <summary>Gets or sets the maximum request count queued by the shared server-wide token bucket.</summary>
        /// <remarks>The WebLib starting default is 10,000 queued requests, approximately one sustained-rate second.</remarks>
        public int QueueLimit { get; set; } = 10_000;
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
