using System;

namespace Eigenverft.WebLib.Infrastructure.Hosting.RequestTrafficLogging
{
    /// <summary>
    /// Selects groups of fields captured by request traffic logging.
    /// </summary>
    [Flags]
    public enum RequestTrafficLoggingFields
    {
        /// <summary>
        /// Captures no optional HTTP field groups. The lifecycle envelope still emits one <c>RequestTraffic</c>
        /// record containing at least <c>Event</c> and <c>Outcome</c> while information logging is enabled.
        /// </summary>
        None = 0,

        /// <summary>Captures the inexpensive request/response lifecycle fields that form the core traffic record.</summary>
        Core = 1 << 0,

        /// <summary>Captures the raw request query string.</summary>
        Query = 1 << 1,

        /// <summary>Captures request headers with framework redaction plus the configured sensitive-value policy.</summary>
        RequestHeaders = 1 << 2,

        /// <summary>Captures response headers with framework redaction plus the configured sensitive-value policy.</summary>
        ResponseHeaders = 1 << 3,

        /// <summary>Captures a bounded request-body prefix for supported text media types.</summary>
        RequestBody = 1 << 4,

        /// <summary>Captures a bounded response-body prefix for supported text media types.</summary>
        ResponseBody = 1 << 5,

        /// <summary>Captures endpoint and route-pattern information.</summary>
        Routing = 1 << 6,

        /// <summary>Captures a small authentication identity summary without claims.</summary>
        Identity = 1 << 7,

        /// <summary>Captures local/remote connection ports and local IP information.</summary>
        Connection = 1 << 8,

        /// <summary>Captures normalized forwarded-IP information when the WebLib client-network feature is present.</summary>
        ForwardedInformation = 1 << 9,

        /// <summary>Captures every field group supported by this version.</summary>
        All = Core | Query | RequestHeaders | ResponseHeaders | RequestBody | ResponseBody | Routing | Identity | Connection | ForwardedInformation,
    }
}
