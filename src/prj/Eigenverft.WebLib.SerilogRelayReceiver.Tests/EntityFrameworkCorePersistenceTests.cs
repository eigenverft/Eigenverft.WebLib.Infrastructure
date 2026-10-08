using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Eigenverft.NetLib.SerilogRelay;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog;

namespace Eigenverft.WebLib.SerilogRelayReceiver.Tests
{
    /// <summary>
    /// Exercises the product EF Core integration with SQLite as the concrete test provider.
    /// Provider selection, connection configuration, and migrations remain host-owned.
    /// </summary>
    [TestClass]
    public sealed class EntityFrameworkCorePersistenceTests
    {
        [TestMethod]
        public void EntityFrameworkCorePublicGuardsRejectNullInfrastructure()
        {
            Assert.ThrowsExactly<ArgumentNullException>(
                () => SerilogRelayReceiverEntityFrameworkCoreServiceCollectionExtensions
                    .AddSerilogRelayReceiverEntityFrameworkCore<TestRelayDbContext>(null!));

            Assert.ThrowsExactly<ArgumentNullException>(
                () => SerilogRelayReceiverModelBuilderExtensions
                    .ConfigureSerilogRelayReceiver(null!));

            Assert.ThrowsExactly<ArgumentNullException>(
                () => new EntityFrameworkCoreSerilogRelayBatchHandler<TestRelayDbContext>(null!));

            Assert.ThrowsExactly<ArgumentNullException>(
                () => SerilogRelayReceiverEntityFrameworkCoreEndpointRouteBuilderExtensions
                    .MapSerilogRelayReceiverEntityFrameworkCore<TestRelayDbContext>(
                        null!,
                        "/logs"));
        }

        [TestMethod]
        public async Task EntityFrameworkCoreReceiverPersistsCompleteBatchAndRepeatedReceives()
        {
            string databasePath = CreateDatabasePath();
            var control = new SaveControl();
            WebApplication app = await CreateApplicationAsync(databasePath, control);

            try
            {
                using HttpClient client = await StartClientAsync(app);
                SerilogRelayBatch batch = CreateBatch("App.One", "App.Two");
                batch.Logs![0].Exception = "exception-text";
                batch.Logs[0].Properties = "{\"Property\":\"Value\"}";

                string firstBatchId = batch.BatchId;
                string repeatedEventId = batch.Logs[0].EventId;
                string? originalMessage = batch.Logs[0].RenderMessage;
                DateTimeOffset beforeReceive = DateTimeOffset.UtcNow;

                using HttpResponseMessage firstResponse =
                    await client.PostAsJsonAsync("/logs", batch);
                Assert.AreEqual(HttpStatusCode.NoContent, firstResponse.StatusCode);

                batch.BatchId = Guid.NewGuid().ToString("D");
                batch.Logs[0].RenderMessage = "changed repeated receive";

                using HttpResponseMessage secondResponse =
                    await client.PostAsJsonAsync("/logs", batch);
                Assert.AreEqual(HttpStatusCode.NoContent, secondResponse.StatusCode);

                DateTimeOffset afterReceive = DateTimeOffset.UtcNow;
                List<SerilogRelayReceivedEvent> rows =
                    await ReadRowsAsync(databasePath);

                Assert.AreEqual(4, rows.Count);
                CollectionAssert.AreEquivalent(
                    new[] { "App.One", "App.Two" },
                    rows.Select(row => row.ApplicationId).Distinct().ToArray());

                SerilogRelayReceivedEvent first = rows[0];
                Assert.IsTrue(first.ReceiveId > 0);
                Assert.AreEqual(1, first.ProtocolVersion);
                Assert.AreEqual(firstBatchId, first.BatchId);
                Assert.AreEqual(batch.Timestamp, first.BatchTimestamp);
                Assert.AreEqual(2, first.BatchCount);
                Assert.AreEqual(1L, first.SenderLocalId);
                Assert.AreEqual(repeatedEventId, first.EventId);
                Assert.AreEqual("App.One", first.ApplicationId);
                Assert.AreEqual("machine", first.MachineId);
                Assert.AreEqual(100, first.ProcessId);
                Assert.AreEqual(batch.Logs[0].Timestamp, first.Timestamp);
                Assert.AreEqual("Information", first.Level);
                Assert.AreEqual(originalMessage, first.RenderMessage);
                Assert.AreEqual("message {Index}", first.MessageTemplate);
                Assert.AreEqual("trace", first.TraceId);
                Assert.AreEqual("span", first.SpanId);
                Assert.AreEqual("exception-text", first.Exception);
                Assert.AreEqual("{\"Property\":\"Value\"}", first.Properties);
                Assert.IsTrue(first.ReceivedAtUtc >= beforeReceive);
                Assert.IsTrue(first.ReceivedAtUtc <= afterReceive);

                SerilogRelayReceivedEvent[] repeatedRows = rows
                    .Where(row => row.EventId == repeatedEventId)
                    .OrderBy(row => row.ReceiveId)
                    .ToArray();

                Assert.AreEqual(2, repeatedRows.Length);
                Assert.AreEqual(originalMessage, repeatedRows[0].RenderMessage);
                Assert.AreEqual(
                    "changed repeated receive",
                    repeatedRows[1].RenderMessage);
                Assert.AreNotEqual(
                    repeatedRows[0].BatchId,
                    repeatedRows[1].BatchId);
                Assert.AreNotEqual(
                    repeatedRows[0].ReceiveId,
                    repeatedRows[1].ReceiveId);
            }
            finally
            {
                await DisposeApplicationAndDatabaseAsync(app, databasePath);
            }
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        public async Task EntityFrameworkCoreReceiverPreservesNullablePayloadFields(string? value)
        {
            string databasePath = CreateDatabasePath();
            var control = new SaveControl();
            WebApplication app = await CreateApplicationAsync(databasePath, control);

            try
            {
                using HttpClient client = await StartClientAsync(app);
                SerilogRelayBatch batch = CreateBatch("App.One");
                SerilogRelayEvent logEvent = batch.Logs![0];
                batch.Timestamp = value;
                logEvent.Timestamp = value;
                logEvent.Level = value;
                logEvent.RenderMessage = value;
                logEvent.MessageTemplate = value;
                logEvent.MachineId = value;
                logEvent.TraceId = value;
                logEvent.SpanId = value;
                logEvent.Exception = value;
                logEvent.Properties = value;

                using HttpResponseMessage response = await client.PostAsJsonAsync("/logs", batch);
                Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);

                List<SerilogRelayReceivedEvent> rows = await ReadRowsAsync(databasePath);
                Assert.AreEqual(1, rows.Count);
                SerilogRelayReceivedEvent row = rows[0];
                Assert.AreEqual(batch.BatchId, row.BatchId);
                Assert.AreEqual(logEvent.EventId, row.EventId);
                Assert.AreEqual(logEvent.ApplicationId, row.ApplicationId);
                Assert.AreEqual(value, row.BatchTimestamp);
                Assert.AreEqual(value, row.Timestamp);
                Assert.AreEqual(value, row.Level);
                Assert.AreEqual(value, row.RenderMessage);
                Assert.AreEqual(value, row.MessageTemplate);
                Assert.AreEqual(value, row.MachineId);
                Assert.AreEqual(value, row.TraceId);
                Assert.AreEqual(value, row.SpanId);
                Assert.AreEqual(value, row.Exception);
                Assert.AreEqual(value, row.Properties);
            }
            finally
            {
                await DisposeApplicationAndDatabaseAsync(app, databasePath);
            }
        }

        [TestMethod]
        [DataRow(32)]
        [DataRow(33)]
        [DataRow(64)]
        [DataRow(65)]
        [DataRow(256)]
        [DataRow(257)]
        public async Task EntityFrameworkCoreReceiverFitsBoundedMetadataToStorage(int length)
        {
            string databasePath = CreateDatabasePath();
            var control = new SaveControl();
            WebApplication app = await CreateApplicationAsync(databasePath, control);

            try
            {
                using HttpClient client = await StartClientAsync(app);
                SerilogRelayBatch batch = CreateBatch("App.One");
                string metadata = new string('x', length);
                batch.Timestamp = metadata;
                SerilogRelayEvent logEvent = batch.Logs![0];
                logEvent.MachineId = metadata;
                logEvent.Timestamp = metadata;
                logEvent.Level = metadata;
                logEvent.TraceId = metadata;
                logEvent.SpanId = metadata;
                logEvent.RenderMessage = new string('m', 300);

                using HttpResponseMessage response =
                    await client.PostAsJsonAsync("/logs", batch);
                Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);

                SerilogRelayReceivedEvent row = (await ReadRowsAsync(databasePath)).Single();
                Assert.AreEqual(metadata[..Math.Min(length, 64)], row.BatchTimestamp);
                Assert.AreEqual(metadata[..Math.Min(length, 256)], row.MachineId);
                Assert.AreEqual(metadata[..Math.Min(length, 64)], row.Timestamp);
                Assert.AreEqual(metadata[..Math.Min(length, 32)], row.Level);
                Assert.AreEqual(metadata[..Math.Min(length, 64)], row.TraceId);
                Assert.AreEqual(metadata[..Math.Min(length, 32)], row.SpanId);
                Assert.AreEqual(logEvent.RenderMessage, row.RenderMessage);
                Assert.AreEqual(logEvent.EventId, row.EventId);
                Assert.AreEqual(logEvent.ApplicationId, row.ApplicationId);
            }
            finally
            {
                await DisposeApplicationAndDatabaseAsync(app, databasePath);
            }
        }

        [TestMethod]
        public async Task EntityFrameworkCoreTruncationKeepsUnicodeCharactersIntact()
        {
            string databasePath = CreateDatabasePath();
            var control = new SaveControl();
            WebApplication app = await CreateApplicationAsync(databasePath, control);

            try
            {
                using HttpClient client = await StartClientAsync(app);
                SerilogRelayBatch batch = CreateBatch("App.One");
                batch.Timestamp = new string('x', 63) + "\U0001F600suffix";
                SerilogRelayEvent logEvent = batch.Logs![0];
                logEvent.MachineId = new string('x', 255) + "\U0001F600suffix";
                logEvent.Timestamp = new string('x', 63) + "\U0001F600suffix";
                logEvent.Level = new string('x', 31) + "\U0001F600suffix";
                logEvent.TraceId = new string('x', 63) + "\U0001F600suffix";
                logEvent.SpanId = new string('x', 31) + "\U0001F600suffix";

                using HttpResponseMessage response =
                    await client.PostAsJsonAsync("/logs", batch);
                Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);

                SerilogRelayReceivedEvent row = (await ReadRowsAsync(databasePath)).Single();
                Assert.AreEqual(new string('x', 63), row.BatchTimestamp);
                Assert.AreEqual(new string('x', 255), row.MachineId);
                Assert.AreEqual(new string('x', 63), row.Timestamp);
                Assert.AreEqual(new string('x', 31), row.Level);
                Assert.AreEqual(new string('x', 63), row.TraceId);
                Assert.AreEqual(new string('x', 31), row.SpanId);
            }
            finally
            {
                await DisposeApplicationAndDatabaseAsync(app, databasePath);
            }
        }

        [TestMethod]
        public async Task EntityFrameworkCoreReceiverPreservesMixedVersionsAndLegacyNull()
        {
            string databasePath = CreateDatabasePath();
            var control = new SaveControl();
            WebApplication app = await CreateApplicationAsync(databasePath, control);

            try
            {
                using HttpClient client = await StartClientAsync(app);
                string applicationId = new string('a', 255);
                SerilogRelayBatch batch = CreateBatch(applicationId, applicationId, applicationId);
                batch.Logs![0].ApplicationVersion = "1.0.0+old-commit";
                batch.Logs[1].ApplicationVersion = new string('v', 255);
                var wireOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                };

                using HttpResponseMessage response =
                    await client.PostAsJsonAsync("/logs", batch, wireOptions);
                Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);

                List<SerilogRelayReceivedEvent> rows = await ReadRowsAsync(databasePath);
                Assert.AreEqual(3, rows.Count);
                CollectionAssert.AreEqual(
                    batch.Logs.Select(logEvent => logEvent.ApplicationVersion).ToArray(),
                    rows.Select(row => row.ApplicationVersion).ToArray());
                Assert.IsTrue(rows.All(row => row.ApplicationId == applicationId));
                CollectionAssert.AreEqual(
                    batch.Logs.Select(logEvent => logEvent.EventId).ToArray(),
                    rows.Select(row => row.EventId).ToArray());
            }
            finally
            {
                await DisposeApplicationAndDatabaseAsync(app, databasePath);
            }
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task ReleasedSenderDrainsSharedSpoolWithoutReplacingOriginVersion(bool useMaximumLengths)
        {
            string databasePath = CreateDatabasePath();
            string spoolDirectory = databasePath + ".spool";
            string applicationId = useMaximumLengths ? new string('a', 255) : "Shared.App";
            string newVersion = useMaximumLengths ? new string('v', 255) : "2.0.0+new-commit";
            var control = new SaveControl();
            WebApplication app = await CreateApplicationAsync(databasePath, control);

            try
            {
                using HttpClient client = await StartClientAsync(app);
                var oldOptions = new SerilogRelayOptions
                {
                    ApplicationVersion = "1.0.0+old-commit",
                };
                oldOptions.Delivery.ShutdownTimeout = TimeSpan.Zero;

                // The older application version has no endpoint and leaves its event in the shared spool.
                using (var oldLogger = new LoggerConfiguration()
                    .WriteTo.SerilogRelay(
                        spoolDirectory: spoolDirectory,
                        applicationId: applicationId,
                        options: oldOptions)
                    .CreateLogger())
                {
                    oldLogger.Information("Event from old application");
                }

                var newOptions = new SerilogRelayOptions
                {
                    ApplicationVersion = newVersion,
                };
                newOptions.Delivery.MinimumBatchEvents = 1;
                newOptions.Delivery.ShutdownTimeout = TimeSpan.FromSeconds(10);
                newOptions.Delivery.ShutdownRequestTimeout = TimeSpan.FromSeconds(5);

                using (var newLogger = new LoggerConfiguration()
                    .WriteTo.SerilogRelay(
                        endpoint: new Uri(client.BaseAddress!, "/logs").ToString(),
                        spoolDirectory: spoolDirectory,
                        applicationId: applicationId,
                        options: newOptions)
                    .CreateLogger())
                {
                    newLogger.Information("Event from new application");
                }

                List<SerilogRelayReceivedEvent> rows = await ReadRowsAsync(databasePath);
                Assert.AreEqual(2, rows.Count);
                Assert.IsTrue(rows.All(row => row.ApplicationId == applicationId));
                Assert.AreEqual(
                    "1.0.0+old-commit",
                    rows.Single(row => row.RenderMessage == "Event from old application").ApplicationVersion);
                Assert.AreEqual(
                    newVersion,
                    rows.Single(row => row.RenderMessage == "Event from new application").ApplicationVersion);
            }
            finally
            {
                await DisposeApplicationAndDatabaseAsync(app, databasePath);
                if (Directory.Exists(spoolDirectory))
                {
                    // This unique test directory contains only sender spool files.
                    foreach (string path in Directory.EnumerateFiles(spoolDirectory))
                        File.Delete(path);
                    Directory.Delete(spoolDirectory);
                }
            }
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task ReleasedSenderDefaultsDeliverFullSpoolAndEmergencyBatches(bool emergency)
        {
            string databasePath = CreateDatabasePath();
            string spoolPath = databasePath + ".spool";
            var control = new SaveControl { PauseBeforeSave = emergency };
            WebApplication app = await CreateApplicationAsync(databasePath, control);
            var senderDefaults = new SerilogRelayOptions();
            int batchCount = emergency
                ? senderDefaults.Delivery.EmergencyMaximumBatchEvents
                : senderDefaults.Delivery.MaximumBatchEvents;

            try
            {
                using HttpClient client = await StartClientAsync(app);
                string endpoint = new Uri(client.BaseAddress!, "/logs").ToString();
                if (emergency)
                    File.WriteAllText(spoolPath, "A file prevents creating the spool directory.");

                // Both sides use unmodified options. A file-backed obstruction forces the RAM path.
                using (var logger = new LoggerConfiguration()
                    .WriteTo.SerilogRelay(
                        endpoint: emergency ? endpoint : null,
                        spoolDirectory: spoolPath,
                        applicationId: "Default.Pair")
                    .CreateLogger())
                {
                    try
                    {
                        if (emergency)
                        {
                            logger.Information("First emergency event");
                            await control.BeforeSaveReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
                        }

                        for (int index = 0; index < batchCount; index++)
                            logger.Information("Default batch event {Index}", index);
                    }
                    finally
                    {
                        control.ContinueSave.TrySetResult(true);
                    }
                }

                if (!emergency)
                {
                    // Startup/shutdown drains the full durable batch using the sender defaults.
                    using var logger = new LoggerConfiguration()
                        .WriteTo.SerilogRelay(
                            endpoint: endpoint,
                            spoolDirectory: spoolPath,
                            applicationId: "Default.Pair")
                        .CreateLogger();
                }

                List<SerilogRelayReceivedEvent> rows = await ReadRowsAsync(databasePath);
                Assert.AreEqual(batchCount + (emergency ? 1 : 0), rows.Count);
                Assert.IsTrue(rows.All(row => row.ApplicationId == "Default.Pair"));
                Assert.AreEqual(batchCount, rows.Count(row => row.BatchCount == batchCount));
                foreach (var batch in rows.GroupBy(row => row.BatchId))
                    Assert.IsTrue(batch.All(row => row.BatchCount == batch.Count()));
            }
            finally
            {
                control.ContinueSave.TrySetResult(true);
                await DisposeApplicationAndDatabaseAsync(app, databasePath);
                if (File.Exists(spoolPath))
                    File.Delete(spoolPath);
                if (Directory.Exists(spoolPath))
                {
                    foreach (string path in Directory.EnumerateFiles(spoolPath))
                        File.Delete(path);
                    Directory.Delete(spoolPath);
                }
            }
        }

        [TestMethod]
        public async Task EntityFrameworkCoreReceiverCompletesSaveBeforeReturningSuccess()
        {
            string databasePath = CreateDatabasePath();
            var control = new SaveControl
            {
                PauseBeforeSave = true,
            };
            WebApplication app = await CreateApplicationAsync(databasePath, control);

            try
            {
                using HttpClient client = await StartClientAsync(app);

                Task<HttpResponseMessage> responseTask =
                    client.PostAsJsonAsync(
                        "/logs",
                        CreateBatch("App.One", "App.Two"));

                await control.BeforeSaveReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.IsFalse(responseTask.IsCompleted);

                control.ContinueSave.TrySetResult(true);
                await control.Saved.Task.WaitAsync(TimeSpan.FromSeconds(5));

                using HttpResponseMessage response =
                    await responseTask.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);

                List<SerilogRelayReceivedEvent> rows =
                    await ReadRowsAsync(databasePath);
                Assert.AreEqual(2, rows.Count);
            }
            finally
            {
                control.ContinueSave.TrySetResult(true);
                await DisposeApplicationAndDatabaseAsync(app, databasePath);
            }
        }

        [TestMethod]
        public async Task ClientCancellationAfterValidatedHandoffDoesNotCancelEfCoreSave()
        {
            string databasePath = CreateDatabasePath();
            var control = new SaveControl
            {
                PauseBeforeSave = true,
            };
            WebApplication app = await CreateApplicationAsync(databasePath, control);

            try
            {
                using HttpClient client = await StartClientAsync(app);
                using var clientCancellation = new CancellationTokenSource();

                Task<HttpResponseMessage> responseTask =
                    client.PostAsJsonAsync(
                        "/logs",
                        CreateBatch("App.One", "App.Two"),
                        clientCancellation.Token);

                await control.BeforeSaveReached.Task.WaitAsync(TimeSpan.FromSeconds(5));

                clientCancellation.Cancel();

                await Assert.ThrowsExactlyAsync<TaskCanceledException>(
                    async () =>
                    {
                        using HttpResponseMessage ignored =
                            await responseTask.WaitAsync(TimeSpan.FromSeconds(5));
                    });

                Assert.IsFalse(control.LastSaveCancellationToken.IsCancellationRequested);

                control.ContinueSave.TrySetResult(true);
                await control.Saved.Task.WaitAsync(TimeSpan.FromSeconds(5));

                List<SerilogRelayReceivedEvent> rows =
                    await ReadRowsAsync(databasePath);
                Assert.AreEqual(2, rows.Count);
            }
            finally
            {
                control.ContinueSave.TrySetResult(true);
                await DisposeApplicationAndDatabaseAsync(app, databasePath);
            }
        }

        [TestMethod]
        public async Task HostShutdownCancelsEfCoreSaveAndReturnsServiceUnavailable()
        {
            string databasePath = CreateDatabasePath();
            var control = new SaveControl
            {
                PauseBeforeSave = true,
            };
            WebApplication app = await CreateApplicationAsync(databasePath, control);

            try
            {
                using HttpClient client = await StartClientAsync(app);

                Task<HttpResponseMessage> responseTask =
                    client.PostAsJsonAsync(
                        "/logs",
                        CreateBatch("App.One", "App.Two"));

                await control.BeforeSaveReached.Task.WaitAsync(TimeSpan.FromSeconds(5));

                IHostApplicationLifetime lifetime =
                    app.Services.GetRequiredService<IHostApplicationLifetime>();
                lifetime.StopApplication();

                using HttpResponseMessage response =
                    await responseTask.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.AreEqual(
                    HttpStatusCode.ServiceUnavailable,
                    response.StatusCode);
                Assert.IsTrue(control.LastSaveCancellationToken.IsCancellationRequested);

                List<SerilogRelayReceivedEvent> rows =
                    await ReadRowsAsync(databasePath);
                Assert.AreEqual(0, rows.Count);
            }
            finally
            {
                control.ContinueSave.TrySetResult(true);
                await DisposeApplicationAndDatabaseAsync(app, databasePath);
            }
        }

        [TestMethod]
        public async Task EfCoreSaveFailureRollsBackTheWholeBatch()
        {
            string databasePath = CreateDatabasePath();
            var control = new SaveControl();
            WebApplication app = await CreateApplicationAsync(databasePath, control);

            try
            {
                await InstallRejectSecondApplicationTriggerAsync(app);
                using HttpClient client = await StartClientAsync(app);

                using HttpResponseMessage response =
                    await client.PostAsJsonAsync(
                        "/logs",
                        CreateBatch("App.One", "App.Two"));

                Assert.AreEqual(
                    HttpStatusCode.InternalServerError,
                    response.StatusCode);

                List<SerilogRelayReceivedEvent> rows =
                    await ReadRowsAsync(databasePath);
                Assert.AreEqual(0, rows.Count);
            }
            finally
            {
                await DisposeApplicationAndDatabaseAsync(app, databasePath);
            }
        }

        private static async Task<WebApplication> CreateApplicationAsync(
            string databasePath,
            SaveControl control)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            builder.Services.AddDbContextFactory<TestRelayDbContext>(
                options =>
                {
                    options.UseSqlite($"Data Source={databasePath}");
                    options.AddInterceptors(
                        new ControlledSaveChangesInterceptor(control));
                });
            builder.Services
                .AddSerilogRelayReceiverEntityFrameworkCore<TestRelayDbContext>();

            WebApplication app = builder.Build();

            IDbContextFactory<TestRelayDbContext> databaseFactory =
                app.Services.GetRequiredService<IDbContextFactory<TestRelayDbContext>>();
            await using (TestRelayDbContext database =
                await databaseFactory.CreateDbContextAsync())
            {
                await database.Database.EnsureCreatedAsync();
            }

            app.MapSerilogRelayReceiverEntityFrameworkCore<TestRelayDbContext>(
                "/logs");
            return app;
        }

        private static async Task InstallRejectSecondApplicationTriggerAsync(
            WebApplication app)
        {
            IDbContextFactory<TestRelayDbContext> databaseFactory =
                app.Services.GetRequiredService<IDbContextFactory<TestRelayDbContext>>();
            await using TestRelayDbContext database =
                await databaseFactory.CreateDbContextAsync();

            await database.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER RejectAppTwo
                BEFORE INSERT ON "SerilogRelayReceivedEvents"
                WHEN NEW."ApplicationId" = 'App.Two'
                BEGIN
                    SELECT RAISE(ABORT, 'App.Two rejected for rollback test');
                END;
                """);
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

        private static async Task<List<SerilogRelayReceivedEvent>> ReadRowsAsync(
            string databasePath)
        {
            var options = new DbContextOptionsBuilder<TestRelayDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using var database = new TestRelayDbContext(options);
            return await database.Set<SerilogRelayReceivedEvent>()
                .AsNoTracking()
                .OrderBy(row => row.ReceiveId)
                .ToListAsync();
        }

        private static async Task DisposeApplicationAndDatabaseAsync(
            WebApplication app,
            string databasePath)
        {
            await app.DisposeAsync();
            SqliteConnection.ClearAllPools();

            DeleteIfExists(databasePath);
            DeleteIfExists(databasePath + "-wal");
            DeleteIfExists(databasePath + "-shm");
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        private static string CreateDatabasePath()
            => Path.Combine(
                Path.GetTempPath(),
                $"Eigenverft.WebLib.SerilogRelayReceiver.Tests-{Guid.NewGuid():N}.db");

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

        private sealed class SaveControl
        {
            internal bool PauseBeforeSave { get; set; }

            internal CancellationToken LastSaveCancellationToken { get; set; }

            internal TaskCompletionSource<bool> BeforeSaveReached { get; } =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            internal TaskCompletionSource<bool> ContinueSave { get; } =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            internal TaskCompletionSource<bool> Saved { get; } =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

        }

        private sealed class ControlledSaveChangesInterceptor : SaveChangesInterceptor
        {
            private readonly SaveControl _control;

            internal ControlledSaveChangesInterceptor(SaveControl control)
            {
                _control = control;
            }

            public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
                DbContextEventData eventData,
                InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
            {
                _control.LastSaveCancellationToken = cancellationToken;
                _control.BeforeSaveReached.TrySetResult(true);

                if (_control.PauseBeforeSave)
                {
                    await _control.ContinueSave.Task
                        .WaitAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                return result;
            }

            public override ValueTask<int> SavedChangesAsync(
                SaveChangesCompletedEventData eventData,
                int result,
                CancellationToken cancellationToken = default)
            {
                _control.Saved.TrySetResult(true);
                return ValueTask.FromResult(result);
            }
        }

        private sealed class TestRelayDbContext : DbContext
        {
            public TestRelayDbContext(
                DbContextOptions<TestRelayDbContext> options)
                : base(options)
            {
            }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.ConfigureSerilogRelayReceiver();
            }
        }
    }
}
