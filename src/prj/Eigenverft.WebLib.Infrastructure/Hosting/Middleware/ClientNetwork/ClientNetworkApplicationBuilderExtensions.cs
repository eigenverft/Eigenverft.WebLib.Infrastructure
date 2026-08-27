using System;

using Eigenverft.WebLib.Infrastructure.Hosting.Middleware.Infrastructure;

using Microsoft.AspNetCore.Builder;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Middleware.ClientNetwork
{
    /// <summary>
    /// Adds the shared client-network feature middleware to an ASP.NET Core pipeline.
    /// </summary>
    public static class ClientNetworkApplicationBuilderExtensions
    {
        /// <summary>
        /// Ensures that <see cref="IClientNetworkFeature"/> is populated once for requests passing through this pipeline.
        /// </summary>
        /// <remarks>
        /// Place this before middleware such as Forwarded Headers that rewrites
        /// <c>HttpContext.Connection.RemoteIpAddress</c> when the feature must capture the actual remote peer.
        /// </remarks>
        public static IApplicationBuilder UseClientNetworkFeature(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);
            return app.UseMiddlewareOnce<ClientNetworkMiddleware>();
        }
    }
}
