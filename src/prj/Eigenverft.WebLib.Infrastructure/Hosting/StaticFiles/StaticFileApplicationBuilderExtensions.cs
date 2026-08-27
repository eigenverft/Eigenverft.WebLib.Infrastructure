using System;

using Microsoft.AspNetCore.Builder;

namespace Eigenverft.WebLib.Infrastructure.Hosting.StaticFiles
{
    /// <summary>
    /// Adds typed, additive static-file mappings and a thin PWA file-hosting convenience over ASP.NET Core.
    /// </summary>
    public static class StaticFileApplicationBuilderExtensions
    {
        /// <summary>
        /// Registers ASP.NET Core static-file middleware while retaining the target framework's default MIME mappings
        /// as the base and adding only extensions missing from those defaults.
        /// </summary>
        /// <param name="app">The application or branch builder.</param>
        /// <param name="additionalMappings">The typed mapping group to add.</param>
        /// <returns>The same builder for chaining.</returns>
        public static IApplicationBuilder UseStaticFiles(
            this IApplicationBuilder app,
            StaticFileAdditionalMappings additionalMappings)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(additionalMappings);

            var options = new StaticFileOptions
            {
                ContentTypeProvider = StaticFileContentTypeProviderFactory.Create(additionalMappings),
            };

            return StaticFileExtensions.UseStaticFiles(app, options);
        }

        /// <summary>
        /// Hosts a PWA/static web app with ASP.NET Core file-server and default-file behavior plus
        /// <see cref="AdditionalMappings.WebApp"/>. This is the recommended common-case overload.
        /// </summary>
        /// <remarks>
        /// Use this inside <c>MapIsolated</c> when the URL subtree must own misses. The native isolated branch then
        /// supplies the terminal 404 behavior; this helper does not add a custom fallback or routing system.
        /// </remarks>
        public static IApplicationBuilder UsePwaHost(this IApplicationBuilder app)
        {
            return UsePwaHost(app, AdditionalMappings.WebApp);
        }

        /// <summary>
        /// Hosts a PWA/static web app with ASP.NET Core file-server and default-file behavior plus a typed mapping group.
        /// </summary>
        /// <param name="app">The application or isolated branch builder.</param>
        /// <param name="additionalMappings">The typed mappings to add to framework defaults.</param>
        /// <returns>The same builder for chaining.</returns>
        public static IApplicationBuilder UsePwaHost(
            this IApplicationBuilder app,
            StaticFileAdditionalMappings additionalMappings)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(additionalMappings);

            var options = new FileServerOptions
            {
                EnableDefaultFiles = true,
                EnableDirectoryBrowsing = false,
            };

            options.StaticFileOptions.ContentTypeProvider =
                StaticFileContentTypeProviderFactory.Create(additionalMappings);

            return FileServerExtensions.UseFileServer(app, options);
        }
    }
}
