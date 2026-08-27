using System;
using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Builder;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Middleware.Infrastructure
{
    /// <summary>
    /// Provides small middleware-composition helpers.
    /// </summary>
    public static class ApplicationBuilderMiddlewareExtensions
    {
        private const string MarkerPrefix = "Eigenverft.WebLib.Infrastructure.UseMiddlewareOnce:";

        /// <summary>
        /// Adds a convention-based middleware type once per linear application-builder pipeline.
        /// </summary>
        /// <remarks>
        /// Native <see cref="IApplicationBuilder.New"/> branch builders copy application properties, so a marker
        /// established before a non-rejoining branch is inherited while markers added inside one branch remain local
        /// to that branch. This helper intentionally does not attempt graph-wide deduplication for rejoining pipelines.
        /// </remarks>
        public static IApplicationBuilder UseMiddlewareOnce<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] TMiddleware>(
            this IApplicationBuilder app)
            where TMiddleware : class
        {
            ArgumentNullException.ThrowIfNull(app);

            var middlewareIdentity = typeof(TMiddleware).AssemblyQualifiedName
                ?? typeof(TMiddleware).FullName
                ?? typeof(TMiddleware).Name;
            var markerKey = MarkerPrefix + middlewareIdentity;

            if (app.Properties.TryGetValue(markerKey, out var marker) && marker is true)
                return app;

            app.Properties[markerKey] = true;
            return app.UseMiddleware<TMiddleware>();
        }
    }
}
