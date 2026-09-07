namespace Eigenverft.WebLib.Infrastructure.Hosting.RequestTrafficLogging
{
    /// <summary>
    /// Controls how configured sensitive header values are represented in a traffic record.
    /// </summary>
    public enum SensitiveValueMode
    {
        /// <summary>Keep the header name visible while replacing its value with the framework redaction marker.</summary>
        Redact = 0,

        /// <summary>Keep the value redacted and add a SHA-256 fingerprint for forensic equality comparisons.</summary>
        Hash = 1,

        /// <summary>Include the complete header value. This can expose replayable credentials and should be used deliberately.</summary>
        Include = 2,
    }
}
