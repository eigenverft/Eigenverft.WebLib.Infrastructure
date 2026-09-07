using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;

using Eigenverft.WebLib.Infrastructure.Hosting.Middleware.CanonicalHostRedirect;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Eigenverft.WebLib.Infrastructure.Tests;

[TestClass]
public sealed class CanonicalHostRedirectMiddlewareTests
{
    [TestMethod]
    public async Task HttpToHttpsAndApexToWwwUseSingleRedirectWithoutIncomingHttpPort()
    {
        bool nextCalled = false;
        using ServiceProvider services = CreateServices(options =>
        {
            options.PrimaryApexHost = "example.com";
        });

        var middleware = new CanonicalHostRedirectMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            services.GetRequiredService<IOptionsMonitor<CanonicalHostRedirectOptions>>());

        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("example.com", 8080);
        context.Request.PathBase = "/base";
        context.Request.Path = "/resource";
        context.Request.QueryString = new QueryString("?a=1");

        await middleware.InvokeAsync(context);

        Assert.IsFalse(nextCalled);
        Assert.AreEqual(StatusCodes.Status308PermanentRedirect, context.Response.StatusCode);
        Assert.AreEqual("https://www.example.com/base/resource?a=1", context.Response.Headers.Location.ToString());
    }

    [TestMethod]
    public async Task ConfiguredHttpsTargetPortReplacesIncomingHttpPort()
    {
        using ServiceProvider services = CreateServices(options =>
        {
            options.PrimaryApexHost = "example.com";
            options.Canonicalization = CanonicalHostMode.ToApex;
            options.HttpsTargetPort = 8443;
        });

        var middleware = new CanonicalHostRedirectMiddleware(
            _ => Task.CompletedTask,
            services.GetRequiredService<IOptionsMonitor<CanonicalHostRedirectOptions>>());

        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("example.com", 8080);
        context.Request.Path = "/healthz";

        await middleware.InvokeAsync(context);

        Assert.AreEqual("https://example.com:8443/healthz", context.Response.Headers.Location.ToString());
    }

    [TestMethod]
    public async Task AlternateIncomingHttpsPortIsNormalizedToImplicit443()
    {
        using ServiceProvider services = CreateServices(options =>
        {
            options.PrimaryApexHost = "example.com";
        });

        var middleware = new CanonicalHostRedirectMiddleware(
            _ => Task.CompletedTask,
            services.GetRequiredService<IOptionsMonitor<CanonicalHostRedirectOptions>>());

        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("www.example.com", 8443);
        context.Request.Path = "/secure";

        await middleware.InvokeAsync(context);

        Assert.AreEqual("https://www.example.com/secure", context.Response.Headers.Location.ToString());
    }

    [TestMethod]
    public async Task AliasAndSchemeAreNormalizedInTheSameRedirect()
    {
        using ServiceProvider services = CreateServices(options =>
        {
            options.PrimaryApexHost = "example.com";
            options.RedirectFromHosts = ["legacy.example.net"];
        });

        var middleware = new CanonicalHostRedirectMiddleware(
            _ => Task.CompletedTask,
            services.GetRequiredService<IOptionsMonitor<CanonicalHostRedirectOptions>>());

        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("legacy.example.net", 8080);
        context.Request.Path = "/docs";
        context.Request.QueryString = new QueryString("?page=2");

        await middleware.InvokeAsync(context);

        Assert.AreEqual("https://www.example.com/docs?page=2", context.Response.Headers.Location.ToString());
    }

    [TestMethod]
    public void RegistrationBindsTheOptionalHttpsTargetPortFromConfiguration()
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationManager();
        configuration.AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
        {
            ["CanonicalHostRedirect:PrimaryApexHost"] = "example.com",
            ["CanonicalHostRedirect:HttpsTargetPort"] = "8443",
        });

        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
        services.AddCanonicalHostRedirect();

        using ServiceProvider provider = services.BuildServiceProvider();
        CanonicalHostRedirectOptions options = provider
            .GetRequiredService<IOptionsMonitor<CanonicalHostRedirectOptions>>()
            .CurrentValue;

        Assert.AreEqual("example.com", options.PrimaryApexHost);
        Assert.AreEqual(8443, options.HttpsTargetPort);
    }

    [TestMethod]
    public void RegistrationRejectsPortsEmbeddedInCanonicalHostConfiguration()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
        {
            ["CanonicalHostRedirect:PrimaryApexHost"] = "example.com:8443",
        });

        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
        services.AddCanonicalHostRedirect();

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.ThrowsExactly<OptionsValidationException>(() =>
        {
            _ = provider.GetRequiredService<IOptionsMonitor<CanonicalHostRedirectOptions>>().CurrentValue;
        });
    }

    [TestMethod]
    public void RegistrationRejectsUnsupportedCanonicalization()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
        {
            ["CanonicalHostRedirect:Canonicalization"] = "0",
        });

        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
        services.AddCanonicalHostRedirect();

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.ThrowsExactly<OptionsValidationException>(() =>
        {
            _ = provider.GetRequiredService<IOptionsMonitor<CanonicalHostRedirectOptions>>().CurrentValue;
        });
    }

    [TestMethod]
    public void UseCanonicalHostRedirectWithoutRegistrationReportsMatchingAddCall()
    {
        var services = new ServiceCollection();
        services.AddOptions();

        using ServiceProvider provider = services.BuildServiceProvider();
        var app = new ApplicationBuilder(provider);

        InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            app.UseCanonicalHostRedirect());

        StringAssert.Contains(exception.Message, "AddCanonicalHostRedirect()", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task MapBranchesCanUseIndependentCanonicalRedirectOverrides()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
        {
            ["CanonicalHostRedirect:PrimaryApexHost"] = "example.com",
        });

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCanonicalHostRedirect(options =>
        {
            options.Canonicalization = CanonicalHostMode.ToWww;
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        var app = new ApplicationBuilder(provider);

        app.Map("/internal", branch =>
        {
            branch.UseCanonicalHostRedirect(options =>
            {
                options.Canonicalization = CanonicalHostMode.ToApex;
            });

            branch.Run(context =>
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            });
        });

        app.Map("/public", branch =>
        {
            branch.UseCanonicalHostRedirect();
            branch.Run(context =>
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            });
        });

        RequestDelegate pipeline = app.Build();

        var internalContext = new DefaultHttpContext();
        internalContext.RequestServices = provider;
        internalContext.Request.Scheme = "https";
        internalContext.Request.Host = new HostString("example.com");
        internalContext.Request.Path = "/internal";
        await pipeline(internalContext);

        Assert.AreEqual(StatusCodes.Status204NoContent, internalContext.Response.StatusCode);
        Assert.IsFalse(internalContext.Response.Headers.ContainsKey("Location"));

        var publicContext = new DefaultHttpContext();
        publicContext.RequestServices = provider;
        publicContext.Request.Scheme = "https";
        publicContext.Request.Host = new HostString("example.com");
        publicContext.Request.Path = "/public";
        await pipeline(publicContext);

        Assert.AreEqual(StatusCodes.Status308PermanentRedirect, publicContext.Response.StatusCode);
        Assert.AreEqual(
            "https://www.example.com/public",
            publicContext.Response.Headers.Location.ToString());
    }

    [TestMethod]
    public async Task LocalCanonicalRedirectOverrideKeepsConfiguredBaselineValues()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
        {
            ["CanonicalHostRedirect:PrimaryApexHost"] = "example.com",
            ["CanonicalHostRedirect:RedirectFromHosts:0"] = "legacy.example.com",
        });

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCanonicalHostRedirect(options =>
        {
            options.Canonicalization = CanonicalHostMode.ToWww;
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        var app = new ApplicationBuilder(provider);
        app.UseCanonicalHostRedirect(options =>
        {
            options.HttpsTargetPort = 8443;
        });

        RequestDelegate pipeline = app.Build();
        var context = new DefaultHttpContext();
        context.RequestServices = provider;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("legacy.example.com");

        await pipeline(context);

        Assert.AreEqual(StatusCodes.Status308PermanentRedirect, context.Response.StatusCode);
        Assert.AreEqual(
            "https://www.example.com:8443/",
            context.Response.Headers.Location.ToString());

        CanonicalHostRedirectOptions global = provider
            .GetRequiredService<IOptionsMonitor<CanonicalHostRedirectOptions>>()
            .CurrentValue;
        Assert.IsNull(global.HttpsTargetPort);
        CollectionAssert.AreEqual(new[] { "legacy.example.com" }, global.RedirectFromHosts);
    }

    [TestMethod]
    public async Task ForwardedHeadersAppliedFirstPreventAnInternalSchemeAndHostRedirect()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationManager());
        services.AddCanonicalHostRedirect(options =>
        {
            options.PrimaryApexHost = "example.com";
        });
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto;
            options.KnownProxies.Add(IPAddress.Loopback);
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        var app = new ApplicationBuilder(provider);
        app.UseForwardedHeaders();
        app.UseCanonicalHostRedirect();

        bool terminalCalled = false;
        app.Run(context =>
        {
            terminalCalled = true;
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });

        RequestDelegate pipeline = app.Build();
        var context = new DefaultHttpContext();
        context.RequestServices = provider;
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("internal.local", 8080);
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["X-Forwarded-Host"] = "www.example.com";
        context.Response.Body = new MemoryStream();

        await pipeline(context);

        Assert.IsTrue(terminalCalled);
        Assert.AreEqual(StatusCodes.Status204NoContent, context.Response.StatusCode);
        Assert.IsFalse(context.Response.Headers.ContainsKey("Location"));
        Assert.AreEqual("https", context.Request.Scheme);
        Assert.AreEqual("www.example.com", context.Request.Host.Value);
    }

    private static ServiceProvider CreateServices(Action<CanonicalHostRedirectOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure(configure);
        return services.BuildServiceProvider();
    }
}
