using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Eigenverft.WebLib.ClientNetwork;
using Eigenverft.WebLib.RequestTrafficLogging;

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
using Microsoft.Extensions.Primitives;

namespace Eigenverft.WebLib.RequestTrafficLogging.Tests;

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
        Assert.AreEqual("Completed", record.GetProperty("Pipeline.Outcome"));
        Assert.AreEqual("GET", record.GetProperty("Request.Method"));
        Assert.AreEqual("https", record.GetProperty("Request.Scheme"));
        Assert.AreEqual("example.test:8443", record.GetProperty("Request.Host"));
        Assert.AreEqual("/api", record.GetProperty("Request.PathBase")?.ToString());
        Assert.AreEqual("/items/42", record.GetProperty("Request.Path")?.ToString());
        Assert.AreEqual("HTTP/1.1", record.GetProperty("Request.Protocol"));
        Assert.AreEqual(StatusCodes.Status204NoContent, record.GetProperty("Response.StatusCode"));
        Assert.AreEqual("203.0.113.7", record.GetProperty("Connection.Remote.IpAddress"));
        Assert.AreEqual(false, record.GetProperty("Pipeline.Aborted"));
        Assert.IsTrue((double)record.GetProperty("Pipeline.DurationMs")! >= 0D);
        Assert.IsInstanceOfType<DateTimeOffset>(record.GetProperty("TimestampUtc"));
        Assert.AreEqual("trace-test-123", record.GetProperty("TraceId"));
        Assert.AreEqual("GET /items/{id}", record.GetProperty("Routing.Endpoint"));
        Assert.AreEqual("/items/{id}", record.GetProperty("Routing.Pattern"));
        Assert.IsNull(record.GetProperty("Pipeline.ExceptionType"));
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
        Assert.AreEqual("Completed", record.GetProperty("Pipeline.Outcome"));
        Assert.AreEqual("203.0.113.7", record.GetProperty("Connection.Remote.IpAddress"));
        Assert.AreEqual("HEAD", record.GetProperty("Request.Method"));
        Assert.AreEqual("example.test:8443", record.GetProperty("Request.Host"));
        Assert.AreEqual("/.env", record.GetProperty("Request.Path")?.ToString());
        Assert.AreEqual("scanner/1.0", record.GetProperty("Request.UserAgent"));
        Assert.AreEqual(StatusCodes.Status404NotFound, record.GetProperty("Response.StatusCode"));
    }

    [TestMethod]
    public async Task DefaultRecord_UsesHierarchicalSectionsInDiagnosticOrder()
    {
        using var host = new RequestTrafficLoggingTestHost();
        RequestDelegate pipeline = host.BuildPipeline(app =>
            app.Run(context =>
            {
                context.Response.ContentType = "text/plain";
                return Task.CompletedTask;
            }));

        await pipeline(host.CreateContext("/ordered"));

        CapturedLogRecord record = host.SingleTrafficRecord();
        Assert.IsTrue(record.GetPropertyIndex("Event") < record.GetPropertyIndex("Request.Protocol"));
        Assert.IsTrue(record.GetPropertyIndex("Request.Body.DeclaredLength") <
            record.GetPropertyIndex("Connection.Remote.IpAddress"));
        Assert.IsTrue(record.GetPropertyIndex("Connection.Remote.IpAddress") <
            record.GetPropertyIndex("Routing.Endpoint"));
        Assert.IsTrue(record.GetPropertyIndex("Routing.Pattern") <
            record.GetPropertyIndex("Response.StatusCode"));
        Assert.IsTrue(record.GetPropertyIndex("Response.Body.DeclaredLength") <
            record.GetPropertyIndex("Pipeline.Outcome"));
        Assert.IsFalse(record.TryGetProperty("Method", out _));
        Assert.IsFalse(record.TryGetProperty("RemoteIpAddress", out _));
        Assert.IsFalse(record.TryGetProperty("StatusCode", out _));
        Assert.IsFalse(record.TryGetProperty("PipelineOutcome", out _));
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
        Assert.AreEqual("Faulted", record.GetProperty("Pipeline.Outcome"));
        Assert.AreEqual(typeof(InvalidOperationException).FullName, record.GetProperty("Pipeline.ExceptionType"));
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
        Assert.AreEqual("Faulted", record.GetProperty("Pipeline.Outcome"));
        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, record.GetProperty("Response.StatusCode"));
        Assert.AreEqual(typeof(InvalidOperationException).FullName, record.GetProperty("Pipeline.ExceptionType"));
    }

    [TestMethod]
    public async Task CancellationSignal_WhenPipelineReturnsNormally_IsCompletedAndPreserved()
    {
        using var host = new RequestTrafficLoggingTestHost();
        using var cancellation = new CancellationTokenSource();
        RequestDelegate pipeline = host.BuildPipeline(app =>
            app.Run(context =>
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                cancellation.Cancel();
                return Task.CompletedTask;
            }));
        DefaultHttpContext context = host.CreateContext();
        context.RequestAborted = cancellation.Token;

        await pipeline(context);

        CapturedLogRecord record = host.SingleTrafficRecord();
        Assert.AreEqual("Completed", record.GetProperty("Pipeline.Outcome"));
        Assert.AreEqual(true, record.GetProperty("Pipeline.Aborted"));
        Assert.AreEqual(StatusCodes.Status204NoContent, record.GetProperty("Response.StatusCode"));
        Assert.IsNull(record.GetProperty("Pipeline.ExceptionType"));
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
        Assert.AreEqual("Aborted", record.GetProperty("Pipeline.Outcome"));
        Assert.AreEqual(true, record.GetProperty("Pipeline.Aborted"));
        Assert.AreEqual(false, record.GetProperty("Response.Started"));
        Assert.AreEqual(typeof(OperationCanceledException).FullName, record.GetProperty("Pipeline.ExceptionType"));
    }

    [TestMethod]
    public async Task ClientDisconnectIOException_WithCancellationSignal_IsAborted()
    {
        using var host = new RequestTrafficLoggingTestHost();
        using var cancellation = new CancellationTokenSource();
        RequestDelegate pipeline = host.BuildPipeline(app =>
            app.Run(_ =>
            {
                cancellation.Cancel();
                return Task.FromException(new IOException("The client disconnected."));
            }));
        DefaultHttpContext context = host.CreateContext();
        context.RequestAborted = cancellation.Token;

        IOException? thrown = null;
        try
        {
            await pipeline(context);
        }
        catch (IOException exception)
        {
            thrown = exception;
        }

        Assert.IsNotNull(thrown);
        CapturedLogRecord record = host.SingleTrafficRecord();
        Assert.AreEqual("Aborted", record.GetProperty("Pipeline.Outcome"));
        Assert.AreEqual(true, record.GetProperty("Pipeline.Aborted"));
        Assert.AreEqual(typeof(IOException).FullName, record.GetProperty("Pipeline.ExceptionType"));
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
        Assert.AreEqual("Aborted", record.GetProperty("Pipeline.Outcome"));
        Assert.AreEqual(StatusCodes.Status200OK, record.GetProperty("Response.StatusCode"));
        Assert.AreEqual(true, record.GetProperty("Response.Started"));
        Assert.AreEqual(true, record.GetProperty("Pipeline.Aborted"));
        Assert.AreEqual(typeof(OperationCanceledException).FullName, record.GetProperty("Pipeline.ExceptionType"));
    }

    [TestMethod]
    public async Task StartedResponse_WithLateCancellationSignal_IsCompleted()
    {
        using var host = new RequestTrafficLoggingTestHost();
        using var cancellation = new CancellationTokenSource();
        StartedResponseFeature? startedFeature = null;

        RequestDelegate pipeline = host.BuildPipeline(app =>
            app.Run(context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                startedFeature!.HasStartedValue = true;
                cancellation.Cancel();
                return Task.CompletedTask;
            }));

        DefaultHttpContext context = host.CreateContext();
        context.RequestAborted = cancellation.Token;
        startedFeature = new StartedResponseFeature(context.Features.GetRequiredFeature<IHttpResponseFeature>());
        context.Features.Set<IHttpResponseFeature>(startedFeature);

        await pipeline(context);

        CapturedLogRecord record = host.SingleTrafficRecord();
        Assert.AreEqual("Completed", record.GetProperty("Pipeline.Outcome"));
        Assert.AreEqual(StatusCodes.Status200OK, record.GetProperty("Response.StatusCode"));
        Assert.AreEqual(true, record.GetProperty("Response.Started"));
        Assert.AreEqual(true, record.GetProperty("Pipeline.Aborted"));
        Assert.IsNull(record.GetProperty("Pipeline.ExceptionType"));
    }

    [TestMethod]
    public async Task CompletedSseResponse_WithLateCancellationSignal_IsCompleted()
    {
        using var host = new RequestTrafficLoggingTestHost(options =>
            options.Fields |= RequestTrafficLoggingFields.ResponseBody);
        using var cancellation = new CancellationTokenSource();
        StartedResponseFeature? startedFeature = null;
        const string responseBody = "event: message\ndata: {\"result\":{\"resultType\":\"complete\"}}\n\n";

        RequestDelegate pipeline = host.BuildPipeline(app =>
            app.Run(async context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "text/event-stream";
                await context.Response.WriteAsync(responseBody);
                startedFeature!.HasStartedValue = true;
                cancellation.Cancel();
            }));

        DefaultHttpContext context = host.CreateContext("/mcp");
        context.RequestAborted = cancellation.Token;
        startedFeature = new StartedResponseFeature(context.Features.GetRequiredFeature<IHttpResponseFeature>());
        context.Features.Set<IHttpResponseFeature>(startedFeature);

        await pipeline(context);

        CapturedLogRecord record = host.SingleTrafficRecord();
        Assert.AreEqual("Completed", record.GetProperty("Pipeline.Outcome"));
        Assert.AreEqual(StatusCodes.Status200OK, record.GetProperty("Response.StatusCode"));
        Assert.AreEqual(true, record.GetProperty("Response.Started"));
        Assert.AreEqual("text/event-stream", record.GetProperty("Response.Body.ContentType"));
        Assert.AreEqual(true, record.GetProperty("Pipeline.Aborted"));
        Assert.IsNull(record.GetProperty("Pipeline.ExceptionType"));
        StringAssert.Contains(record.GetProperty("ResponseBody")?.ToString(), "\"resultType\":\"complete\"");
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
            Assert.IsFalse(host.SingleTrafficRecord().TryGetProperty("Request.QueryString", out _));
        }

        using (var host = new RequestTrafficLoggingTestHost(options =>
               options.Fields |= RequestTrafficLoggingFields.Query))
        {
            RequestDelegate pipeline = host.BuildPipeline(app => app.Run(static _ => Task.CompletedTask));
            DefaultHttpContext context = host.CreateContext();
            context.Request.QueryString = new QueryString("?token=secret");
            await pipeline(context);
            Assert.AreEqual("?token=secret", host.SingleTrafficRecord().GetProperty("Request.QueryString"));
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
            Assert.IsFalse(host.SingleTrafficRecord().TryGetProperty("Request.Header.X-Correlation", out _));
        }

        using (var host = new RequestTrafficLoggingTestHost(options =>
               options.Fields |= RequestTrafficLoggingFields.RequestHeaders))
        {
            RequestDelegate pipeline = host.BuildPipeline(app => app.Run(static _ => Task.CompletedTask));
            DefaultHttpContext context = host.CreateContext();
            context.Request.Headers["X-Correlation"] = "abc";
            await pipeline(context);
            Assert.AreEqual("[Redacted]", host.SingleTrafficRecord().GetProperty("Request.Header.X-Correlation"));
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
            Assert.IsFalse(host.SingleTrafficRecord().TryGetProperty("Response.Header.X-Node", out _));
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
            Assert.AreEqual("[Redacted]", host.SingleTrafficRecord().GetProperty("Response.Header.X-Node"));
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
        Assert.AreEqual("[Redacted]", record.GetProperty("Request.Header.Authorization"));
        Assert.IsFalse(record.TryGetProperty("Request.Header.Authorization.Hash", out _));
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
        Assert.AreEqual("[Redacted]", record.GetProperty("Request.Header.Authorization"));
        Assert.AreEqual(Hash(value), record.GetProperty("Request.Header.Authorization.Hash"));
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

        Assert.AreEqual(value, host.SingleTrafficRecord().GetProperty("Request.Header.Authorization"));
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
        Assert.AreEqual("[Redacted]", record.GetProperty("Response.Header.Set-Cookie"));
        Assert.AreEqual(Hash(value), record.GetProperty("Response.Header.Set-Cookie.Hash"));
    }

    [TestMethod]
    public async Task AllRawHeaders_IncludeUnknownAndSensitiveValuesWithoutFrameworkRedaction()
    {
        using var host = new RequestTrafficLoggingTestHost(options =>
        {
            options.Fields |= RequestTrafficLoggingFields.RequestHeaders | RequestTrafficLoggingFields.ResponseHeaders;
            options.HeaderCaptureMode = HeaderCaptureMode.AllRaw;
        });
        RequestDelegate pipeline = host.BuildPipeline(app => app.Run(context =>
        {
            context.Response.Headers.SetCookie = "session=secret";
            context.Response.Headers["X-Unknown-Response"] = "response-value";
            return Task.CompletedTask;
        }));
        DefaultHttpContext context = host.CreateContext();
        context.Request.Headers.Authorization = "Bearer secret-token";
        context.Request.Headers["X-Unknown-Request"] = "request-value";

        await pipeline(context);

        CapturedLogRecord record = host.SingleTrafficRecord();
        Assert.AreEqual("Bearer secret-token", record.GetProperty("Request.Header.Authorization"));
        Assert.AreEqual("request-value", record.GetProperty("Request.Header.X-Unknown-Request"));
        Assert.AreEqual("session=secret", record.GetProperty("Response.Header.Set-Cookie"));
        Assert.AreEqual("response-value", record.GetProperty("Response.Header.X-Unknown-Response"));
        Assert.IsTrue(record.Message.Contains("Request.Header.Authorization: Bearer secret-token", StringComparison.Ordinal));
        Assert.IsTrue(record.Message.Contains("Response.Header.Set-Cookie: session=secret", StringComparison.Ordinal));
        Assert.IsFalse(record.Message.Contains("[Redacted]", StringComparison.Ordinal));
        Assert.IsFalse(record.TryGetProperty("Authorization", out _));
    }

    [TestMethod]
    public async Task AllRawHeaders_PreserveMultipleValuesAndRespectFieldFlags()
    {
        using (var host = new RequestTrafficLoggingTestHost(options =>
               {
                   options.Fields |= RequestTrafficLoggingFields.RequestHeaders | RequestTrafficLoggingFields.ResponseHeaders;
                   options.HeaderCaptureMode = HeaderCaptureMode.AllRaw;
               }))
        {
            RequestDelegate pipeline = host.BuildPipeline(app => app.Run(context =>
            {
                context.Response.Headers.SetCookie = new StringValues(new[] { "a=1", "b=2" });
                return Task.CompletedTask;
            }));
            DefaultHttpContext context = host.CreateContext();
            context.Request.Headers["X-Multiple"] = new StringValues(new[] { "first", "second" });

            await pipeline(context);

            CapturedLogRecord record = host.SingleTrafficRecord();
            Assert.AreEqual("first", record.GetProperty("Request.Header.X-Multiple[0]"));
            Assert.AreEqual("second", record.GetProperty("Request.Header.X-Multiple[1]"));
            Assert.AreEqual("a=1", record.GetProperty("Response.Header.Set-Cookie[0]"));
            Assert.AreEqual("b=2", record.GetProperty("Response.Header.Set-Cookie[1]"));
        }

        using (var host = new RequestTrafficLoggingTestHost(options =>
               {
                   options.Fields = RequestTrafficLoggingFields.Core;
                   options.HeaderCaptureMode = HeaderCaptureMode.AllRaw;
               }))
        {
            RequestDelegate pipeline = host.BuildPipeline(app => app.Run(static _ => Task.CompletedTask));
            DefaultHttpContext context = host.CreateContext();
            context.Request.Headers.Authorization = "Bearer secret-token";

            await pipeline(context);

            CapturedLogRecord record = host.SingleTrafficRecord();
            Assert.IsFalse(record.TryGetProperty("Request.Header.Authorization", out _));
        }
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
        Assert.AreEqual(8L, record.GetProperty("Request.Body.DeclaredLength"));
        Assert.AreEqual(true, record.GetProperty("Request.Body.Truncated"));
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
            Assert.AreEqual((long)size, record.GetProperty("Request.Body.DeclaredLength"));
            Assert.AreEqual(expectedTruncated, record.GetProperty("Request.Body.Truncated"));
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
        Assert.IsNull(record.GetProperty("Request.Body.DeclaredLength"));
        Assert.IsNull(record.GetProperty("Request.Body.Truncated"));
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
        Assert.AreEqual(100L, record.GetProperty("Request.Body.DeclaredLength"));
        Assert.AreEqual(false, record.GetProperty("Request.Body.Truncated"));
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
        Assert.AreEqual(8L, record.GetProperty("Response.Body.DeclaredLength"));
        Assert.AreEqual(true, record.GetProperty("Response.Body.Truncated"));
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
            Assert.AreEqual((long)size, record.GetProperty("Response.Body.DeclaredLength"));
            Assert.AreEqual(expectedTruncated, record.GetProperty("Response.Body.Truncated"));
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
        Assert.IsNull(record.GetProperty("Response.Body.DeclaredLength"));
        Assert.IsNull(record.GetProperty("Response.Body.Truncated"));
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
        Assert.AreEqual("Orders.Get", record.GetProperty("Routing.Endpoint"));
        Assert.AreEqual("/orders/{id:int}", record.GetProperty("Routing.Pattern"));
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
        Assert.AreEqual("203.0.113.99", record.GetProperty("Connection.Remote.IpAddress"));
        Assert.AreEqual("XForwardedFor:198.51.100.40", record.GetProperty("Connection.ForwardedIpChain"));
        Assert.AreEqual(false, record.GetProperty("Connection.ForwardedIpMalformed"));
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
        Assert.AreEqual(true, record.GetProperty("Identity.Authenticated"));
        Assert.AreEqual("alice", record.GetProperty("Identity.Name"));
        Assert.AreEqual("test", record.GetProperty("Identity.AuthenticationType"));
        Assert.AreEqual("127.0.0.1", record.GetProperty("Connection.Local.IpAddress"));
        Assert.AreEqual(8443, record.GetProperty("Connection.Local.Port"));
        Assert.AreEqual(52341, record.GetProperty("Connection.Remote.Port"));
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
        Assert.AreEqual("Completed", record.GetProperty("Pipeline.Outcome"));
        Assert.IsFalse(record.TryGetProperty("Request.Method", out _));
        Assert.IsFalse(record.TryGetProperty("Request.Path", out _));
        Assert.IsFalse(record.TryGetProperty("Connection.Remote.IpAddress", out _));
        Assert.IsFalse(record.TryGetProperty("Response.StatusCode", out _));
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
        Assert.AreEqual(0, options.RequestHeaders.Count);
        Assert.AreEqual(0, options.ResponseHeaders.Count);
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
