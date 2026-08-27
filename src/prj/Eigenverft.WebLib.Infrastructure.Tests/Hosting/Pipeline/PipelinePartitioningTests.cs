using System.Threading.Tasks;

using Eigenverft.WebLib.Infrastructure.Hosting.Pipeline;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Eigenverft.WebLib.Infrastructure.Tests;

[TestClass]
public sealed class PipelinePartitioningTests
{
    [TestMethod]
    public async Task MapIsolatedOwnsMatchingSubtreeAndMapRemainingHandlesOthers()
    {
        using var host = new PipelineTestHost();

        int isolatedHits = 0;
        int remainingHits = 0;
        IApplicationBuilder app = host.CreateApplicationBuilder();

        app.MapIsolated("/apps", isolated =>
        {
            isolated.Run(context =>
            {
                isolatedHits++;
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            });
        });

        app.MapRemaining(remaining =>
        {
            remaining.Run(context =>
            {
                remainingHits++;
                context.Response.StatusCode = StatusCodes.Status200OK;
                return context.Response.WriteAsync("shell");
            });
        });

        RequestDelegate pipeline = app.Build();

        PipelineTestResponse isolatedResponse = await host.ExecuteAsync(pipeline, "/apps/item");
        Assert.AreEqual(StatusCodes.Status204NoContent, isolatedResponse.StatusCode);
        Assert.AreEqual(1, isolatedHits);
        Assert.AreEqual(0, remainingHits);

        PipelineTestResponse remainingResponse = await host.ExecuteAsync(pipeline, "/other");
        Assert.AreEqual(StatusCodes.Status200OK, remainingResponse.StatusCode);
        Assert.AreEqual(1, isolatedHits);
        Assert.AreEqual(1, remainingHits);
        Assert.AreEqual("shell", remainingResponse.Body);

        PipelineTestResponse segmentBoundaryResponse = await host.ExecuteAsync(pipeline, "/apps2");
        Assert.AreEqual(StatusCodes.Status200OK, segmentBoundaryResponse.StatusCode);
        Assert.AreEqual(1, isolatedHits);
        Assert.AreEqual(2, remainingHits);
    }

    [TestMethod]
    public async Task EmptyIsolatedBranchEndsWithNative404AndDoesNotRejoin()
    {
        using var host = new PipelineTestHost();

        bool remainingHit = false;
        IApplicationBuilder app = host.CreateApplicationBuilder();

        app.MapIsolated("/apps", _ => { });
        app.MapRemaining(remaining =>
        {
            remaining.Run(context =>
            {
                remainingHit = true;
                return context.Response.WriteAsync("shell fallback");
            });
        });

        PipelineTestResponse response = await host.ExecuteAsync(app.Build(), "/apps/missing");

        Assert.AreEqual(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.IsFalse(remainingHit);
        Assert.AreEqual(string.Empty, response.Body);
    }

    [TestMethod]
    public async Task GlobalStatusCodeReexecutionCannotEscapeIsolatedBranch()
    {
        using var host = new PipelineTestHost();

        int remainingHits = 0;
        IApplicationBuilder app = host.CreateApplicationBuilder();

        app.UseStatusCodePagesWithReExecute("/errors/{0}");
        app.MapIsolated("/apps", _ => { });
        app.MapRemaining(remaining =>
        {
            remaining.Run(context =>
            {
                remainingHits++;
                context.Response.StatusCode = StatusCodes.Status200OK;
                return context.Response.WriteAsync($"shell:{context.Request.Path}");
            });
        });

        PipelineTestResponse response = await host.ExecuteAsync(app.Build(), "/apps/missing");

        Assert.AreEqual(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.AreEqual(0, remainingHits);
        Assert.AreEqual(string.Empty, response.Body);
    }

    [TestMethod]
    public async Task StatusCodeReexecutionStillWorksForRemainingPipeline()
    {
        using var host = new PipelineTestHost();

        int remainingHits = 0;
        IApplicationBuilder app = host.CreateApplicationBuilder();

        app.UseStatusCodePagesWithReExecute("/errors/{0}");
        app.MapIsolated("/apps", _ => { });
        app.MapRemaining(remaining =>
        {
            remaining.Run(context =>
            {
                remainingHits++;

                if (context.Request.Path == "/errors/404")
                {
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    return context.Response.WriteAsync("shell error page");
                }

                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            });
        });

        PipelineTestResponse response = await host.ExecuteAsync(app.Build(), "/shell-missing");

        Assert.AreEqual(StatusCodes.Status200OK, response.StatusCode);
        Assert.AreEqual(2, remainingHits);
        Assert.AreEqual("shell error page", response.Body);
    }
}
