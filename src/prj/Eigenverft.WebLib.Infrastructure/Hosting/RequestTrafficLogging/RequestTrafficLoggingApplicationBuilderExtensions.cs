using System;

using Microsoft.AspNetCore.Builder;

namespace Eigenverft.WebLib.Infrastructure.Hosting.RequestTrafficLogging
{
    /// <summary>
    /// Provides pipeline activation for WebLib request traffic logging.
    /// </summary>
    public static class RequestTrafficLoggingApplicationBuilderExtensions
    {
        private const string MarkerKey = "Eigenverft.WebLib.Infrastructure.UseRequestTrafficLogging";

        /// <summary>
        /// Adds ASP.NET Core HTTP capture followed by the WebLib request-completion layer.
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <returns>The original application builder.</returns>
        /// <remarks>
        /// Register this before exception-handling middleware when handled exceptions should be classified as
        /// <c>Faulted</c> with their final handled response status. The method is idempotent per linear application pipeline.
        /// Services must first be registered with <c>AddRequestTrafficLogging()</c>.
        /// </remarks>
        public static IApplicationBuilder UseRequestTrafficLogging(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            if (app.Properties.TryGetValue(MarkerKey, out object? marker) && marker is true)
            {
                return app;
            }

            app.UseHttpLogging();
            app.UseMiddleware<RequestTrafficLoggingCompletionMiddleware>();
            app.Properties[MarkerKey] = true;
            return app;
        }
    }
}
