using System;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Middleware.HealthProbeFaviconAware
{
    /// <summary>
    /// Provides a small <c>/health</c> short-circuit and suppresses browser favicon noise originating from that probe.
    /// </summary>
    /// <remarks>
    /// Register this middleware before filters or other pipeline components that must not run for health probes.
    /// Only GET and HEAD are handled for <c>/health</c>. A GET or HEAD for <c>/favicon.ico</c> is answered with
    /// 204 only when its Referer points to <c>/health</c>.
    /// </remarks>
    internal sealed class HealthProbeFaviconAwareMiddleware
    {
        private const string HealthPathText = "/health";
        private const string DefaultContentType = "text/plain; charset=utf-8";
        private static readonly PathString HealthPath = new(HealthPathText);
        private static readonly PathString FaviconPath = new("/favicon.ico");

        private readonly RequestDelegate _next;

        /// <summary>
        /// Initializes the middleware.
        /// </summary>
        /// <param name="next">The next request delegate.</param>
        public HealthProbeFaviconAwareMiddleware(RequestDelegate next)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
        }

        /// <summary>
        /// Handles health and probe-originated favicon requests or continues the pipeline.
        /// </summary>
        /// <param name="context">The current HTTP context.</param>
        /// <returns>A task representing request processing.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (context.Request.Path.Equals(HealthPath, StringComparison.OrdinalIgnoreCase) &&
                (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)))
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = DefaultContentType;
                context.Response.Headers.CacheControl = "no-store, no-cache";
                context.Response.Headers.Pragma = "no-cache";

                if (!HttpMethods.IsHead(context.Request.Method))
                {
                    await context.Response.WriteAsync("OK").ConfigureAwait(false);
                }

                return;
            }

            if (context.Request.Path.Equals(FaviconPath, StringComparison.OrdinalIgnoreCase) &&
                (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) &&
                IsHealthReferer(context.Request.Headers))
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            await _next(context).ConfigureAwait(false);
        }

        private static bool IsHealthReferer(IHeaderDictionary headers)
        {
            if (!headers.TryGetValue("Referer", out StringValues refererValues))
            {
                return false;
            }

            string referer = refererValues.ToString();
            if (string.IsNullOrWhiteSpace(referer) ||
                referer.IndexOf(HealthPathText, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            if (Uri.TryCreate(referer, UriKind.Absolute, out Uri? absoluteUri))
            {
                return absoluteUri.AbsolutePath.Equals(HealthPathText, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(referer, HealthPathText, StringComparison.OrdinalIgnoreCase);
        }
    }
}
