using System;
using System.Threading.Tasks;

using Eigenverft.WebLib.Infrastructure.Hosting.Middleware.Infrastructure;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Eigenverft.WebLib.Infrastructure.Tests.Hosting.Middleware.Infrastructure;

[TestClass]
public sealed class ApplicationBuilderMiddlewareExtensionsTests
{
    [TestMethod]
    public async Task UseMiddlewareOnceDeduplicatesLinearPipeline()
    {
        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var app = new ApplicationBuilder(services);

        app.UseMiddlewareOnce<CountingMiddleware>();
        app.UseMiddlewareOnce<CountingMiddleware>();
        app.Run(static _ => Task.CompletedTask);

        RequestDelegate pipeline = app.Build();
        var context = new DefaultHttpContext();

        await pipeline(context);

        Assert.AreEqual(1, GetCount(context));
    }

    [TestMethod]
    public void FailedMiddlewareRegistrationDoesNotLeaveAOnceMarkerBehind()
    {
        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var app = new ApplicationBuilder(services);

        Assert.ThrowsExactly<InvalidOperationException>(() => app.UseMiddlewareOnce<InvalidMiddleware>());
        Assert.ThrowsExactly<InvalidOperationException>(() => app.UseMiddlewareOnce<InvalidMiddleware>());
    }

    [TestMethod]
    public async Task UseMiddlewareOnceDeduplicatesIndependentlyInsideNativeMapBranches()
    {
        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var app = new ApplicationBuilder(services);

        app.Map("/a", branch => ConfigureCountingBranch(branch));
        app.Map("/b", branch => ConfigureCountingBranch(branch));
        app.Run(static _ => Task.CompletedTask);

        RequestDelegate pipeline = app.Build();

        var first = new DefaultHttpContext();
        first.Request.Path = "/a";
        await pipeline(first);

        var second = new DefaultHttpContext();
        second.Request.Path = "/b";
        await pipeline(second);

        Assert.AreEqual(1, GetCount(first));
        Assert.AreEqual(1, GetCount(second));
    }

    [TestMethod]
    public async Task UseMiddlewareOnceMatchesAgreedMapIsolatedNonRejoiningBranchSemantics()
    {
        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var app = new ApplicationBuilder(services);

        MapIsolatedEquivalent(app, "/isolated-a", branch => ConfigureCountingBranch(branch));
        MapIsolatedEquivalent(app, "/isolated-b", branch => ConfigureCountingBranch(branch));
        app.Run(static _ => Task.CompletedTask);

        RequestDelegate pipeline = app.Build();

        var first = new DefaultHttpContext();
        first.Request.Path = "/isolated-a";
        await pipeline(first);

        var second = new DefaultHttpContext();
        second.Request.Path = "/isolated-b";
        await pipeline(second);

        Assert.AreEqual(1, GetCount(first));
        Assert.AreEqual(1, GetCount(second));
    }

    private static void ConfigureCountingBranch(IApplicationBuilder branch)
    {
        branch.UseMiddlewareOnce<CountingMiddleware>();
        branch.UseMiddlewareOnce<CountingMiddleware>();
        branch.Run(static _ => Task.CompletedTask);
    }

    private static IApplicationBuilder MapIsolatedEquivalent(
        IApplicationBuilder app,
        PathString path,
        Action<IApplicationBuilder> configure)
    {
        // WP6 owns the public MapIsolated API and has coordinated that it remains a thin native Map wrapper.
        return app.Map(path, preserveMatchedPathSegment: true, configure);
    }

    private static int GetCount(HttpContext context)
        => context.Items.TryGetValue(CountingMiddleware.CountKey, out object? value) && value is int count ? count : 0;

    private sealed class CountingMiddleware
    {
        public static readonly object CountKey = new();

        private readonly RequestDelegate _next;

        public CountingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task InvokeAsync(HttpContext context)
        {
            context.Items[CountKey] = GetCount(context) + 1;
            return _next(context);
        }
    }

    private sealed class InvalidMiddleware
    {
        public InvalidMiddleware(RequestDelegate next)
        {
        }
    }
}
