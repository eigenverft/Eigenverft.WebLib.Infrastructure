using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Eigenverft.WebLib.SerilogRelayReceiver
{
    /// <summary>
    /// Registers SerilogRelay receiver handlers.
    /// </summary>
    public static class SerilogRelayReceiverServiceCollectionExtensions
    {
        /// <summary>
        /// Registers a handler that can be selected by a mapped SerilogRelay receiver endpoint.
        /// </summary>
        public static IServiceCollection AddSerilogRelayReceiver<THandler>(
            this IServiceCollection services)
            where THandler : class, ISerilogRelayBatchHandler
        {
            ArgumentNullException.ThrowIfNull(services);

            services.TryAddScoped<THandler>();
            return services;
        }
    }
}
