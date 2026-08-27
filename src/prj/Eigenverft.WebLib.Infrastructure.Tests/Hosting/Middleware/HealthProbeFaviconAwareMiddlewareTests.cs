using System.IO;
using System.Text;
using System.Threading.Tasks;

using Eigenverft.WebLib.Infrastructure.Hosting.Middleware.HealthProbeFaviconAware;

using Microsoft.AspNetCore.Http;

namespace Eigenverft.WebLib.Infrastructure.Tests;

[TestClass]
public sealed class HealthProbeFaviconAwareMiddlewareTests
{
    [TestMethod]
    public async Task GetHealthShortCircuitsWithOkBodyAndNoCacheHeaders()
    {
        bool nextCalled = false;
        var middleware = new HealthProbeFaviconAwareMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/health";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.IsFalse(nextCalled);
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.AreEqual("text/plain; charset=utf-8", context.Response.ContentType);
        Assert.AreEqual("no-store, no-cache", context.Response.Headers.CacheControl.ToString());
        Assert.AreEqual("no-cache", context.Response.Headers.Pragma.ToString());
        Assert.AreEqual("OK", await ReadBodyAsync(context.Response.Body));
    }

    [TestMethod]
    public async Task HeadHealthShortCircuitsWithoutBody()
    {
        bool nextCalled = false;
        var middleware = new HealthProbeFaviconAwareMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Head;
        context.Request.Path = "/HEALTH";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.IsFalse(nextCalled);
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.AreEqual(string.Empty, await ReadBodyAsync(context.Response.Body));
    }

    [TestMethod]
    public async Task NonGetOrHeadHealthRequestContinuesPipeline()
    {
        bool nextCalled = false;
        var middleware = new HealthProbeFaviconAwareMiddleware(context =>
        {
            nextCalled = true;
            context.Response.StatusCode = StatusCodes.Status418ImATeapot;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/health";

        await middleware.InvokeAsync(context);

        Assert.IsTrue(nextCalled);
        Assert.AreEqual(StatusCodes.Status418ImATeapot, context.Response.StatusCode);
    }

    [TestMethod]
    public async Task FaviconFromHealthRefererIsSwallowedWithNoContent()
    {
        bool nextCalled = false;
        var middleware = new HealthProbeFaviconAwareMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/favicon.ico";
        context.Request.Headers.Referer = "https://example.com/health?browser=1";

        await middleware.InvokeAsync(context);

        Assert.IsFalse(nextCalled);
        Assert.AreEqual(StatusCodes.Status204NoContent, context.Response.StatusCode);
    }

    [TestMethod]
    public async Task FaviconWithoutHealthRefererContinuesPipeline()
    {
        bool nextCalled = false;
        var middleware = new HealthProbeFaviconAwareMiddleware(context =>
        {
            nextCalled = true;
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/favicon.ico";
        context.Request.Headers.Referer = "https://example.com/other/health-info";

        await middleware.InvokeAsync(context);

        Assert.IsTrue(nextCalled);
        Assert.AreEqual(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    private static async Task<string> ReadBodyAsync(Stream stream)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }
}
