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
        /// Gets or sets the maximum number of log events accepted in one batch.
        /// </summary>
        public int MaximumBatchEvents { get; set; } = 100;
    }
}
