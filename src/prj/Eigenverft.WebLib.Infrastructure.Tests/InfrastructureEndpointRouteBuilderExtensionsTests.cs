using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Eigenverft.WebLib.Infrastructure.Tests;

[TestClass]
public sealed class InfrastructureEndpointRouteBuilderExtensionsTests
{
    [TestMethod]
    public void MapInfrastructureLivenessMapsDefaultRoute()
    {
        WebApplication application = WebApplication.CreateBuilder().Build();

        application.MapInfrastructureLiveness();

        RouteEndpoint endpoint = ((IEndpointRouteBuilder)application)
            .DataSources
            .SelectMany(static source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single();

        Assert.AreEqual("/health/live", endpoint.RoutePattern.RawText);
    }

    [TestMethod]
    public void MapInfrastructureLivenessRejectsEmptyPattern()
    {
        WebApplication application = WebApplication.CreateBuilder().Build();

        Assert.ThrowsExactly<ArgumentException>(
            () => application.MapInfrastructureLiveness(string.Empty));
    }
}
