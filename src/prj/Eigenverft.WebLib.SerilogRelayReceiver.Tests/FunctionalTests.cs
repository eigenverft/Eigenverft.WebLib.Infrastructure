using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Eigenverft.WebLib.SerilogRelayReceiver.Tests
{
    [TestClass]
    public sealed class FunctionalTests
    {
        [TestMethod]
        public void OptionsDefaultToSenderCompatibleBatchMaximum()
        {
            var options = new SerilogRelayReceiverOptions();

            Assert.AreEqual(100, options.MaximumBatchEvents);
            Assert.IsNull(options.BearerToken);

            options.BearerToken = "secret";
            options.MaximumBatchEvents = 42;

            Assert.AreEqual("secret", options.BearerToken);
            Assert.AreEqual(42, options.MaximumBatchEvents);
        }

        [TestMethod]
        public void BearerAuthenticationHandlesDisabledValidAndInvalidHeaders()
        {
            Assert.IsTrue(BearerTokenAuthentication.IsAuthorized(null, null));
            Assert.IsTrue(BearerTokenAuthentication.IsAuthorized(" ", "Basic ignored"));
            Assert.IsTrue(BearerTokenAuthentication.IsAuthorized("secret", "Bearer secret"));
            Assert.IsTrue(BearerTokenAuthentication.IsAuthorized("secret", "bearer secret"));

            Assert.IsFalse(BearerTokenAuthentication.IsAuthorized("secret", null));
            Assert.IsFalse(BearerTokenAuthentication.IsAuthorized("secret", "Basic secret"));
            Assert.IsFalse(BearerTokenAuthentication.IsAuthorized("secret", "Bearer"));
            Assert.IsFalse(BearerTokenAuthentication.IsAuthorized("secret", "Bearer wrong"));
            Assert.IsFalse(BearerTokenAuthentication.IsAuthorized("secret", "Bearer short"));
        }

        [TestMethod]
        public void ValidatorPreservesProtocolAndMultiApplicationSemantics()
        {
            SerilogRelayBatch valid = CreateBatch("App.One", "App.Two");

            Assert.IsNull(SerilogRelayReceiverValidator.Validate(valid, 100));

            valid.ProtocolVersion = 2;
            StringAssert.Contains(
                SerilogRelayReceiverValidator.Validate(valid, 100)!,
                "Unsupported protocolVersion");
            valid.ProtocolVersion = 1;

            valid.BatchId = "not-a-guid";
            Assert.AreEqual(
                "batchId must be a canonical GUID.",
                SerilogRelayReceiverValidator.Validate(valid, 100));
            valid.BatchId = Guid.NewGuid().ToString("D");

            valid.Logs = null;
            Assert.AreEqual(
                "logs must not be null.",
                SerilogRelayReceiverValidator.Validate(valid, 100));
            valid.Logs = CreateBatch("App.One", "App.Two").Logs;

            valid.Count = 1;
            Assert.AreEqual(
                "count must match the number of logs.",
                SerilogRelayReceiverValidator.Validate(valid, 100));
            valid.Count = 2;

            StringAssert.Contains(
                SerilogRelayReceiverValidator.Validate(valid, 1)!,
                "maximum is 1");

            valid.Logs![0].EventId = "bad";
            Assert.AreEqual(
                "Every log event must contain a canonical eventId GUID.",
                SerilogRelayReceiverValidator.Validate(valid, 100));
            valid.Logs[0].EventId = Guid.NewGuid().ToString("D");

            valid.Logs[0] = null!;
            Assert.AreEqual(
                "logs must not contain null entries.",
                SerilogRelayReceiverValidator.Validate(valid, 100));
            List<SerilogRelayEvent> restoredLogs =
                CreateBatch("App.One", "App.Two").Logs!;
            valid.Logs = restoredLogs;

            restoredLogs[0].ApplicationId = " ";
            Assert.AreEqual(
                "Every log event must contain a non-empty applicationId.",
                SerilogRelayReceiverValidator.Validate(valid, 100));
            restoredLogs[0].ApplicationId = "App.One";

            restoredLogs[0].ProcessId = 0;
            Assert.AreEqual(
                "Every log event must contain a positive processId.",
                SerilogRelayReceiverValidator.Validate(valid, 100));
        }

        [TestMethod]
        public void ServiceRegistrationAndMappingValidateArguments()
        {
            Assert.ThrowsExactly<ArgumentNullException>(
                () => SerilogRelayReceiverServiceCollectionExtensions
                    .AddSerilogRelayReceiver<CapturingHandler>(null!));

            var services = new ServiceCollection();
            IServiceCollection returned =
                services.AddSerilogRelayReceiver<CapturingHandler>();
            Assert.AreSame(services, returned);

            services.AddSerilogRelayReceiver<CapturingHandler>();
            Assert.AreEqual(
                1,
                services.Count(descriptor =>
                    descriptor.ServiceType == typeof(CapturingHandler)));

            WebApplication app = WebApplication.CreateBuilder().Build();

            Assert.ThrowsExactly<ArgumentNullException>(
                () => SerilogRelayReceiverEndpointRouteBuilderExtensions
                    .MapSerilogRelayReceiver<CapturingHandler>(
                        null!,
                        "/logs"));

            Assert.ThrowsExactly<ArgumentException>(
                () => app.MapSerilogRelayReceiver<CapturingHandler>(" "));

            Assert.IsNotNull(
                app.MapSerilogRelayReceiver<CapturingHandler>("/defaults"));

            Assert.ThrowsExactly<InvalidOperationException>(
                () => app.MapSerilogRelayReceiver<CapturingHandler>(
                    "/invalid",
                    options => options.MaximumBatchEvents = 0));
        }

        [TestMethod]
        public async Task EndpointAuthenticatesValidatesAndAcceptsMultipleApplications()
        {
            BatchCapture capture = new BatchCapture();
            await using WebApplication app = CreateApplication(
                capture,
                bearerToken: "secret",
                maximumBatchEvents: 100);

            using HttpClient client = await StartClientAsync(app);
            SerilogRelayBatch valid = CreateBatch("App.One", "App.Two");

            using HttpResponseMessage unauthorized =
                await client.PostAsJsonAsync("/logs", valid);
            Assert.AreEqual(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
            Assert.AreEqual(
                "Bearer",
                unauthorized.Headers.WwwAuthenticate.Single().Scheme);

            using var plainText = new StringContent("{}", Encoding.UTF8, "text/plain");
            using HttpRequestMessage unsupportedRequest =
                new HttpRequestMessage(HttpMethod.Post, "/logs")
                {
                    Content = plainText,
                };
            unsupportedRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", "secret");
            using HttpResponseMessage unsupported =
                await client.SendAsync(unsupportedRequest);
            Assert.AreEqual(
                HttpStatusCode.UnsupportedMediaType,
                unsupported.StatusCode);

            using var malformed = new StringContent(
                "{",
                Encoding.UTF8,
                "application/json");
            using HttpRequestMessage malformedRequest =
                new HttpRequestMessage(HttpMethod.Post, "/logs")
                {
                    Content = malformed,
                };
            malformedRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", "secret");
            using HttpResponseMessage malformedResponse =
                await client.SendAsync(malformedRequest);
            Assert.AreEqual(HttpStatusCode.BadRequest, malformedResponse.StatusCode);

            using var nullJson = new StringContent(
                "null",
                Encoding.UTF8,
                "application/json");
            using HttpRequestMessage nullRequest =
                new HttpRequestMessage(HttpMethod.Post, "/logs")
                {
                    Content = nullJson,
                };
            nullRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", "secret");
            using HttpResponseMessage nullResponse =
                await client.SendAsync(nullRequest);
            Assert.AreEqual(HttpStatusCode.BadRequest, nullResponse.StatusCode);

            SerilogRelayBatch invalid = CreateBatch("App.One");
            invalid.ProtocolVersion = 99;
            using HttpRequestMessage invalidRequest =
                new HttpRequestMessage(HttpMethod.Post, "/logs")
                {
                    Content = JsonContent.Create(invalid),
                };
            invalidRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", "secret");
            using HttpResponseMessage invalidResponse =
                await client.SendAsync(invalidRequest);
            Assert.AreEqual(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
            StringAssert.Contains(
                await invalidResponse.Content.ReadAsStringAsync(),
                "Unsupported protocolVersion");

            using HttpRequestMessage validRequest =
                new HttpRequestMessage(HttpMethod.Post, "/logs")
                {
                    Content = JsonContent.Create(valid),
                };
            validRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", "secret");
            using HttpResponseMessage accepted =
                await client.SendAsync(validRequest);
            Assert.AreEqual(HttpStatusCode.NoContent, accepted.StatusCode);

            SerilogRelayBatch received =
                await capture.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            CollectionAssert.AreEqual(
                new[] { "App.One", "App.Two" },
                received.Logs!.Select(entry => entry.ApplicationId).ToArray());
        }

        [TestMethod]
        public async Task EndpointSupportsDisabledAuthAndPerEndpointBatchLimit()
        {
            BatchCapture capture = new BatchCapture();
            await using WebApplication app = CreateApplication(
                capture,
                bearerToken: null,
                maximumBatchEvents: 1);

            using HttpClient client = await StartClientAsync(app);

            using HttpResponseMessage tooLarge =
                await client.PostAsJsonAsync(
                    "/logs",
                    CreateBatch("App.One", "App.Two"));
            Assert.AreEqual(HttpStatusCode.BadRequest, tooLarge.StatusCode);
            StringAssert.Contains(
                await tooLarge.Content.ReadAsStringAsync(),
                "maximum is 1");

            using HttpResponseMessage accepted =
                await client.PostAsJsonAsync("/logs", CreateBatch("App.One"));
            Assert.AreEqual(HttpStatusCode.NoContent, accepted.StatusCode);
        }

        [TestMethod]
        public async Task EndpointOptionsRemainIsolatedAcrossMultipleMappings()
        {
            BatchCapture capture = new BatchCapture();
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton(capture);
            builder.Services.AddSerilogRelayReceiver<CapturingHandler>();

            await using WebApplication app = builder.Build();
            app.MapSerilogRelayReceiver<CapturingHandler>(
                "/strict",
                options =>
                {
                    options.BearerToken = "strict-secret";
                    options.MaximumBatchEvents = 1;
                });
            app.MapSerilogRelayReceiver<CapturingHandler>(
                "/open",
                options => options.MaximumBatchEvents = 2);

            using HttpClient client = await StartClientAsync(app);

            using HttpResponseMessage strictUnauthorized =
                await client.PostAsJsonAsync("/strict", CreateBatch("App.One"));
            Assert.AreEqual(
                HttpStatusCode.Unauthorized,
                strictUnauthorized.StatusCode);

            using HttpResponseMessage openAccepted =
                await client.PostAsJsonAsync(
                    "/open",
                    CreateBatch("App.One", "App.Two"));
            Assert.AreEqual(HttpStatusCode.NoContent, openAccepted.StatusCode);

            using HttpRequestMessage strictTooLargeRequest =
                new HttpRequestMessage(HttpMethod.Post, "/strict")
                {
                    Content = JsonContent.Create(
                        CreateBatch("App.One", "App.Two")),
                };
            strictTooLargeRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", "strict-secret");

            using HttpResponseMessage strictTooLarge =
                await client.SendAsync(strictTooLargeRequest);
            Assert.AreEqual(HttpStatusCode.BadRequest, strictTooLarge.StatusCode);
            StringAssert.Contains(
                await strictTooLarge.Content.ReadAsStringAsync(),
                "maximum is 1");
        }

        [TestMethod]
        public async Task EndpointHandlesJsonMediaTypeEdgesAsProtocolInput()
        {
            BatchCapture capture = new BatchCapture();
            await using WebApplication app = CreateApplication(
                capture,
                bearerToken: null,
                maximumBatchEvents: 100);
            using HttpClient client = await StartClientAsync(app);

            SerilogRelayBatch batch = CreateBatch("App.One");
            string json = System.Text.Json.JsonSerializer.Serialize(batch);

            using var subtypeContent =
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/vnd.eigenverft.serilogrelay+json");
            using HttpResponseMessage subtypeResponse =
                await client.PostAsync("/logs", subtypeContent);
            Assert.AreEqual(HttpStatusCode.NoContent, subtypeResponse.StatusCode);

            using var emptyJson =
                new StringContent(string.Empty, Encoding.UTF8, "application/json");
            using HttpResponseMessage emptyResponse =
                await client.PostAsync("/logs", emptyJson);
            Assert.AreEqual(HttpStatusCode.BadRequest, emptyResponse.StatusCode);

            using var invalidCharset =
                new StringContent(json, Encoding.UTF8, "application/json");
            invalidCharset.Headers.ContentType!.CharSet = "does-not-exist";
            using HttpResponseMessage invalidCharsetResponse =
                await client.PostAsync("/logs", invalidCharset);
            Assert.AreEqual(
                HttpStatusCode.UnsupportedMediaType,
                invalidCharsetResponse.StatusCode);
        }

        [TestMethod]
        public async Task ReceiverWireJsonIsIndependentFromGlobalHostJsonOptions()
        {
            BatchCapture capture = new BatchCapture();
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton(capture);
            builder.Services.ConfigureHttpJsonOptions(
                options =>
                {
                    options.SerializerOptions.PropertyNamingPolicy =
                        JsonNamingPolicy.SnakeCaseLower;
                    options.SerializerOptions.PropertyNameCaseInsensitive = false;
                });
            builder.Services.AddSerilogRelayReceiver<CapturingHandler>();

            await using WebApplication app = builder.Build();
            app.MapSerilogRelayReceiver<CapturingHandler>("/logs");

            using HttpClient client = await StartClientAsync(app);

            SerilogRelayBatch batch = CreateBatch("App.One", "App.Two");
            string senderCompatibleJson = JsonSerializer.Serialize(
                batch,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            using var content =
                new StringContent(
                    senderCompatibleJson,
                    Encoding.UTF8,
                    "application/json");
            using HttpResponseMessage response =
                await client.PostAsync("/logs", content);

            Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);

            SerilogRelayBatch received =
                await capture.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(batch.BatchId, received.BatchId);
            Assert.AreEqual(batch.Count, received.Count);
            CollectionAssert.AreEqual(
                new[] { "App.One", "App.Two" },
                received.Logs!.Select(logEvent => logEvent.ApplicationId).ToArray());
        }

        private static WebApplication CreateApplication(
            BatchCapture capture,
            string? bearerToken,
            int maximumBatchEvents)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton(capture);
            builder.Services.AddSerilogRelayReceiver<CapturingHandler>();

            WebApplication app = builder.Build();
            app.MapSerilogRelayReceiver<CapturingHandler>(
                "/logs",
                options =>
                {
                    options.BearerToken = bearerToken;
                    options.MaximumBatchEvents = maximumBatchEvents;
                });

            return app;
        }

        private static async Task<HttpClient> StartClientAsync(WebApplication app)
        {
            await app.StartAsync();

            IServer server = app.Services.GetRequiredService<IServer>();
            IServerAddressesFeature addresses =
                server.Features.Get<IServerAddressesFeature>()
                ?? throw new InvalidOperationException("Server addresses are unavailable.");
            string address = addresses.Addresses.Single();

            return new HttpClient
            {
                BaseAddress = new Uri(address),
            };
        }

        private static SerilogRelayBatch CreateBatch(params string[] applicationIds)
        {
            var logs = new List<SerilogRelayEvent>();
            for (int index = 0; index < applicationIds.Length; index++)
            {
                logs.Add(
                    new SerilogRelayEvent
                    {
                        Id = index + 1,
                        EventId = Guid.NewGuid().ToString("D"),
                        ApplicationId = applicationIds[index],
                        MachineId = "machine",
                        ProcessId = index + 100,
                        Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                        Level = "Information",
                        RenderMessage = $"message {index}",
                        MessageTemplate = "message {Index}",
                        TraceId = "trace",
                        SpanId = "span",
                        Exception = null,
                        Properties = "{}",
                    });
            }

            return new SerilogRelayBatch
            {
                ProtocolVersion = 1,
                BatchId = Guid.NewGuid().ToString("D"),
                Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                Count = logs.Count,
                Logs = logs,
            };
        }

        private sealed class BatchCapture
        {
            internal TaskCompletionSource<SerilogRelayBatch> Received { get; } =
                new TaskCompletionSource<SerilogRelayBatch>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class CapturingHandler : ISerilogRelayBatchHandler
        {
            private readonly BatchCapture _capture;

            public CapturingHandler(BatchCapture capture)
            {
                _capture = capture;
            }

            public ValueTask HandleAsync(
                SerilogRelayBatch batch,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _capture.Received.TrySetResult(batch);
                return ValueTask.CompletedTask;
            }
        }
    }
}
