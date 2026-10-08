namespace Eigenverft.WebLib.SerilogRelayReceiver
{
    /// <summary>
    /// Configures one mapped SerilogRelay receiver endpoint.
    /// </summary>
    public sealed class SerilogRelayReceiverOptions
    {
        /// <summary>
        /// Gets or sets the optional raw bearer token required by this endpoint.
        /// Null, empty, or whitespace disables bearer-token validation.
        /// </summary>
        public string? BearerToken { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of log events accepted in one batch from any sender source.
        /// Defaults to 256, accepting both the matching sender's durable-spool batches (up to 100)
        /// and direct emergency-memory batches (up to 256), including during shutdown.
        /// If sender batch limits are changed, keep this at least as large as both limits.
        /// </summary>
        public int MaximumBatchEvents { get; set; } = 256;
    }
}
