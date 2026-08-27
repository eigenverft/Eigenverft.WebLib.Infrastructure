using System.IO;
using System.Text;
using System.Threading.Tasks;

using Eigenverft.WebLib.Infrastructure.Hosting;

using Microsoft.AspNetCore.Http;

namespace Eigenverft.WebLib.Infrastructure.Tests;

[TestClass]
public sealed class HttpResponseExtensionsTests
{
    [TestMethod]
    public async Task WriteHtmlStatusResponseAsyncUsesAspNetCoreReasonPhrase()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await context.Response.WriteHtmlStatusResponseAsync(StatusCodes.Status404NotFound);

        string body = await ReadBodyAsync(context.Response.Body);
        Assert.AreEqual(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.AreEqual("text/html; charset=utf-8", context.Response.ContentType);
        StringAssert.Contains(body, "404 - Not Found");
    }

    [TestMethod]
    public async Task WriteHtmlStatusResponseAsyncDoesNotInventDescriptionForUnknownStatusCode()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await context.Response.WriteHtmlStatusResponseAsync(599);

        string body = await ReadBodyAsync(context.Response.Body);
        Assert.AreEqual(599, context.Response.StatusCode);
        StringAssert.Contains(body, ">599<");
        Assert.IsFalse(body.Contains("Unknown", System.StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string> ReadBodyAsync(Stream stream)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }
}
