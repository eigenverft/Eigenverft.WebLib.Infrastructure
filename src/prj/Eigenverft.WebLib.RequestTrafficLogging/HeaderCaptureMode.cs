namespace Eigenverft.WebLib.RequestTrafficLogging
{
    /// <summary>
    /// Controls how request and response header values are captured.
    /// </summary>
    public enum HeaderCaptureMode
    {
        /// <summary>
        /// Uses the configured header allowlists and sensitive-value policy.
        /// Unlisted header values remain redacted by ASP.NET Core HTTP Logging.
        /// </summary>
        AllowListed = 0,

        /// <summary>
        /// Captures every header value, including credentials and cookies, without framework redaction.
        /// Use only when the resulting logs are protected appropriately.
        /// </summary>
        AllRaw = 1,
    }
}
