using System;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Hsts
{
    /// <summary>
    /// Registers and activates the WebLib HSTS policy on top of ASP.NET Core's native HSTS support.
    /// </summary>
    public static class WebLibHstsExtensions
    {
        private const string ConfigurationSectionName = "Hsts";

        /// <summary>
        /// Registers native HSTS services with the Eigenverft 180-day max-age baseline and binds the standard <c>Hsts</c> configuration section.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The same service collection.</returns>
        /// <remarks>
        /// Configuration is applied after the WebLib baseline. Native <see cref="HstsOptions"/> defaults, including excluded hosts,
        /// remain owned by ASP.NET Core. Middleware activation is intentionally handled separately by <see cref="UseWebLibHsts(IApplicationBuilder)"/>.
        /// </remarks>
        public static IServiceCollection AddWebLibHsts(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddHsts(static options =>
            {
                options.MaxAge = TimeSpan.FromDays(180);
            });

            services
                .AddOptions<HstsOptions>()
                .BindConfiguration(ConfigurationSectionName);

            return services;
        }

        /// <summary>
        /// Registers native HSTS services and applies code-based configuration after the <c>Hsts</c> configuration section.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Additional HSTS configuration applied last.</param>
        /// <returns>The same service collection.</returns>
        public static IServiceCollection AddWebLibHsts(
            this IServiceCollection services,
            Action<HstsOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            services.AddWebLibHsts();
            services.Configure(configure);
            return services;
        }

        /// <summary>
        /// Adds ASP.NET Core's native HSTS middleware outside the Development environment.
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <returns>The same application builder.</returns>
        /// <remarks>
        /// Place this early enough to wrap responses that may short-circuit later in the pipeline, such as rate-limit rejections and health probes.
        /// HTTP requests and excluded hosts continue to follow the native ASP.NET Core HSTS middleware semantics.
        /// </remarks>
        public static IApplicationBuilder UseWebLibHsts(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            IHostEnvironment environment = app.ApplicationServices.GetRequiredService<IHostEnvironment>();
            return environment.IsDevelopment()
                ? app
                : app.UseHsts();
        }
    }
}
