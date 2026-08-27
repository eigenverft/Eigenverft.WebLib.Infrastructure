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
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    public async Task RequestBody_UsesFrameworkCaptureLimitAndKnownContentLengthMetadata()
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
        Assert.AreEqual(8L, record.GetProperty("RequestBodyTotalBytes"));
        Assert.AreEqual(true, record.GetProperty("RequestBodyTruncated"));
        Assert.IsFalse(record.TryGetProperty("RequestBodyCapturedBytes", out _));
    }

    [TestMethod]
    public async Task RequestBody_TruncationBoundaryUsesStrictGreaterThan()
    {
        foreach ((int size, bool expectedTruncated) in new[] { (3, false), (4, false), (5, true) })
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
            byte[] body = Encoding.UTF8.GetBytes(new string('x', size));
            context.Request.Method = HttpMethods.Post;
            context.Request.ContentType = "text/plain";
            context.Request.ContentLength = body.Length;
            context.Request.Body = new MemoryStream(body);

            await pipeline(context);

            CapturedLogRecord record = host.SingleTrafficRecord();
            Assert.AreEqual((long)size, record.GetProperty("RequestBodyTotalBytes"));
            Assert.AreEqual(expectedTruncated, record.GetProperty("RequestBodyTruncated"));
        }
    }

    [TestMethod]
    public async Task PartialRequestRead_DoesNotInventUnknownTotalBytes()
    {
        using var host = new RequestTrafficLoggingTestHost(options =>
        {
            options.Fields |= RequestTrafficLoggingFields.RequestBody;
            options.RequestBodyLimit = 4;
        });
        RequestDelegate pipeline = host.BuildPipeline(app => app.Run(async context =>
        {
            var buffer = new byte[10];
            _ = await context.Request.Body.ReadAsync(buffer);
        }));
        DefaultHttpContext context = host.CreateContext();
        byte[] body = Encoding.UTF8.GetBytes(new string('x', 100));
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = "text/plain";
        context.Request.ContentLength = null;
        context.Request.Body = new MemoryStream(body);

        await pipeline(context);

        CapturedLogRecord record = host.SingleTrafficRecord();
        Assert.IsNull(record.GetProperty("RequestBodyTotalBytes"));
        Assert.IsNull(record.GetProperty("RequestBodyTruncated"));
    }

    [TestMethod]
    public async Task SeekableRequestReRead_UsesContentLengthInsteadOfSummedReads()
    {
        using var host = new RequestTrafficLoggingTestHost(options =>
        {
            options.Fields |= RequestTrafficLoggingFields.RequestBody;
            options.RequestBodyLimit = 200;
        });
        RequestDelegate pipeline = host.BuildPipeline(app => app.Run(async context =>
        {
            await context.Request.Body.CopyToAsync(Stream.Null);
            context.Request.Body.Position = 0;
            await context.Request.Body.CopyToAsync(Stream.Null);
        }));
        DefaultHttpContext context = host.CreateContext();
        byte[] body = Encoding.UTF8.GetBytes(new string('x', 100));
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = "text/plain";
        context.Request.ContentLength = body.Length;
        context.Request.Body = new MemoryStream(body);

        await pipeline(context);

        CapturedLogRecord record = host.SingleTrafficRecord();
        Assert.AreEqual(100L, record.GetProperty("RequestBodyTotalBytes"));
        Assert.AreEqual(false, record.GetProperty("RequestBodyTruncated"));
    }

    [TestMethod]
    public async Task ResponseBody_UsesFrameworkCaptureLimitAndKnownContentLengthMetadata()
    {
        using var host = new RequestTrafficLoggingTestHost(options =>
        {
            options.Fields |= RequestTrafficLoggingFields.ResponseBody;
            options.ResponseBodyLimit = 4;
        });
        RequestDelegate pipeline = host.BuildPipeline(app => app.Run(async context =>
        {
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.ContentLength = 8;
            await context.Response.WriteAsync("abcdefgh");
        }));

        await pipeline(host.CreateContext());

        CapturedLogRecord record = host.SingleTrafficRecord();
        Assert.AreEqual("abcd", record.GetProperty("ResponseBody"));
        Assert.AreEqual(8L, record.GetProperty("ResponseBodyTotalBytes"));
        Assert.AreEqual(true, record.GetProperty("ResponseBodyTruncated"));
        Assert.IsFalse(record.TryGetProperty("ResponseBodyCapturedBytes", out _));
    }

    [TestMethod]
    public async Task ResponseBody_TruncationBoundaryUsesStrictGreaterThan()
    {
        foreach ((int size, bool expectedTruncated) in new[] { (3, false), (4, false), (5, true) })
        {
            using var host = new RequestTrafficLoggingTestHost(options =>
            {
                options.Fields |= RequestTrafficLoggingFields.ResponseBody;
                options.ResponseBodyLimit = 4;
            });
            string body = new string('x', size);
            RequestDelegate pipeline = host.BuildPipeline(app => app.Run(async context =>
            {
                context.Response.ContentType = "text/plain";
                context.Response.ContentLength = size;
                await context.Response.WriteAsync(body);
            }));

            await pipeline(host.CreateContext());

            CapturedLogRecord record = host.SingleTrafficRecord();
            Assert.AreEqual((long)size, record.GetProperty("ResponseBodyTotalBytes"));
            Assert.AreEqual(expectedTruncated, record.GetProperty("ResponseBodyTruncated"));
        }
    }

    [TestMethod]
    public async Task ResponseBodyWithoutContentLength_DoesNotInventTotalBytes()
    {
        using var host = new RequestTrafficLoggingTestHost(options =>
        {
            options.Fields |= RequestTrafficLoggingFields.ResponseBody;
            options.ResponseBodyLimit = 4;
        });
        RequestDelegate pipeline = host.BuildPipeline(app => app.Run(async context =>
        {
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("abcdefgh");
        }));

        await pipeline(host.CreateContext());

        CapturedLogRecord record = host.SingleTrafficRecord();
        Assert.AreEqual("abcd", record.GetProperty("ResponseBody"));
        Assert.IsNull(record.GetProperty("ResponseBodyTotalBytes"));
        Assert.IsNull(record.GetProperty("ResponseBodyTruncated"));
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
    public async Task FieldsNone_EmitsLifecycleEnvelopeWithoutOptionalHttpFields()
    {
        using var host = new RequestTrafficLoggingTestHost(options =>
            options.Fields = RequestTrafficLoggingFields.None);
        RequestDelegate pipeline = host.BuildPipeline(app => app.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        }));

        await pipeline(host.CreateContext());

        CapturedLogRecord record = host.SingleTrafficRecord();
        Assert.AreEqual("RequestTraffic", record.GetProperty("Event"));
        Assert.AreEqual("Completed", record.GetProperty("Outcome"));
        Assert.IsFalse(record.TryGetProperty("Method", out _));
        Assert.IsFalse(record.TryGetProperty("Path", out _));
        Assert.IsFalse(record.TryGetProperty("RemoteIpAddress", out _));
        Assert.IsFalse(record.TryGetProperty("StatusCode", out _));
    }

    [TestMethod]
    public void ExistingHttpLoggingConfiguration_IsNormalizedToA4OwnershipContract()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();
        services.AddHttpLogging(options =>
        {
            options.CombineLogs = false;
            options.LoggingFields = HttpLoggingFields.All;
            options.RequestBodyLogLimit = 17;
            options.ResponseBodyLogLimit = 19;
            options.RequestHeaders.Clear();
            options.RequestHeaders.Add("X-Existing-Request");
            options.ResponseHeaders.Clear();
            options.ResponseHeaders.Add("X-Existing-Response");
        });
        services.AddRequestTrafficLogging(options =>
        {
            options.RequestBodyLimit = 31;
            options.ResponseBodyLimit = 37;
            options.RequestHeaders.Add("X-A4-Request");
            options.ResponseHeaders.Add("X-A4-Response");
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        HttpLoggingOptions options = provider.GetRequiredService<IOptions<HttpLoggingOptions>>().Value;

        Assert.IsTrue(options.CombineLogs);
        Assert.AreEqual(HttpLoggingFields.None, options.LoggingFields);
        Assert.AreEqual(31, options.RequestBodyLogLimit);
        Assert.AreEqual(37, options.ResponseBodyLogLimit);
        Assert.IsFalse(options.RequestHeaders.Contains("X-Existing-Request"));
        Assert.IsFalse(options.ResponseHeaders.Contains("X-Existing-Response"));
        Assert.IsTrue(options.RequestHeaders.Contains("User-Agent"));
        Assert.IsTrue(options.RequestHeaders.Contains("X-A4-Request"));
        Assert.IsTrue(options.ResponseHeaders.Contains("Content-Type"));
        Assert.IsTrue(options.ResponseHeaders.Contains("X-A4-Response"));
    }

    [TestMethod]
    public async Task LoggingDisabled_DoesNotWrapBodiesAndEmitsNoTrafficRecord()
    {
        using var host = new RequestTrafficLoggingTestHost(options =>
            options.Fields |= RequestTrafficLoggingFields.RequestBody | RequestTrafficLoggingFields.ResponseBody,
            minimumLevel: LogLevel.Warning);
        Stream? observedRequestBody = null;
        IHttpResponseBodyFeature? observedResponseBodyFeature = null;
        RequestDelegate pipeline = host.BuildPipeline(app => app.Run(context =>
        {
            observedRequestBody = context.Request.Body;
            observedResponseBodyFeature = context.Features.Get<IHttpResponseBodyFeature>();
            return Task.CompletedTask;
        }));
        DefaultHttpContext context = host.CreateContext();
        var originalRequestBody = new MemoryStream(Encoding.UTF8.GetBytes("payload"));
        context.Request.ContentType = "text/plain";
        context.Request.Body = originalRequestBody;
        IHttpResponseBodyFeature? originalResponseBodyFeature = context.Features.Get<IHttpResponseBodyFeature>();

        await pipeline(context);

        Assert.AreSame(originalRequestBody, observedRequestBody);
        Assert.AreSame(originalResponseBodyFeature, observedResponseBodyFeature);
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
