using System;

namespace Eigenverft.WebLib.Infrastructure.Hosting.SelfHttpWarmup
{
    /// <summary>
    /// Configures optional HTTP requests sent to the application after ASP.NET Core has fully started.
    /// </summary>
    public sealed class SelfHttpWarmupOptions
    {
        /// <summary>
        /// Configuration section used by <c>AddSelfHttpWarmup()</c>.
        /// </summary>
        public const string SectionName = "SelfHttpWarmup";

        /// <summary>
        /// Gets or sets a value indicating whether self-HTTP warmup is enabled.
        /// </summary>
        /// <remarks>
        /// The feature is opt-in and is disabled by default even when its services are registered.
        /// </remarks>
        public bool Enabled { get; set; }

        /// <summary>
        /// Gets or sets an optional delay applied after <c>ApplicationStarted</c> before the first request.
        /// </summary>
        public TimeSpan InitialDelay { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Gets or sets the maximum duration of each warmup request.
        /// </summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Gets or sets the maximum duration of one TCP connection attempt to one resolved IP address.
        /// </summary>
        /// <remarks>
        /// When a host resolves to multiple addresses, a failed or timed-out attempt falls back to the next address.
        /// The request timeout remains the overall upper bound for the request.
        /// </remarks>
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Gets or sets the absolute HTTP or HTTPS URLs requested once during startup warmup.
        /// </summary>
        public string[] TargetUrls { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the User-Agent header sent with warmup requests.
        /// </summary>
        public string UserAgent { get; set; } = "Eigenverft.WebLib.Infrastructure/SelfHttpWarmup";
    }
}
