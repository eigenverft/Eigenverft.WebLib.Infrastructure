using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Eigenverft.WebLib.Infrastructure.Hosting.Middleware.ClientNetwork;
using Eigenverft.WebLib.Infrastructure.Hosting.RequestTrafficLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;

namespace Eigenverft.WebLib.Infrastructure.Tests.Hosting.RequestTrafficLogging;

[TestClass]
public sealed class RequestTrafficLoggingTests
{
    [TestMethod]
    public async Task NormalCompletion_EmitsExactlyOneCoreTrafficRecord()
    {
        using var host = new RequestTrafficLoggingTestHost();
        RequestDelegate pipeline = host.BuildPipeline(app =>
            app.Run(context =>
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            }), useTrafficLoggingTwice: true);

        DefaultHttpContext context = host.CreateContext("/items/42");
        context.Request.PathBase = "/api";
        context.SetEndpoint(CreateRouteEndpoint("GET /items/{id}", "/items/{id}"));

        await pipeline(context);

        CapturedLogRecord record = host.SingleTrafficRecord();
        Assert.AreEqual("Completed", record.GetProperty("Outcome"));
        Assert.AreEqual("GET", record.GetProperty("Method"));
        Assert.AreEqual("https", record.GetProperty("Scheme"));
        Assert.AreEqual("example.test:8443", record.GetProperty("Host"));
        Assert.AreEqual("/api", record.GetProperty("PathBase")?.ToString());
        Assert.AreEqual("/items/42", record.GetProperty("Path")?.ToString());
        Assert.AreEqual("HTTP/1.1", record.GetProperty("Protocol"));
        Assert.AreEqual(StatusCodes.Status204NoContent, record.GetProperty("StatusCode"));
        Assert.AreEqual("203.0.113.7", record.GetProperty("RemoteIpAddress"));
        Assert.AreEqual(false, record.GetProperty("Aborted"));
        Assert.IsTrue((double)record.GetProperty("DurationMs")! >= 0D);
        Assert.IsInstanceOfType<DateTimeOffset>(record.GetProperty("TimestampUtc"));
        Assert.AreEqual("trace-test-123", record.GetProperty("TraceId"));
        Assert.AreEqual("GET /items/{id}", record.GetProperty("Endpoint"));
        Assert.AreEqual("/items/{id}", record.GetProperty("RoutePattern"));
        Assert.IsNull(record.GetProperty("ExceptionType"));
    }

    [TestMethod]
    public async Task Scanner404_DefaultCoreShowsWhoAskedForWhat()
    {
        using var host = new RequestTrafficLoggingTestHost();
        RequestDelegate pipeline = host.BuildPipeline(app =>
            app.Run(context =>
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }));

        DefaultHttpContext context = host.CreateContext("/.env");
        context.Request.Method = HttpMethods.Head;
        context.Request.Headers.UserAgent = "scanner/1.0";

        await pipeline(context);

        CapturedLogRecord record = host.SingleTrafficRecord();
        Assert.AreEqual("Completed", record.GetProperty("Outcome"));
        Assert.AreEqual("203.0.113.7", record.GetProperty("RemoteIpAddress"));
        Assert.AreEqual("HEAD", record.GetProperty("Method"));
        Assert.AreEqual("example.test:8443", record.GetProperty("Host"));
        Assert.AreEqual("/.env", record.GetProperty("Path")?.ToString());
        Assert.AreEqual("scanner/1.0", record.GetProperty("UserAgent"));
        Assert.AreEqual(StatusCodes.Status404NotFound, record.GetProperty("StatusCode"));
    }

    [TestMethod]
    public async Task UnhandledException_IsFaultedAndRethrown()
    {
        using var host = new RequestTrafficLoggingTestHost();
        RequestDelegate pipeline = host.BuildPipeline(app =>
            app.Run(static _ => throw new InvalidOperationException("boom")));
        DefaultHttpContext context = host.CreateContext();

        InvalidOperationException? thrown = null;
        try
        {
            await pipeline(context);
        }
        catch (InvalidOperationException exception)
        {
            thrown = exception;
        }

        Assert.IsNotNull(thrown, "The application exception must propagate out of traffic logging.");
        CapturedLogRecord record = host.SingleTrafficRecord();
        Assert.AreEqual("Faulted", record.GetProperty("Outcome"));
        Assert.AreEqual(typeof(InvalidOperationException).FullName, record.GetProperty("ExceptionType"));
    }

    [TestMethod]
    public async Task HandledException_UsesFinalStatusAndFaultedOutcome()
    {
        using var host = new RequestTrafficLoggingTestHost();
        RequestDelegate pipeline = host.BuildPipeline(app =>
        {
            app.UseExceptionHandler(errorApp =>
                errorApp.Run(context =>
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    return Task.CompletedTask;
                }));
            app.Run(static _ => throw new InvalidOperationException("handled"));
        });

        DefaultHttpContext context = host.CreateContext();
        await pipeline(context);

        CapturedLogRecord record = host.SingleTrafficRecord();
        Assert.AreEqual("Faulted", record.GetProperty("Outcome"));
        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, record.GetProperty("StatusCode"));
        Assert.AreEqual(typeof(InvalidOperationException).FullName, record.GetProperty("ExceptionType"));
    }

    [TestMethod]
    public async Task ClientAbort_IsAbortedAndCancellationStillPropagates()
    {
        using var host = new RequestTrafficLoggingTestHost();
        using var cancellation = new CancellationTokenSource();
        RequestDelegate pipeline = host.BuildPipeline(app =>
            app.Run(context =>
            {
                cancellation.Cancel();
                return Task.FromException(new OperationCanceledException(context.RequestAborted));
            }));
        DefaultHttpContext context = host.CreateContext();
        context.RequestAborted = cancellation.Token;

        OperationCanceledException? thrown = null;
        try
        {
            await pipeline(context);
        }
        catch (OperationCanceledException exception)
        {
            thrown = exception;
        }

        Assert.IsNotNull(thrown);
        CapturedLogRecord record = host.SingleTrafficRecord();
        Assert.AreEqual("Aborted", record.GetProperty("Outcome"));
        Assert.AreEqual(true, record.GetProperty("Aborted"));
    }

    [TestMethod]
    public async Task LongRunningStartedResponse_CanFinishAsAbortedWithStatus200()
    {
        using var host = new RequestTrafficLoggingTestHost();
        using var cancellation = new CancellationTokenSource();
        StartedResponseFeature? startedFeature = null;

        RequestDelegate pipeline = host.BuildPipeline(app =>
            app.Run(async context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "text/event-stream";
                await context.Response.WriteAsync("data: ready\n\n", context.RequestAborted);
                startedFeature!.HasStartedValue = true;
                cancellation.Cancel();
                throw new OperationCanceledException(context.RequestAborted);
            }));

        DefaultHttpContext context = host.CreateContext("/events");
        context.RequestAborted = cancellation.Token;
        startedFeature = new StartedResponseFeature(context.Features.GetRequiredFeature<IHttpResponseFeature>());
        context.Features.Set<IHttpResponseFeature>(startedFeature);

        try
        {
            await pipeline(context);
            Assert.Fail("Expected the simulated client abort to propagate.");
        }
        catch (OperationCanceledException)
        {
        }

        CapturedLogRecord record = host.SingleTrafficRecord();
        Assert.AreEqual("Aborted", record.GetProperty("Outcome"));
        Assert.AreEqual(StatusCodes.Status200OK, record.GetProperty("StatusCode"));
        Assert.AreEqual(true, record.GetProperty("ResponseStarted"));
        Assert.AreEqual(true, record.GetProperty("Aborted"));
    }

    [TestMethod]
    public async Task Query_CanBeEnabledOrLeftOut()
    {
        using (var host = new RequestTrafficLoggingTestHost())
        {
            RequestDelegate pipeline = host.BuildPipeline(app => app.Run(static _ => Task.CompletedTask));
            DefaultHttpContext context = host.CreateContext();
            context.Request.QueryString = new QueryString("?token=secret");
            await pipeline(context);
            Assert.IsFalse(host.SingleTrafficRecord().TryGetProperty("QueryString", out _));
        }

        using (var host = new RequestTrafficLoggingTestHost(options =>
               options.Fields |= RequestTrafficLoggingFields.Query))
        {
            RequestDelegate pipeline = host.BuildPipeline(app => app.Run(static _ => Task.CompletedTask));
            DefaultHttpContext context = host.CreateContext();
            context.Request.QueryString = new QueryString("?token=secret");
            await pipeline(context);
            Assert.AreEqual("?token=secret", host.SingleTrafficRecord().GetProperty("QueryString"));
        }
    }

    [TestMethod]
    public async Task RequestHeaders_CanBeEnabledOrLeftOut()
    {
        using (var host = new RequestTrafficLoggingTestHost())
        {
            RequestDelegate pipeline = host.BuildPipeline(app => app.Run(static _ => Task.CompletedTask));
            DefaultHttpContext context = host.CreateContext();
            context.Request.Headers["X-Correlation"] = "abc";
            await pipeline(context);
            Assert.IsFalse(host.SingleTrafficRecord().TryGetProperty("X-Correlation", out _));
        }

        using (var host = new RequestTrafficLoggingTestHost(options =>
               options.Fields |= RequestTrafficLoggingFields.RequestHeaders))
        {
            RequestDelegate pipeline = host.BuildPipeline(app => app.Run(static _ => Task.CompletedTask));
            DefaultHttpContext context = host.CreateContext();
            context.Request.Headers["X-Correlation"] = "abc";
            await pipeline(context);
            Assert.AreEqual("[Redacted]", host.SingleTrafficRecord().GetProperty("X-Correlation"));
        }
    }

    [TestMethod]
    public async Task ResponseHeaders_CanBeEnabledOrLeftOut()
    {
        using (var host = new RequestTrafficLoggingTestHost())
        {
            RequestDelegate pipeline = host.BuildPipeline(app => app.Run(context =>
            {
                context.Response.Headers["X-Node"] = "n1";
                return Task.CompletedTask;
            }));
            await pipeline(host.CreateContext());
            Assert.IsFalse(host.SingleTrafficRecord().TryGetProperty("X-Node", out _));
        }

        using (var host = new RequestTrafficLoggingTestHost(options =>
               options.Fields |= RequestTrafficLoggingFields.ResponseHeaders))
        {
            RequestDelegate pipeline = host.BuildPipeline(app => app.Run(context =>
            {
                context.Response.Headers["X-Node"] = "n1";
                return Task.CompletedTask;
            }));
            await pipeline(host.CreateContext());
            Assert.AreEqual("[Redacted]", host.SingleTrafficRecord().GetProperty("X-Node"));
        }
    }

    [TestMethod]
    public async Task SensitiveRequestHeader_Redact_HidesValue()
    {
        using var host = CreateSensitiveHeaderHost(SensitiveValueMode.Redact);
        RequestDelegate pipeline = host.BuildPipeline(app => app.Run(static _ => Task.CompletedTask));
        DefaultHttpContext context = host.CreateContext();
        context.Request.Headers.Authorization = "Bearer secret-token";

        await pipeline(context);

        CapturedLogRecord record = host.SingleTrafficRecord();
        Assert.AreEqual("[Redacted]", record.GetProperty("Authorization"));
        Assert.IsFalse(record.TryGetProperty("RequestHeader.AuthorizationHash", out _));
    }

    [TestMethod]
    public async Task SensitiveRequestHeader_Hash_RedactsAndFingerprintsValue()
    {
        const string value = "Bearer secret-token";
        using var host = CreateSensitiveHeaderHost(SensitiveValueMode.Hash);
        RequestDelegate pipeline = host.BuildPipeline(app => app.Run(static _ => Task.CompletedTask));
        DefaultHttpContext context = host.CreateContext();
        context.Request.Headers.Authorization = value;

        await pipeline(context);

        CapturedLogRecord record = host.SingleTrafficRecord();
        Assert.AreEqual("[Redacted]", record.GetProperty("Authorization"));
        Assert.AreEqual(Hash(value), record.GetProperty("RequestHeader.AuthorizationHash"));
    }

    [TestMethod]
    public async Task SensitiveRequestHeader_Include_ExposesCompleteValue()
    {
        const string value = "Bearer secret-token";
        using var host = CreateSensitiveHeaderHost(SensitiveValueMode.Include);
        RequestDelegate pipeline = host.BuildPipeline(app => app.Run(static _ => Task.CompletedTask));
        DefaultHttpContext context = host.CreateContext();
        context.Request.Headers.Authorization = value;

        await pipeline(context);

        Assert.AreEqual(value, host.SingleTrafficRecord().GetProperty("Authorization"));
    }

    [TestMethod]
    public async Task SensitiveResponseHeader_Hash_RedactsAndFingerprintsValue()
    {
        const string value = "session=secret";
        using var host = new RequestTrafficLoggingTestHost(options =>
        {
            options.Fields |= RequestTrafficLoggingFields.ResponseHeaders;
            options.SensitiveValueMode = SensitiveValueMode.Hash;
        });
        RequestDelegate pipeline = host.BuildPipeline(app => app.Run(context =>
        {
            context.Response.Headers.SetCookie = value;
            return Task.CompletedTask;
        }));

        await pipeline(host.CreateContext());

        CapturedLogRecord record = host.SingleTrafficRecord();
        Assert.AreEqual("[Redacted]", record.GetProperty("Set-Cookie"));
        Assert.AreEqual(Hash(value), record.GetProperty("ResponseHeader.Set-CookieHash"));
    }

    [TestMethod]
    public async Task RequestBody_UsesFrameworkCaptureLimitAndMakesTruncationExplicit()
    {
        using var host = new RequestTrafficLoggingTestHost(options =>
        {
            options.Fields |= RequestTrafficLoggingFields.RequestBody;
            options.RequestBodyLimit = 4;
        });
        RequestDelegate pipeline = host.BuildPipeline(app => app.Run(async context =>
        {
            await context.Request.Body.CopyToAsync(Stream.Null);
        }));
        DefaultHttpContext context = host.CreateContext();
        byte[] body = Encoding.UTF8.GetBytes("abcdefgh");
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = body.Length;
        context.Request.Body = new MemoryStream(body);

        await pipeline(context);

        CapturedLogRecord record = host.SingleTrafficRecord();
        Assert.AreEqual("abcd", record.GetProperty("RequestBody"));
        Assert.AreEqual(4L, record.GetProperty("RequestBodyCapturedBytes"));
        Assert.AreEqual(8L, record.GetProperty("RequestBodyTotalBytes"));
        Assert.AreEqual(true, record.GetProperty("RequestBodyTruncated"));
    }

    [TestMethod]
    public async Task ResponseBody_UsesFrameworkCaptureLimitAndMakesTruncationExplicit()
    {
        using var host = new RequestTrafficLoggingTestHost(options =>
        {
            options.Fields |= RequestTrafficLoggingFields.ResponseBody;
            options.ResponseBodyLimit = 4;
        });
        RequestDelegate pipeline = host.BuildPipeline(app => app.Run(async context =>
        {
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("abcdefgh");
        }));

        await pipeline(host.CreateContext());

        CapturedLogRecord record = host.SingleTrafficRecord();
        Assert.AreEqual("abcd", record.GetProperty("ResponseBody"));
        Assert.AreEqual(4L, record.GetProperty("ResponseBodyCapturedBytes"));
        Assert.AreEqual(8L, record.GetProperty("ResponseBodyTotalBytes"));
        Assert.AreEqual(true, record.GetProperty("ResponseBodyTruncated"));
    }

    [TestMethod]
    public async Task RoutingFields_ContainEndpointAndRoutePattern()
    {
        using var host = new RequestTrafficLoggingTestHost();
        RequestDelegate pipeline = host.BuildPipeline(app => app.Run(static _ => Task.CompletedTask));
        DefaultHttpContext context = host.CreateContext("/orders/7");
        context.SetEndpoint(CreateRouteEndpoint("Orders.Get", "/orders/{id:int}"));

        await pipeline(context);

        CapturedLogRecord record = host.SingleTrafficRecord();
        Assert.AreEqual("Orders.Get", record.GetProperty("Endpoint"));
        Assert.AreEqual("/orders/{id:int}", record.GetProperty("RoutePattern"));
    }

    [TestMethod]
    public async Task ClientNetworkFeature_NormalizesCoreRemoteIpAndCanExposeForwardedChain()
    {
        using var host = new RequestTrafficLoggingTestHost(options =>
            options.Fields |= RequestTrafficLoggingFields.ForwardedInformation);
        RequestDelegate pipeline = host.BuildPipeline(app => app.Run(static _ => Task.CompletedTask));
        DefaultHttpContext context = host.CreateContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.10");
        var forwarded = new ClientForwardedIpAddress(
            ClientForwardedIpSource.XForwardedFor,
            "198.51.100.40",
            IPAddress.Parse("198.51.100.40"),
            isMalformed: false);
        context.Features.Set<IClientNetworkFeature>(new ClientNetworkFeature(
            IPAddress.Parse("203.0.113.99"),
            new[] { forwarded }));

        await pipeline(context);

        CapturedLogRecord record = host.SingleTrafficRecord();
        Assert.AreEqual("203.0.113.99", record.GetProperty("RemoteIpAddress"));
        string[] chain = (string[])record.GetProperty("ForwardedIpChain")!;
        CollectionAssert.AreEqual(new[] { "XForwardedFor:198.51.100.40" }, chain);
        Assert.AreEqual(false, record.GetProperty("HasMalformedForwardedIpInformation"));
    }

    [TestMethod]
    public async Task IdentityAndConnection_AreSmallOptionalGroups()
    {
        using var host = new RequestTrafficLoggingTestHost(options =>
            options.Fields |= RequestTrafficLoggingFields.Identity | RequestTrafficLoggingFields.Connection);
        RequestDelegate pipeline = host.BuildPipeline(app => app.Run(static _ => Task.CompletedTask));
        DefaultHttpContext context = host.CreateContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, "alice"), new Claim("secret-claim", "not-logged") },
            authenticationType: "test"));

        await pipeline(context);

        CapturedLogRecord record = host.SingleTrafficRecord();
        Assert.AreEqual(true, record.GetProperty("IdentityAuthenticated"));
        Assert.AreEqual("alice", record.GetProperty("IdentityName"));
        Assert.AreEqual("test", record.GetProperty("IdentityAuthenticationType"));
        Assert.AreEqual("127.0.0.1", record.GetProperty("LocalIpAddress"));
        Assert.AreEqual(8443, record.GetProperty("LocalPort"));
        Assert.AreEqual(52341, record.GetProperty("RemotePort"));
        Assert.IsFalse(record.TryGetProperty("secret-claim", out _));
    }

    [TestMethod]
    public async Task LoggingDisabled_BypassesA4CaptureWrappersAndEmitsNoTrafficRecord()
    {
        using var host = new RequestTrafficLoggingTestHost(options =>
            options.Fields |= RequestTrafficLoggingFields.RequestBody | RequestTrafficLoggingFields.ResponseBody,
            minimumLevel: LogLevel.Warning);
        bool sawRequestWrapper = false;
        bool sawResponseWrapper = false;
        RequestDelegate pipeline = host.BuildPipeline(app => app.Run(context =>
        {
            sawRequestWrapper = context.Request.Body is RequestTrafficCountingReadStream;
            sawResponseWrapper = context.Features.Get<IHttpResponseBodyFeature>() is RequestTrafficCountingResponseBodyFeature;
            return Task.CompletedTask;
        }));
        DefaultHttpContext context = host.CreateContext();
        context.Request.ContentType = "text/plain";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("payload"));

        await pipeline(context);

        Assert.IsFalse(sawRequestWrapper);
        Assert.IsFalse(sawResponseWrapper);
        foreach (CapturedLogRecord record in host.LoggerProvider.Records)
        {
            Assert.IsFalse(record.TryGetProperty("Event", out object? value) && Equals(value, "RequestTraffic"));
        }
    }

    private static RequestTrafficLoggingTestHost CreateSensitiveHeaderHost(SensitiveValueMode mode)
    {
        return new RequestTrafficLoggingTestHost(options =>
        {
            options.Fields |= RequestTrafficLoggingFields.RequestHeaders;
            options.SensitiveValueMode = mode;
        });
    }

    private static string Hash(string value)
    {
        return "SHA256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static RouteEndpoint CreateRouteEndpoint(string displayName, string pattern)
    {
        return new RouteEndpoint(
            static _ => Task.CompletedTask,
            RoutePatternFactory.Parse(pattern),
            order: 0,
            EndpointMetadataCollection.Empty,
            displayName);
    }

    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        private readonly IHttpResponseFeature _inner;

        internal StartedResponseFeature(IHttpResponseFeature inner)
        {
            _inner = inner;
        }

        internal bool HasStartedValue { get; set; }

        public int StatusCode
        {
            get => _inner.StatusCode;
            set => _inner.StatusCode = value;
        }

        public string? ReasonPhrase
        {
            get => _inner.ReasonPhrase;
            set => _inner.ReasonPhrase = value;
        }

        public IHeaderDictionary Headers
        {
            get => _inner.Headers;
            set => _inner.Headers = value;
        }

#pragma warning disable CS0618 // IHttpResponseFeature still requires this legacy member for the HasStarted test double.
        public Stream Body
        {
            get => _inner.Body;
            set => _inner.Body = value;
        }
#pragma warning restore CS0618

        public bool HasStarted => HasStartedValue || _inner.HasStarted;

        public void OnStarting(Func<object, Task> callback, object state) => _inner.OnStarting(callback, state);

        public void OnCompleted(Func<object, Task> callback, object state) => _inner.OnCompleted(callback, state);
    }
}
