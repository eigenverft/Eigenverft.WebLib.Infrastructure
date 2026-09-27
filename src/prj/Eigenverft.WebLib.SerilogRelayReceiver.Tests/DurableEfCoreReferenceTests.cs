using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Eigenverft.WebLib.SerilogRelayReceiver.Tests
{
    /// <summary>
    /// Exercises one concrete durable handler without making EF Core or SQLite part of the
    /// receiver package. The host owns the DbContext/provider choice.
    /// </summary>
    [TestClass]
    public sealed class DurableEfCoreReferenceTests
    {
        [TestMethod]
        public async Task ReferenceHandlerPreservesRepeatedReceivesAndUsesServerLifetimeToken()
        {
            string databasePath = CreateDatabasePath();
            var control = new ReferenceHandlerControl();
            WebApplication app = await CreateApplicationAsync(databasePath, control);

            try
            {
                using HttpClient client = await StartClientAsync(app);
                SerilogRelayBatch batch = CreateBatch("App.One", "App.Two");
                string repeatedEventId = batch.Logs![0].EventId;
                string originalMessage = batch.Logs[0].RenderMessage;

                using HttpResponseMessage firstResponse =
                    await client.PostAsJsonAsync("/logs", batch);
                Assert.AreEqual(HttpStatusCode.NoContent, firstResponse.StatusCode);

                batch.BatchId = Guid.NewGuid().ToString("D");
                batch.Logs[0].RenderMessage = "changed repeated receive";

                using HttpResponseMessage secondResponse =
                    await client.PostAsJsonAsync("/logs", batch);
                Assert.AreEqual(HttpStatusCode.NoContent, secondResponse.StatusCode);

                IHostApplicationLifetime lifetime =
                    app.Services.GetRequiredService<IHostApplicationLifetime>();
                Assert.AreEqual(lifetime.ApplicationStopping, control.LastCancellationToken);
                Assert.IsFalse(control.LastCancellationToken.IsCancellationRequested);

                List<ReferenceReceivedEvent> rows =
                    await ReadRowsAsync(databasePath);

                Assert.AreEqual(4, rows.Count);
                CollectionAssert.AreEquivalent(
                    new[] { "App.One", "App.Two" },
                    rows.Select(row => row.ApplicationId).Distinct().ToArray());

                ReferenceReceivedEvent[] repeatedRows = rows
                    .Where(row => row.EventId == repeatedEventId)
                    .OrderBy(row => row.ReceiveId)
                    .ToArray();

                Assert.AreEqual(2, repeatedRows.Length);
                Assert.AreEqual(originalMessage, repeatedRows[0].RenderMessage);
                Assert.AreEqual("changed repeated receive", repeatedRows[1].RenderMessage);
                Assert.AreNotEqual(repeatedRows[0].BatchId, repeatedRows[1].BatchId);
            }
            finally
            {
                await DisposeApplicationAndDatabaseAsync(app, databasePath);
            }
        }

        [TestMethod]
        public async Task ReferenceHandlerCommitsWholeBatchBeforeEndpointReturnsSuccess()
        {
            string databasePath = CreateDatabasePath();
            var control = new ReferenceHandlerControl
            {
                PauseBeforeCommit = true,
            };
            WebApplication app = await CreateApplicationAsync(databasePath, control);

            try
            {
                using HttpClient client = await StartClientAsync(app);

                Task<HttpResponseMessage> responseTask =
                    client.PostAsJsonAsync("/logs", CreateBatch("App.One", "App.Two"));

                await control.BeforeCommitReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.IsFalse(responseTask.IsCompleted);

                control.ContinueCommit.TrySetResult(true);

                using HttpResponseMessage response =
                    await responseTask.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);

                List<ReferenceReceivedEvent> rows =
                    await ReadRowsAsync(databasePath);
                Assert.AreEqual(2, rows.Count);
            }
            finally
            {
                control.ContinueCommit.TrySetResult(true);
                await DisposeApplicationAndDatabaseAsync(app, databasePath);
            }
        }

        [TestMethod]
        public async Task ReferenceHandlerRollsBackWholeBatchWhenDurableHandlingFails()
        {
            string databasePath = CreateDatabasePath();
            var control = new ReferenceHandlerControl
            {
                FailAfterSave = true,
            };
            WebApplication app = await CreateApplicationAsync(databasePath, control);

            try
            {
                using HttpClient client = await StartClientAsync(app);

                using HttpResponseMessage response =
                    await client.PostAsJsonAsync(
                        "/logs",
                        CreateBatch("App.One", "App.Two"));

                Assert.AreEqual(
                    HttpStatusCode.InternalServerError,
                    response.StatusCode);

                List<ReferenceReceivedEvent> rows =
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
            ReferenceHandlerControl control)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            builder.Services.AddSingleton(control);
            builder.Services.AddDbContext<ReferenceDbContext>(
                options => options.UseSqlite($"Data Source={databasePath}"));
            builder.Services.AddSerilogRelayReceiver<ReferenceDurableHandler>();

            WebApplication app = builder.Build();

            using (IServiceScope scope = app.Services.CreateScope())
            {
                ReferenceDbContext database =
                    scope.ServiceProvider.GetRequiredService<ReferenceDbContext>();
                await database.Database.EnsureCreatedAsync();
            }

            app.MapSerilogRelayReceiver<ReferenceDurableHandler>("/logs");
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

        private static async Task<List<ReferenceReceivedEvent>> ReadRowsAsync(
            string databasePath)
        {
            var options = new DbContextOptionsBuilder<ReferenceDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using var database = new ReferenceDbContext(options);
            return await database.ReceivedEvents
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

        private sealed class ReferenceHandlerControl
        {
            internal bool FailAfterSave { get; set; }

            internal bool PauseBeforeCommit { get; set; }

            internal CancellationToken LastCancellationToken { get; set; }

            internal TaskCompletionSource<bool> BeforeCommitReached { get; } =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            internal TaskCompletionSource<bool> ContinueCommit { get; } =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class ReferenceDurableHandler : ISerilogRelayBatchHandler
        {
            private readonly ReferenceDbContext _database;
            private readonly ReferenceHandlerControl _control;

            public ReferenceDurableHandler(
                ReferenceDbContext database,
                ReferenceHandlerControl control)
            {
                _database = database;
                _control = control;
            }

            public async ValueTask HandleAsync(
                SerilogRelayBatch batch,
                CancellationToken cancellationToken)
            {
                _control.LastCancellationToken = cancellationToken;

                await using IDbContextTransaction transaction =
                    await _database.Database
                        .BeginTransactionAsync(cancellationToken)
                        .ConfigureAwait(false);

                DateTimeOffset receivedAtUtc = DateTimeOffset.UtcNow;
                foreach (SerilogRelayEvent logEvent in batch.Logs!)
                {
                    _database.ReceivedEvents.Add(
                        new ReferenceReceivedEvent
                        {
                            BatchId = batch.BatchId,
                            BatchTimestamp = batch.Timestamp,
                            SenderLocalId = logEvent.Id,
                            EventId = logEvent.EventId,
                            ApplicationId = logEvent.ApplicationId,
                            MachineId = logEvent.MachineId,
                            ProcessId = logEvent.ProcessId,
                            Timestamp = logEvent.Timestamp,
                            Level = logEvent.Level,
                            RenderMessage = logEvent.RenderMessage,
                            MessageTemplate = logEvent.MessageTemplate,
                            TraceId = logEvent.TraceId,
                            SpanId = logEvent.SpanId,
                            Exception = logEvent.Exception,
                            Properties = logEvent.Properties,
                            ReceivedAtUtc = receivedAtUtc,
                        });
                }

                await _database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                if (_control.PauseBeforeCommit)
                {
                    _control.BeforeCommitReached.TrySetResult(true);
                    await _control.ContinueCommit.Task
                        .WaitAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                if (_control.FailAfterSave)
                    throw new InvalidOperationException("Reference durable handling failure.");

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private sealed class ReferenceDbContext : DbContext
        {
            public ReferenceDbContext(
                DbContextOptions<ReferenceDbContext> options)
                : base(options)
            {
            }

            internal DbSet<ReferenceReceivedEvent> ReceivedEvents
                => Set<ReferenceReceivedEvent>();

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<ReferenceReceivedEvent>(
                    entity =>
                    {
                        entity.ToTable("ReceivedLogEvents");
                        entity.HasKey(row => row.ReceiveId);
                        entity.Property(row => row.ReceiveId).ValueGeneratedOnAdd();

                        // Intentionally non-unique: a repeated EventId is another physical receive.
                        entity.HasIndex(row => row.EventId);
                    });
            }
        }

        private sealed class ReferenceReceivedEvent
        {
            public long ReceiveId { get; set; }

            public string BatchId { get; set; } = string.Empty;

            public string BatchTimestamp { get; set; } = string.Empty;

            public long SenderLocalId { get; set; }

            public string EventId { get; set; } = string.Empty;

            public string ApplicationId { get; set; } = string.Empty;

            public string? MachineId { get; set; }

            public int ProcessId { get; set; }

            public string Timestamp { get; set; } = string.Empty;

            public string Level { get; set; } = string.Empty;

            public string RenderMessage { get; set; } = string.Empty;

            public string MessageTemplate { get; set; } = string.Empty;

            public string? TraceId { get; set; }

            public string? SpanId { get; set; }

            public string? Exception { get; set; }

            public string? Properties { get; set; }

            public DateTimeOffset ReceivedAtUtc { get; set; }
        }
    }
}
