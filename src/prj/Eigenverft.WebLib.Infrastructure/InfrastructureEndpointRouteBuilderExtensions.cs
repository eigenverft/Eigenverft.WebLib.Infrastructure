using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Eigenverft.WebLib.Infrastructure;

/// <summary>
/// Maps shared endpoints used by Eigenverft web-infrastructure components.
/// </summary>
public static class InfrastructureEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps a lightweight process-liveness endpoint.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The route pattern to map.</param>
    /// <returns>The mapped route handler.</returns>
    public static RouteHandlerBuilder MapInfrastructureLiveness(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/health/live")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        return endpoints.MapGet(
            pattern,
            static () => TypedResults.Ok(new InfrastructureLivenessResponse("Healthy")));
    }
}
