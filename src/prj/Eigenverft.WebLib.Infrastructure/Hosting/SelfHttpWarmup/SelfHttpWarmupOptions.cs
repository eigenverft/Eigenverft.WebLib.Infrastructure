using System;

namespace Eigenverft.WebLib.Infrastructure.Hosting.SelfHttpWarmup
{
    /// <summary>
    /// Configures optional HTTP requests sent to the application after startup.
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
        /// The feature is opt-in and is disabled by default when only configuration binding is registered.
        /// URL-based and code-based <c>AddSelfHttpWarmup(...)</c> overloads enable it automatically.
        /// </remarks>
        public bool Enabled { get; set; }

        /// <summary>
        /// Gets or sets an optional delay applied after application startup before the first request.
        /// </summary>
        public TimeSpan InitialDelay { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Gets or sets the maximum duration of each warmup request.
        /// </summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Gets or sets the absolute HTTP or HTTPS URLs requested once during startup warmup.
        /// </summary>
        public string[] TargetUrls { get; set; } = Array.Empty<string>();
    }
}
