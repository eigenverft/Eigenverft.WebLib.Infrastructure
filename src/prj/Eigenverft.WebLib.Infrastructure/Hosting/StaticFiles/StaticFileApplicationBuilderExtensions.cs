using System;

using Microsoft.AspNetCore.Builder;

namespace Eigenverft.WebLib.Infrastructure.Hosting.StaticFiles
{
    /// <summary>
    /// Adds typed, additive static-file mappings over ASP.NET Core defaults.
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
    }
}
