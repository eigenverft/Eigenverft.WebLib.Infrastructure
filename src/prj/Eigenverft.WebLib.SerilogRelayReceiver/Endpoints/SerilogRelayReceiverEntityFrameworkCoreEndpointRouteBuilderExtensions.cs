using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Eigenverft.WebLib.SerilogRelayReceiver
{
    /// <summary>
    /// Maps SerilogRelay endpoints backed by the built-in Entity Framework Core handler.
    /// </summary>
    public static class SerilogRelayReceiverEntityFrameworkCoreEndpointRouteBuilderExtensions
    {
        /// <summary>
        /// Maps one SerilogRelay POST endpoint whose accepted batches are persisted through
        /// the host-owned <typeparamref name="TDbContext"/>.
        /// </summary>
        public static IEndpointConventionBuilder MapSerilogRelayReceiverEntityFrameworkCore<TDbContext>(
            this IEndpointRouteBuilder endpoints,
            string pattern,
            Action<SerilogRelayReceiverOptions>? configure = null)
            where TDbContext : DbContext
        {
            return endpoints.MapSerilogRelayReceiver<
                EntityFrameworkCoreSerilogRelayBatchHandler<TDbContext>>(
                    pattern,
                    configure);
        }
    }
}
