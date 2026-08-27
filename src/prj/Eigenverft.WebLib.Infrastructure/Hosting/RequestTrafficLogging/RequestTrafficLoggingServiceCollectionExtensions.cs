using System;
using System.Collections.Generic;

using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Eigenverft.WebLib.Infrastructure.Hosting.RequestTrafficLogging
{
    /// <summary>
    /// Provides service-registration helpers for WebLib request traffic logging.
    /// </summary>
    public static class RequestTrafficLoggingServiceCollectionExtensions
    {
        /// <summary>
        /// Adds framework HTTP capture plus WebLib request-completion semantics for one structured traffic record per request.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="configure">Optional startup-time traffic logging configuration.</param>
        /// <returns>The original service collection.</returns>
        /// <remarks>
        /// Request traffic logging intentionally owns the global ASP.NET Core <see cref="HttpLoggingOptions"/>
        /// used by this pipeline: it post-configures combined logging, the baseline logging fields, body limits,
        /// and header allowlists so that the interceptor can select the final per-request capture. Existing
        /// <c>AddHttpLogging(...)</c> configuration is therefore normalized to the A4 traffic-logging contract.
        /// Configure the shared capture through <see cref="RequestTrafficLoggingOptions"/> when A4 is enabled.
        /// </remarks>
        public static IServiceCollection AddRequestTrafficLogging(
            this IServiceCollection services,
            Action<RequestTrafficLoggingOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            OptionsBuilder<RequestTrafficLoggingOptions> optionsBuilder = services.AddOptions<RequestTrafficLoggingOptions>();
            if (configure is not null)
            {
                optionsBuilder.Configure(configure);
            }

            optionsBuilder
                .Validate(
                    static options => (options.Fields & ~RequestTrafficLoggingFields.All) == 0,
                    "Fields contains unsupported request traffic logging flags.")
                .Validate(
                    static options => Enum.IsDefined(typeof(SensitiveValueMode), options.SensitiveValueMode),
                    "SensitiveValueMode is invalid.")
                .Validate(static options => options.RequestBodyLimit >= 0, "RequestBodyLimit cannot be negative.")
                .Validate(static options => options.ResponseBodyLimit >= 0, "ResponseBodyLimit cannot be negative.")
                .ValidateOnStart();

            services.AddHttpLogging(static _ => { });
            services.AddHttpLoggingInterceptor<RequestTrafficLoggingInterceptor>();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IPostConfigureOptions<HttpLoggingOptions>, RequestTrafficLoggingHttpLoggingOptionsSetup>());

            return services;
        }
    }

    internal sealed class RequestTrafficLoggingHttpLoggingOptionsSetup : IPostConfigureOptions<HttpLoggingOptions>
    {
        private readonly IOptionsMonitor<RequestTrafficLoggingOptions> _requestTrafficOptions;

        public RequestTrafficLoggingHttpLoggingOptionsSetup(IOptionsMonitor<RequestTrafficLoggingOptions> requestTrafficOptions)
        {
            _requestTrafficOptions = requestTrafficOptions ?? throw new ArgumentNullException(nameof(requestTrafficOptions));
        }

        public void PostConfigure(string? name, HttpLoggingOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            RequestTrafficLoggingOptions trafficOptions = _requestTrafficOptions.CurrentValue;

            // A4 owns the one combined framework event. Per-request fields and body limits are selected by the interceptor.
            options.CombineLogs = true;
            options.LoggingFields = HttpLoggingFields.None;
            options.RequestBodyLogLimit = trafficOptions.RequestBodyLimit;
            options.ResponseBodyLogLimit = trafficOptions.ResponseBodyLimit;

            ConfigureAllowedHeaders(options.RequestHeaders, trafficOptions.RequestHeaders, trafficOptions);
            ConfigureAllowedHeaders(options.ResponseHeaders, trafficOptions.ResponseHeaders, trafficOptions);
        }

        private static void ConfigureAllowedHeaders(
            ISet<string> frameworkHeaders,
            ISet<string> configuredHeaders,
            RequestTrafficLoggingOptions options)
        {
            frameworkHeaders.Clear();

            foreach (string header in configuredHeaders)
            {
                if (options.SensitiveValueMode != SensitiveValueMode.Include && options.SensitiveHeaders.Contains(header))
                {
                    continue;
                }

                frameworkHeaders.Add(header);
            }

            if (options.SensitiveValueMode == SensitiveValueMode.Include)
            {
                foreach (string header in options.SensitiveHeaders)
                {
                    frameworkHeaders.Add(header);
                }
            }
        }
    }
}
