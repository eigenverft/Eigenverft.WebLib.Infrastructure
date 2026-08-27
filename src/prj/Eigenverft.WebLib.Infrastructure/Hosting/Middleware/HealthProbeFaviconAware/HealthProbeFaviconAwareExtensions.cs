using System;

using Microsoft.AspNetCore.Builder;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Middleware.HealthProbeFaviconAware
{
    /// <summary>
    /// Adds the favicon-aware health probe to an ASP.NET Core request pipeline.
    /// </summary>
    public static class HealthProbeFaviconAwareExtensions
    {
        /// <summary>
        /// Adds the fixed <c>/health</c> short-circuit and referer-aware favicon suppression middleware.
        /// </summary>
        /// <remarks>
        /// Call this early in the pipeline when health requests must bypass later filters or middleware.
        /// </remarks>
        /// <param name="app">The application builder.</param>
        /// <returns>The same application builder.</returns>
        public static IApplicationBuilder UseHealthProbeFaviconAware(this IApplicationBuilder app)
        {
            if (app is null)
            {
                throw new ArgumentNullException(nameof(app));
            }

            return app.UseMiddleware<HealthProbeFaviconAwareMiddleware>();
        }
    }
}
