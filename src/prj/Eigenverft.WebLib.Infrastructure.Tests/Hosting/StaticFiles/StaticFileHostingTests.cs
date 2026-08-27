using System.Threading.Tasks;

using Eigenverft.WebLib.Infrastructure.Hosting.Pipeline;
using Eigenverft.WebLib.Infrastructure.Hosting.StaticFiles;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;

namespace Eigenverft.WebLib.Infrastructure.Tests;

[TestClass]
public sealed class StaticFileHostingTests
{
    [TestMethod]
    public void WebAppMappingsContainOnlyLegacyExtensionsMissingFromFrameworkDefaults()
    {
        Assert.AreEqual(2, AdditionalMappings.WebApp.Mappings.Count);
        Assert.AreEqual("application/octet-stream", AdditionalMappings.WebApp.Mappings[".br"]);
        Assert.AreEqual("application/octet-stream", AdditionalMappings.WebApp.Mappings[".dat"]);
        Assert.IsFalse(AdditionalMappings.WebApp.Mappings.ContainsKey(".webmanifest"));
        Assert.IsFalse(AdditionalMappings.WebApp.Mappings.ContainsKey(".wasm"));

        FileExtensionContentTypeProvider provider =
            StaticFileContentTypeProviderFactory.Create(AdditionalMappings.WebApp);

        Assert.AreEqual("application/manifest+json", provider.Mappings[".webmanifest"]);
        Assert.AreEqual("application/wasm", provider.Mappings[".wasm"]);
        Assert.AreEqual("text/html", provider.Mappings[".html"]);
        Assert.AreEqual("application/octet-stream", provider.Mappings[".br"]);
        Assert.AreEqual("application/octet-stream", provider.Mappings[".dat"]);
    }

    [TestMethod]
    public void MediaMappingsBackfillOnlyFrameworksThatNeedAvif()
    {
#if NET8_0
        Assert.AreEqual(1, AdditionalMappings.Media.Mappings.Count);
        Assert.AreEqual("image/avif", AdditionalMappings.Media.Mappings[".avif"]);
#else
        Assert.AreEqual(0, AdditionalMappings.Media.Mappings.Count);
#endif

        FileExtensionContentTypeProvider provider =
            StaticFileContentTypeProviderFactory.Create(AdditionalMappings.Media);

        Assert.AreEqual("image/avif", provider.Mappings[".avif"]);
        Assert.AreEqual("text/html", provider.Mappings[".html"]);
    }

    [TestMethod]
    public async Task UseStaticFilesServesAdditionalAndFrameworkDefaultMappingsInsideIsolatedBranch()
    {
        using var host = new PipelineTestHost();
        host.WriteFile("downloads/archive.br", "brotli-payload");
        host.WriteFile("downloads/readme.html", "<p>default mapping</p>");

        bool remainingHit = false;
        IApplicationBuilder app = host.CreateApplicationBuilder();

        app.MapIsolated("/downloads", downloads =>
        {
            downloads.UseStaticFiles(AdditionalMappings.WebApp);
        });

        app.MapRemaining(remaining =>
        {
            remaining.Run(context =>
            {
                remainingHit = true;
                return context.Response.WriteAsync("shell");
            });
        });

        RequestDelegate pipeline = app.Build();

        PipelineTestResponse compressed = await host.ExecuteAsync(pipeline, "/downloads/archive.br");
        Assert.AreEqual(StatusCodes.Status200OK, compressed.StatusCode);
        Assert.AreEqual("application/octet-stream", compressed.ContentType);
        StringAssert.Contains(compressed.Body, "brotli-payload");
        Assert.IsFalse(remainingHit);

        PipelineTestResponse html = await host.ExecuteAsync(pipeline, "/downloads/readme.html");
        Assert.AreEqual(StatusCodes.Status200OK, html.StatusCode);
        Assert.IsTrue(html.ContentType?.StartsWith("text/html") == true);
        StringAssert.Contains(html.Body, "default mapping");
        Assert.IsFalse(remainingHit);
    }

    [TestMethod]
    public async Task UsePwaHostDefaultServesDefaultFileAndWebAppMappingsWithinPreservedIsolatedPath()
    {
        using var host = new PipelineTestHost();
        host.WriteFile("apps/index.html", "<main>PWA root</main>");
        host.WriteFile("apps/boot.dat", "boot-payload");

        IApplicationBuilder app = host.CreateApplicationBuilder();
        app.MapIsolated("/apps", apps => apps.UsePwaHost());
        app.MapRemaining(remaining => remaining.Run(context => context.Response.WriteAsync("shell")));

        RequestDelegate pipeline = app.Build();

        PipelineTestResponse root = await host.ExecuteAsync(pipeline, "/apps/");
        Assert.AreEqual(StatusCodes.Status200OK, root.StatusCode);
        Assert.IsTrue(root.ContentType?.StartsWith("text/html") == true);
        StringAssert.Contains(root.Body, "PWA root");

        PipelineTestResponse mapped = await host.ExecuteAsync(pipeline, "/apps/boot.dat");
        Assert.AreEqual(StatusCodes.Status200OK, mapped.StatusCode);
        Assert.AreEqual("application/octet-stream", mapped.ContentType);
        StringAssert.Contains(mapped.Body, "boot-payload");
    }

    [TestMethod]
    public async Task MissingPwaFileReturns404WithoutFallingIntoRemainingPipeline()
    {
        using var host = new PipelineTestHost();
        host.WriteFile("apps/index.html", "<main>PWA root</main>");

        bool remainingHit = false;
        IApplicationBuilder app = host.CreateApplicationBuilder();

        app.MapIsolated("/apps", apps => apps.UsePwaHost());
        app.MapRemaining(remaining =>
        {
            remaining.Run(context =>
            {
                remainingHit = true;
                context.Response.StatusCode = StatusCodes.Status200OK;
                return context.Response.WriteAsync("shell fallback");
            });
        });

        PipelineTestResponse response = await host.ExecuteAsync(app.Build(), "/apps/missing.js");

        Assert.AreEqual(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.IsFalse(remainingHit);
        Assert.AreEqual(string.Empty, response.Body);
    }
}
