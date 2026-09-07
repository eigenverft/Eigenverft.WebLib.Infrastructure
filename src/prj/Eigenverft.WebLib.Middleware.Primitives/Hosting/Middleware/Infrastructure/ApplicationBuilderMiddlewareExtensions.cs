using System;
using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;

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

            app.UseMiddleware<TMiddleware>();
            app.Properties[markerKey] = true;
            return app;
        }

        /// <summary>
        /// Creates an isolated options monitor for one middleware use by rebuilding the registered options baseline
        /// and applying the supplied local override afterwards.
        /// </summary>
        /// <remarks>
        /// Global options are not modified and separate middleware uses remain independent. Registered configure and
        /// post-configure steps build the baseline before <paramref name="configure"/> runs, and registered validation
        /// runs afterwards. Configuration reloads rebuild from the current registered baseline and then reapply the
        /// same local override.
        /// </remarks>
        /// <typeparam name="TOptions">The options type.</typeparam>
        /// <param name="app">The application builder for the concrete middleware use.</param>
        /// <param name="configure">The local override to apply only to this options monitor.</param>
        /// <returns>An isolated, reload-aware options monitor.</returns>
        public static IOptionsMonitor<TOptions> CreateUseSiteOptionsMonitor<TOptions>(
            this IApplicationBuilder app,
            Action<TOptions> configure)
            where TOptions : class
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(configure);

            return UseSiteOptionsMonitorFactory.Create(app, configure);
        }
    }
}
