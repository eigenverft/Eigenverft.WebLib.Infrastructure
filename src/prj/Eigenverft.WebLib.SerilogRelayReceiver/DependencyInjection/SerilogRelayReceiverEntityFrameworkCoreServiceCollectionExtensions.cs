using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Eigenverft.WebLib.SerilogRelayReceiver
{
    /// <summary>
    /// Registers the built-in Entity Framework Core SerilogRelay persistence handler.
    /// </summary>
    public static class SerilogRelayReceiverEntityFrameworkCoreServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the built-in durable handler against a host-owned Entity Framework Core DbContext.
        /// The host remains responsible for registering an <see cref="IDbContextFactory{TContext}"/>
        /// for <typeparamref name="TDbContext"/> with the desired database provider. The handler
        /// creates one isolated DbContext instance per accepted batch.
        /// </summary>
        public static IServiceCollection AddSerilogRelayReceiverEntityFrameworkCore<TDbContext>(
            this IServiceCollection services)
            where TDbContext : DbContext
        {
            ArgumentNullException.ThrowIfNull(services);

            services.TryAddScoped<EntityFrameworkCoreSerilogRelayBatchHandler<TDbContext>>();
            return services;
        }
    }
}
