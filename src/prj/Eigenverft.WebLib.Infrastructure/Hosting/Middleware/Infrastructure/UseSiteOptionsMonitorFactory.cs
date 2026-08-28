using System;
using System.Collections.Generic;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Middleware.Infrastructure
{
    /// <summary>
    /// Creates an isolated options monitor for one concrete middleware use while reusing the registered options pipeline.
    /// </summary>
    internal static class UseSiteOptionsMonitorFactory
    {
        /// <summary>
        /// Creates a monitor whose default options instance is built from the registered configure/post-configure pipeline,
        /// then receives the local override, and is finally validated by the registered validators.
        /// </summary>
        /// <typeparam name="TOptions">Options type.</typeparam>
        /// <param name="app">Application builder for the concrete middleware use.</param>
        /// <param name="configure">Local final override for this middleware use.</param>
        /// <returns>An isolated reload-aware options monitor.</returns>
        internal static IOptionsMonitor<TOptions> CreateUseSiteOptionsMonitor<TOptions>(
            this IApplicationBuilder app,
            Action<TOptions> configure)
            where TOptions : class
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(configure);

            IServiceProvider services = app.ApplicationServices;
            IEnumerable<IConfigureOptions<TOptions>> setups =
                services.GetRequiredService<IEnumerable<IConfigureOptions<TOptions>>>();
            IEnumerable<IPostConfigureOptions<TOptions>> registeredPostConfigures =
                services.GetRequiredService<IEnumerable<IPostConfigureOptions<TOptions>>>();
            IEnumerable<IValidateOptions<TOptions>> validations =
                services.GetRequiredService<IEnumerable<IValidateOptions<TOptions>>>();
            IEnumerable<IOptionsChangeTokenSource<TOptions>> changeTokenSources =
                services.GetRequiredService<IEnumerable<IOptionsChangeTokenSource<TOptions>>>();

            var postConfigures = new List<IPostConfigureOptions<TOptions>>(registeredPostConfigures)
            {
                new UseSitePostConfigureOptions<TOptions>(configure),
            };

            var factory = new OptionsFactory<TOptions>(setups, postConfigures, validations);
            var monitor = new OptionsMonitor<TOptions>(
                factory,
                changeTokenSources,
                new OptionsCache<TOptions>());

            IHostApplicationLifetime? lifetime = services.GetService<IHostApplicationLifetime>();
            if (lifetime is not null)
            {
                lifetime.ApplicationStopped.Register(
                    static state => ((IDisposable)state!).Dispose(),
                    monitor);
            }

            return monitor;
        }

        private sealed class UseSitePostConfigureOptions<TOptions> : IPostConfigureOptions<TOptions>
            where TOptions : class
        {
            private readonly Action<TOptions> _configure;

            internal UseSitePostConfigureOptions(Action<TOptions> configure)
            {
                _configure = configure;
            }

            public void PostConfigure(string? name, TOptions options)
            {
                if (string.Equals(name, Options.DefaultName, StringComparison.Ordinal))
                {
                    _configure(options);
                }
            }
        }
    }
}
