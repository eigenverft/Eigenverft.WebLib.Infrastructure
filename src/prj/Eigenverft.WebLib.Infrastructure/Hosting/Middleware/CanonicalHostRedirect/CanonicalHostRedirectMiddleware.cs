using System;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Options;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Middleware.CanonicalHostRedirect
{
    /// <summary>
    /// Redirects requests to the configured canonical host and HTTPS target in one hop.
    /// </summary>
    /// <remarks>
    /// When forwarded headers are used, call <c>UseForwardedHeaders()</c> before this middleware so the
    /// effective request scheme and host represent the external client-facing request.
    /// </remarks>
    public sealed class CanonicalHostRedirectMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IOptionsMonitor<CanonicalHostRedirectOptions> _optionsMonitor;

        /// <summary>
        /// Initializes the middleware.
        /// </summary>
        /// <param name="next">The next request delegate.</param>
        /// <param name="optionsMonitor">The canonical redirect options.</param>
        public CanonicalHostRedirectMiddleware(
            RequestDelegate next,
            IOptionsMonitor<CanonicalHostRedirectOptions> optionsMonitor)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        }

        /// <summary>
        /// Applies canonical host, scheme, and HTTPS-port normalization when required.
        /// </summary>
        /// <param name="context">The current HTTP context.</param>
        /// <returns>A task representing request processing.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            CanonicalHostRedirectOptions options = _optionsMonitor.CurrentValue;
            if (!options.Enabled || !context.Request.Host.HasValue)
            {
                await _next(context).ConfigureAwait(false);
                return;
            }

            string requestHostOnly = context.Request.Host.Host;
            if (string.IsNullOrWhiteSpace(requestHostOnly))
            {
                await _next(context).ConfigureAwait(false);
                return;
            }

            string targetHostOnly = requestHostOnly;
            if (!string.IsNullOrWhiteSpace(options.PrimaryApexHost) &&
                TryResolveCanonicalHost(requestHostOnly, options, out string canonicalHostOnly))
            {
                targetHostOnly = canonicalHostOnly;
            }

            string requestScheme = string.IsNullOrWhiteSpace(context.Request.Scheme)
                ? Uri.UriSchemeHttp
                : context.Request.Scheme;
            string targetScheme = options.EnforceHttps ? Uri.UriSchemeHttps : requestScheme;
            HostString targetHost = BuildTargetHost(context.Request.Host, targetHostOnly, targetScheme, options);

            bool needsRedirect =
                !string.Equals(requestScheme, targetScheme, StringComparison.OrdinalIgnoreCase) ||
                !HostStringEquals(context.Request.Host, targetHost);

            if (!needsRedirect)
            {
                await _next(context).ConfigureAwait(false);
                return;
            }

            string location = UriHelper.BuildAbsolute(
                targetScheme,
                targetHost,
                context.Request.PathBase,
                context.Request.Path,
                context.Request.QueryString);

            context.Response.StatusCode = options.RedirectStatusCode;
            context.Response.Headers.Location = location;
        }

        private static HostString BuildTargetHost(
            HostString requestHost,
            string targetHostOnly,
            string targetScheme,
            CanonicalHostRedirectOptions options)
        {
            if (!string.Equals(targetScheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || !options.EnforceHttps)
            {
                return requestHost.Port.HasValue
                    ? new HostString(targetHostOnly, requestHost.Port.Value)
                    : new HostString(targetHostOnly);
            }

            int? httpsTargetPort = options.HttpsTargetPort;
            if (!httpsTargetPort.HasValue || httpsTargetPort.Value == 443)
            {
                return new HostString(targetHostOnly);
            }

            return new HostString(targetHostOnly, httpsTargetPort.Value);
        }

        private static bool HostStringEquals(HostString left, HostString right)
        {
            return string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase) && left.Port == right.Port;
        }

        private static bool TryResolveCanonicalHost(
            string requestHost,
            CanonicalHostRedirectOptions options,
            out string canonicalHost)
        {
            canonicalHost = string.Empty;

            string primaryApex = (options.PrimaryApexHost ?? string.Empty).Trim();
            if (primaryApex.Length == 0)
            {
                return false;
            }

            string primaryWww = "www." + primaryApex;
            if (string.Equals(requestHost, primaryApex, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(requestHost, primaryWww, StringComparison.OrdinalIgnoreCase))
            {
                canonicalHost = options.Canonicalization switch
                {
                    CanonicalHostMode.ToApex => primaryApex,
                    CanonicalHostMode.ToWww => primaryWww,
                    _ => requestHost,
                };

                return true;
            }

            string[] aliases = options.RedirectFromHosts ?? Array.Empty<string>();
            for (int i = 0; i < aliases.Length; i++)
            {
                string alias = (aliases[i] ?? string.Empty).Trim();
                if (alias.Length == 0 || !string.Equals(requestHost, alias, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                canonicalHost = options.Canonicalization switch
                {
                    CanonicalHostMode.ToWww => primaryWww,
                    _ => primaryApex,
                };

                return true;
            }

            return false;
        }
    }
}
