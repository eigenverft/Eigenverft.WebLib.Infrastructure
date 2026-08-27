using System;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Pipeline
{
    /// <summary>
    /// Provides thin semantic wrappers for non-rejoining ASP.NET Core pipeline branches.
    /// </summary>
    public static class PipelinePartitioningExtensions
    {
        /// <summary>
        /// Maps an exclusive URL subtree using native ASP.NET Core <c>Map</c> branch semantics.
        /// </summary>
        /// <remarks>
        /// The matched path segment is preserved so normal <c>UseStaticFiles</c>/<c>UseFileServer</c> middleware can
        /// resolve <c>/apps/...</c> against the corresponding <c>wwwroot/apps/...</c> path without a custom mount layer.
        /// The branch does not rejoin the remaining pipeline. Any already-active outer status-code-pages feature is
        /// disabled for the isolated request so status-code re-execution cannot transfer a branch-owned 404 to
        /// <c>MapRemaining</c> or later shell endpoints.
        /// </remarks>
        /// <param name="app">The application builder.</param>
        /// <param name="pathMatch">The URL subtree to own.</param>
        /// <param name="configuration">The native branch pipeline configuration.</param>
        /// <returns>The original application builder.</returns>
        public static IApplicationBuilder MapIsolated(
            this IApplicationBuilder app,
            PathString pathMatch,
            Action<IApplicationBuilder> configuration)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(configuration);

            return app.Map(
                pathMatch,
                preserveMatchedPathSegment: true,
                branch =>
                {
                    branch.Use(static (context, next) =>
                    {
                        IStatusCodePagesFeature? statusCodePages =
                            context.Features.Get<IStatusCodePagesFeature>();

                        if (statusCodePages is not null)
                        {
                            statusCodePages.Enabled = false;
                        }

                        return next(context);
                    });

                    configuration(branch);
                });
        }

        /// <summary>
        /// Defines the terminal pipeline for all requests not claimed by earlier isolated mappings.
        /// </summary>
        /// <remarks>
        /// This uses native non-rejoining <c>MapWhen</c> branch semantics with an always-true predicate. Declare it after
        /// all <see cref="MapIsolated(IApplicationBuilder, PathString, Action{IApplicationBuilder})"/> branches.
        /// </remarks>
        /// <param name="app">The application builder.</param>
        /// <param name="configuration">The remaining shell pipeline.</param>
        /// <returns>The original application builder.</returns>
        public static IApplicationBuilder MapRemaining(
            this IApplicationBuilder app,
            Action<IApplicationBuilder> configuration)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(configuration);

            return app.MapWhen(static _ => true, configuration);
        }
    }
}
