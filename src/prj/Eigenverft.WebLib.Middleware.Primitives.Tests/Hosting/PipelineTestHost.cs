using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Eigenverft.WebLib.Infrastructure.Tests;

internal sealed class PipelineTestHost : IDisposable
{
    private readonly PhysicalFileProvider _contentRootFileProvider;
    private readonly PhysicalFileProvider _webRootFileProvider;
    private readonly ServiceProvider _services;

    internal PipelineTestHost()
    {
        WebRootPath = Path.Combine(
            Path.GetTempPath(),
            "Eigenverft.WebLib.Infrastructure.Tests",
            Guid.NewGuid().ToString("N"),
            "wwwroot");

        Directory.CreateDirectory(WebRootPath);
        _contentRootFileProvider = new PhysicalFileProvider(Path.GetDirectoryName(WebRootPath)!);
        _webRootFileProvider = new PhysicalFileProvider(WebRootPath);

        var environment = new TestWebHostEnvironment
        {
            ApplicationName = typeof(PipelineTestHost).Assembly.GetName().Name ?? "Eigenverft.WebLib.Infrastructure.Tests",
            EnvironmentName = Environments.Development,
            ContentRootPath = Path.GetDirectoryName(WebRootPath)!,
            ContentRootFileProvider = _contentRootFileProvider,
            WebRootPath = WebRootPath,
            WebRootFileProvider = _webRootFileProvider,
        };

        _services = new ServiceCollection()
            .AddLogging()
            .AddOptions()
            .AddSingleton<IWebHostEnvironment>(environment)
            .AddSingleton<IHostEnvironment>(environment)
            .BuildServiceProvider();
    }

    internal string WebRootPath { get; }

    internal IApplicationBuilder CreateApplicationBuilder()
    {
        return new ApplicationBuilder(_services);
    }

    internal void WriteFile(string relativePath, string content)
    {
        string fullPath = Path.Combine(WebRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        string? parent = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(fullPath, content, Encoding.UTF8);
    }

    internal async Task<PipelineTestResponse> ExecuteAsync(RequestDelegate pipeline, string path)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = _services,
        };

        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        await pipeline(context).ConfigureAwait(false);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        string body = await reader.ReadToEndAsync().ConfigureAwait(false);

        return new PipelineTestResponse(
            context.Response.StatusCode,
            context.Response.ContentType,
            body,
            context.Response.Headers.Location.ToString());
    }

    public void Dispose()
    {
        _services.Dispose();
        _webRootFileProvider.Dispose();
        _contentRootFileProvider.Dispose();

        string contentRoot = Path.GetDirectoryName(WebRootPath)!;
        if (Directory.Exists(contentRoot))
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = string.Empty;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = string.Empty;

        public string EnvironmentName { get; set; } = string.Empty;

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

internal sealed class PipelineTestResponse
{
    internal PipelineTestResponse(int statusCode, string? contentType, string body, string location)
    {
        StatusCode = statusCode;
        ContentType = contentType;
        Body = body;
        Location = location;
    }

    internal int StatusCode { get; }

    internal string? ContentType { get; }

    internal string Body { get; }

    internal string Location { get; }
}
