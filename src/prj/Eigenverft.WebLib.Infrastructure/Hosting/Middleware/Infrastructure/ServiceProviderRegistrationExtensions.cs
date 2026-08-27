using System;
using System.Collections.Generic;

using Microsoft.Extensions.DependencyInjection;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Middleware.Infrastructure
{
    /// <summary>
    /// Provides developer-facing validation that required middleware services were registered.
    /// </summary>
    public static class ServiceProviderRegistrationExtensions
    {
        /// <summary>
        /// Ensures that all specified closed service types are available without activating those services.
        /// </summary>
        /// <param name="serviceProvider">The application service provider.</param>
        /// <param name="registrationHint">Optional guidance such as the matching <c>AddFoo()</c> call.</param>
        /// <param name="requiredServiceTypes">Closed service types required by the middleware feature.</param>
        public static void EnsureServicesRegistered(
            this IServiceProvider serviceProvider,
            string? registrationHint,
            params Type[] requiredServiceTypes)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider);
            ArgumentNullException.ThrowIfNull(requiredServiceTypes);

            var serviceProbe = serviceProvider.GetService<IServiceProviderIsService>()
                ?? throw new InvalidOperationException(
                    $"The configured service provider does not expose {nameof(IServiceProviderIsService)}, so middleware service registrations cannot be validated without activation.");

            var missing = new List<string>();

            foreach (var requiredServiceType in requiredServiceTypes)
            {
                ArgumentNullException.ThrowIfNull(requiredServiceType);

                if (requiredServiceType.IsGenericTypeDefinition)
                {
                    throw new ArgumentException(
                        $"Open generic service type '{requiredServiceType}' cannot be checked with {nameof(IServiceProviderIsService)}. Pass a representative closed service type or a dedicated registration marker instead.",
                        nameof(requiredServiceTypes));
                }

                if (!serviceProbe.IsService(requiredServiceType))
                    missing.Add(requiredServiceType.FullName ?? requiredServiceType.Name);
            }

            if (missing.Count == 0)
                return;

            var message = "The following required services are not registered: " + string.Join(", ", missing) + ".";
            if (!string.IsNullOrWhiteSpace(registrationHint))
                message += " " + registrationHint.Trim();

            throw new InvalidOperationException(message);
        }

        /// <summary>
        /// Ensures that all specified closed service types are available without activating those services.
        /// </summary>
        public static void EnsureServicesRegistered(this IServiceProvider serviceProvider, params Type[] requiredServiceTypes)
            => EnsureServicesRegistered(serviceProvider, null, requiredServiceTypes);

        /// <summary>
        /// Ensures that a single service is registered without activating it.
        /// </summary>
        public static void EnsureServicesRegistered<TService>(this IServiceProvider serviceProvider, string? registrationHint = null)
            => EnsureServicesRegistered(serviceProvider, registrationHint, typeof(TService));
    }
}
