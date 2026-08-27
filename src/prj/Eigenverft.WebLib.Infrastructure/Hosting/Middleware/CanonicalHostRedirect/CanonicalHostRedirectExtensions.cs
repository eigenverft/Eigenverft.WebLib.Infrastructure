using System;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Middleware.CanonicalHostRedirect
{
    /// <summary>
    /// Registers and activates canonical host redirect behavior.
    /// </summary>
    public static class CanonicalHostRedirectExtensions
    {
        /// <summary>
        /// Registers canonical host redirect options from the standard
        /// <c>CanonicalHostRedirectOptions</c> configuration section.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The same service collection.</returns>
        public static IServiceCollection AddCanonicalHostRedirect(this IServiceCollection services)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services
                .AddOptions<CanonicalHostRedirectOptions>()
                .BindConfiguration(nameof(CanonicalHostRedirectOptions))
                .Validate(
                    options => !options.HttpsTargetPort.HasValue ||
                               options.HttpsTargetPort.Value is >= 1 and <= 65535,
                    "HttpsTargetPort must be between 1 and 65535 when configured.")
                .Validate(
                    HasNoEmbeddedHostPorts,
                    "PrimaryApexHost and RedirectFromHosts must not contain ports; use HttpsTargetPort for the HTTPS target port.");

            return services;
        }

        /// <summary>
        /// Registers canonical host redirect options and applies code-based configuration after configuration binding.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Additional options configuration.</param>
        /// <returns>The same service collection.</returns>
        public static IServiceCollection AddCanonicalHostRedirect(
            this IServiceCollection services,
            Action<CanonicalHostRedirectOptions> configure)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            services.AddCanonicalHostRedirect();
            services.Configure(configure);
            return services;
        }

        /// <summary>
        /// Adds the canonical host redirect middleware to the request pipeline.
        /// </summary>
        /// <remarks>
        /// Place forwarded-header middleware before this call when a reverse proxy supplies the external host or scheme.
        /// </remarks>
        /// <param name="app">The application builder.</param>
        /// <returns>The same application builder.</returns>
        public static IApplicationBuilder UseCanonicalHostRedirect(this IApplicationBuilder app)
        {
            if (app is null)
            {
                throw new ArgumentNullException(nameof(app));
            }

            return app.UseMiddleware<CanonicalHostRedirectMiddleware>();
        }

        private static bool HasNoEmbeddedHostPorts(CanonicalHostRedirectOptions options)
        {
            if (!HasNoExplicitPort(options.PrimaryApexHost))
            {
                return false;
            }

            string[] aliases = options.RedirectFromHosts ?? Array.Empty<string>();
            for (int i = 0; i < aliases.Length; i++)
            {
                if (!HasNoExplicitPort(aliases[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasNoExplicitPort(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            try
            {
                return !new HostString(value.Trim()).Port.HasValue;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
