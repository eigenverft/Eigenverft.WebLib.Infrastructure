using System;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Middleware.CanonicalHostRedirect
{
    /// <summary>
    /// Determines how requests for the primary host group are canonicalized.
    /// </summary>
    public enum CanonicalHostMode
    {
        /// <summary>
        /// Uses the configured apex host, for example <c>example.com</c>.
        /// </summary>
        ToApex = 1,

        /// <summary>
        /// Uses the <c>www.</c> form of the configured apex host, for example <c>www.example.com</c>.
        /// </summary>
        ToWww = 2,
    }

    /// <summary>
    /// Configures canonical host and HTTPS redirects.
    /// </summary>
    public sealed class CanonicalHostRedirectOptions
    {
        /// <summary>
        /// Gets or sets the primary apex host without a port, for example <c>example.com</c>.
        /// </summary>
        public string PrimaryApexHost { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets additional inbound host aliases, without ports, that redirect to the selected primary host form.
        /// </summary>
        public string[] RedirectFromHosts { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the canonical host form for the primary apex/www group and inbound aliases.
        /// </summary>
        public CanonicalHostMode Canonicalization { get; set; } = CanonicalHostMode.ToWww;

        /// <summary>
        /// Gets or sets the optional HTTPS target port.
        /// </summary>
        /// <remarks>
        /// A null value, or an explicit value of 443, produces the canonical implicit HTTPS port and therefore omits
        /// a port from the redirect URI. An alternate value is the one HTTPS port used by every HTTPS redirect.
        /// Incoming HTTP ports are never copied to an HTTPS target.
        /// </remarks>
        public int? HttpsTargetPort { get; set; }

    }
}
