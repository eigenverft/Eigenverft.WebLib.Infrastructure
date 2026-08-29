using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;

using Eigenverft.WebLib.Infrastructure.Hosting.Hsts;
using Eigenverft.WebLib.Infrastructure.Hosting.Middleware.HealthProbeFaviconAware;
using Eigenverft.WebLib.Infrastructure.Hosting.RateLimiting;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Eigenverft.WebLib.Infrastructure.Tests;

[TestClass]
public sealed class WebLibHstsTests
{
    private const string HstsHeaderName = "Strict-Transport-Security";

    [TestMethod]
    public void DefaultsUse180DaysWithoutSubdomainsOrPreload()
    {
        using ServiceProvider provider = BuildProvider(Environments.Production);

        HstsOptions options = provider.GetRequiredService<IOptions<HstsOptions>>().Value;

        Assert.AreEqual(TimeSpan.FromDays(180), options.MaxAge);
        Assert.IsFalse(options.IncludeSubDomains);
        Assert.IsFalse(options.Preload);
    }

    [TestMethod]
    public void JsonCanOverrideNativeHstsOptions()
    {
        IConfiguration configuration = BuildConfiguration(
            new KeyValuePair<string, string?>("Hsts:MaxAge", "30.00:00:00"),
            new KeyValuePair<string, string?>("Hsts:IncludeSubDomains", "true"),
            new KeyValuePair<string, string?>("Hsts:Preload", "true"));

        using ServiceProvider provider = BuildProvider(Environments.Production, configuration);
        HstsOptions options = provider.GetRequiredService<IOptions<HstsOptions>>().Value;

        Assert.AreEqual(TimeSpan.FromDays(30), options.MaxAge);
        Assert.IsTrue(options.IncludeSubDomains);
        Assert.IsTrue(options.Preload);
    }

    [TestMethod]
    public void MissingJsonValuesLeaveWebLibAndNativeDefaultsIntact()
    {
        IConfiguration configuration = BuildConfiguration(
            new KeyValuePair<string, string?>("Hsts:IncludeSubDomains", "true"));

        using ServiceProvider provider = BuildProvider(Environments.Production, configuration);
        HstsOptions options = provider.GetRequiredService<IOptions<HstsOptions>>().Value;

        Assert.AreEqual(TimeSpan.FromDays(180), options.MaxAge);
        Assert.IsTrue(options.IncludeSubDomains);
        Assert.IsFalse(options.Preload);
    }

    [TestMethod]
    public void ConfigureLambdaOverridesJson()
    {
        IConfiguration configuration = BuildConfiguration(
            new KeyValuePair<string, string?>("Hsts:MaxAge", "30.00:00:00"),
            new KeyValuePair<string, string?>("Hsts:IncludeSubDomains", "true"),
            new KeyValuePair<string, string?>("Hsts:Preload", "false"));

        using ServiceProvider provider = BuildProvider(
            Environments.Production,
            configuration,
            options =>
            {
                options.MaxAge = TimeSpan.FromDays(45);
                options.IncludeSubDomains = false;
                options.Preload = true;
            });

        HstsOptions options = provider.GetRequiredService<IOptions<HstsOptions>>().Value;

        Assert.AreEqual(TimeSpan.FromDays(45), options.MaxAge);
        Assert.IsFalse(options.IncludeSubDomains);
        Assert.IsTrue(options.Preload);
    }

    [TestMethod]
    public async Task DevelopmentDoesNotActivateHsts()
    {
        using ServiceProvider provider = BuildProvider(Environments.Development);
        RequestDelegate pipeline = BuildPipeline(provider, app =>
        {
            app.UseWebLibHsts();
            app.Run(CompleteWithNoContentAsync);
        });

        TestResponse response = await ExecuteAsync(provider, pipeline, scheme: "https", host: "example.com");

        Assert.AreEqual(StatusCodes.Status204NoContent, response.StatusCode);
        Assert.AreEqual(string.Empty, response.HstsHeader);
    }

    [TestMethod]
    public async Task NonDevelopmentActivatesNativeHsts()
    {
        using ServiceProvider provider = BuildProvider(Environments.Production);
        RequestDelegate pipeline = BuildPipeline(provider, app =>
        {
            app.UseWebLibHsts();
            app.Run(CompleteWithNoContentAsync);
        });

        TestResponse response = await ExecuteAsync(provider, pipeline, scheme: "https", host: "example.com");

        Assert.AreEqual("max-age=15552000", response.HstsHeader);
    }

    [TestMethod]
    public async Task HttpResponseKeepsNativeNoHstsBehavior()
    {
        using ServiceProvider provider = BuildProvider(Environments.Production);
        RequestDelegate pipeline = BuildPipeline(provider, app =>
        {
            app.UseWebLibHsts();
            app.Run(CompleteWithNoContentAsync);
        });

        TestResponse response = await ExecuteAsync(provider, pipeline, scheme: "http", host: "example.com");

        Assert.AreEqual(string.Empty, response.HstsHeader);
    }

    [TestMethod]
    public async Task NativeExcludedHostSemanticsRemainInEffect()
    {
        using ServiceProvider provider = BuildProvider(Environments.Production);
        RequestDelegate pipeline = BuildPipeline(provider, app =>
        {
            app.UseWebLibHsts();
            app.Run(CompleteWithNoContentAsync);
        });

        TestResponse response = await ExecuteAsync(provider, pipeline, scheme: "https", host: "localhost");

        Assert.AreEqual(string.Empty, response.HstsHeader);
    }

    [TestMethod]
    public async Task HstsBeforeHealthProbeCoversShortCircuitedHealthResponse()
    {
        using ServiceProvider provider = BuildProvider(Environments.Production);
        RequestDelegate pipeline = BuildPipeline(provider, app =>
        {
            app.UseWebLibHsts();
            app.UseHealthProbeFaviconAware();
            app.Run(CompleteWithNoContentAsync);
        });

        TestResponse response = await ExecuteAsync(
            provider,
            pipeline,
            scheme: "https",
            host: "example.com",
            path: "/health");

        Assert.AreEqual(StatusCodes.Status200OK, response.StatusCode);
        Assert.AreEqual("max-age=15552000", response.HstsHeader);
    }

    [TestMethod]
    public async Task HstsBeforeRateLimiterCovers429Response()
    {
        using ServiceProvider provider = BuildProvider(
            Environments.Production,
            configureServices: services => services.AddRequestTrafficShaping(options =>
            {
                options.PerClient.BurstSize = 1;
                options.PerClient.RequestsPerSecond = 1;
                options.PerClient.QueueLimit = 0;
            }));

        RequestDelegate pipeline = BuildPipeline(provider, app =>
        {
            app.UseWebLibHsts();
            app.UseRateLimiter();
            app.Run(CompleteWithNoContentAsync);
        });

        TestResponse accepted = await ExecuteAsync(
            provider,
            pipeline,
            scheme: "https",
            host: "example.com",
            remoteIpAddress: "203.0.113.40");

        TestResponse rejected = await ExecuteAsync(
            provider,
            pipeline,
            scheme: "https",
            host: "example.com",
            remoteIpAddress: "203.0.113.40");

        Assert.AreEqual(StatusCodes.Status204NoContent, accepted.StatusCode);
        Assert.AreEqual(StatusCodes.Status429TooManyRequests, rejected.StatusCode);
        Assert.AreEqual("max-age=15552000", rejected.HstsHeader);
    }

    private static IConfiguration BuildConfiguration(params KeyValuePair<string, string?>[] values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        IConfiguration? configuration = null,
        Action<HstsOptions>? configure = null,
        Action<IServiceCollection>? configureServices = null)
    {
        configuration ??= new ConfigurationBuilder().Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment
        {
            EnvironmentName = environmentName,
        });

        if (configure is null)
        {
            services.AddWebLibHsts();
        }
        else
        {
            services.AddWebLibHsts(configure);
        }

        configureServices?.Invoke(services);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static RequestDelegate BuildPipeline(ServiceProvider provider, Action<IApplicationBuilder> configure)
    {
        var app = new ApplicationBuilder(provider);
        configure(app);
        return app.Build();
    }

    private static async Task<TestResponse> ExecuteAsync(
        ServiceProvider provider,
        RequestDelegate pipeline,
        string scheme,
        string host,
        string path = "/",
        string? remoteIpAddress = null)
    {
        using var body = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = provider,
        };

        context.Request.Method = HttpMethods.Get;
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        context.Request.Path = path;
        context.Response.Body = body;

        if (remoteIpAddress is not null)
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIpAddress);
        }

        await pipeline(context).ConfigureAwait(false);
        await context.Response.CompleteAsync().ConfigureAwait(false);

        return new TestResponse(
            context.Response.StatusCode,
            context.Response.Headers[HstsHeaderName].ToString());
    }

    private static Task CompleteWithNoContentAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status204NoContent;
        return context.Response.CompleteAsync();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = string.Empty;

        public string ApplicationName { get; set; } = typeof(WebLibHstsTests).Assembly.GetName().Name ?? string.Empty;

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestResponse
    {
        internal TestResponse(int statusCode, string hstsHeader)
        {
            StatusCode = statusCode;
            HstsHeader = hstsHeader;
        }

        internal int StatusCode { get; }

        internal string HstsHeader { get; }
    }
}
