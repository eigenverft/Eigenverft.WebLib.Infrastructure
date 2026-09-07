using System.Collections.Generic;

namespace Eigenverft.WebLib.Infrastructure.Hosting.RequestTrafficLogging
{
    /// <summary>
    /// Configures WebLib request traffic logging while delegating HTTP capture mechanics to ASP.NET Core HTTP Logging.
    /// </summary>
    public sealed class RequestTrafficLoggingOptions
    {
        /// <summary>The default maximum number of body bytes retained by framework HTTP Logging.</summary>
        public const int DefaultBodyLimit = 4096;

        /// <summary>
        /// Gets or sets the enabled traffic-record field groups.
        /// </summary>
        public RequestTrafficLoggingFields Fields { get; set; } =
            RequestTrafficLoggingFields.Core |
            RequestTrafficLoggingFields.Routing;

        /// <summary>
        /// Gets or sets how configured sensitive header values are represented.
        /// </summary>
        public SensitiveValueMode SensitiveValueMode { get; set; } = SensitiveValueMode.Redact;

        /// <summary>
        /// Gets or sets the maximum request-body bytes retained by framework HTTP Logging.
        /// When request-body metadata is enabled, <c>RequestBodyTotalBytes</c> uses the request
        /// <c>Content-Length</c> when known and otherwise remains unknown; <c>RequestBodyTruncated</c>
        /// is only classified when that total is known.
        /// </summary>
        public int RequestBodyLimit { get; set; } = DefaultBodyLimit;

        /// <summary>
        /// Gets or sets the maximum response-body bytes retained by framework HTTP Logging.
        /// When response-body metadata is enabled, <c>ResponseBodyTotalBytes</c> uses the response
        /// <c>Content-Length</c> when known and otherwise remains unknown; <c>ResponseBodyTruncated</c>
        /// is only classified when that total is known.
        /// </summary>
        public int ResponseBodyLimit { get; set; } = DefaultBodyLimit;

        /// <summary>
        /// Gets request header names whose values may be included when request-header capture is enabled.
        /// Other request header names remain visible with redacted values.
        /// </summary>
        public ISet<string> RequestHeaders { get; } = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "Accept",
            "Accept-Encoding",
            "Content-Length",
            "Content-Type",
            "User-Agent",
        };

        /// <summary>
        /// Gets response header names whose values may be included when response-header capture is enabled.
        /// Other response header names remain visible with redacted values.
        /// </summary>
        public ISet<string> ResponseHeaders { get; } = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "Cache-Control",
            "Content-Length",
            "Content-Type",
            "Location",
            "Retry-After",
        };

        /// <summary>
        /// Gets header names treated as sensitive by the selected <see cref="SensitiveValueMode"/>.
        /// </summary>
        public ISet<string> SensitiveHeaders { get; } = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "Authorization",
            "Cookie",
            "Set-Cookie",
            "X-Api-Key",
            "Api-Key",
        };
    }
}
