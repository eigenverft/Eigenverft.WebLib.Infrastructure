using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Eigenverft.WebLib.SerilogRelayReceiver
{
    internal sealed class EntityFrameworkCoreSerilogRelayBatchHandler<TDbContext>
        : ISerilogRelayBatchHandler
        where TDbContext : DbContext
    {
        private readonly IDbContextFactory<TDbContext> _databaseFactory;

        public EntityFrameworkCoreSerilogRelayBatchHandler(
            IDbContextFactory<TDbContext> databaseFactory)
        {
            _databaseFactory =
                databaseFactory ?? throw new ArgumentNullException(nameof(databaseFactory));
        }

        public async ValueTask HandleAsync(
            SerilogRelayBatch batch,
            CancellationToken cancellationToken)
        {
            DateTimeOffset receivedAtUtc = DateTimeOffset.UtcNow;
            await using TDbContext database =
                await _databaseFactory
                    .CreateDbContextAsync(cancellationToken)
                    .ConfigureAwait(false);

            var receivedEvents = new List<SerilogRelayReceivedEvent>(batch.Count);

            foreach (SerilogRelayEvent logEvent in batch.Logs!)
            {
                receivedEvents.Add(
                    new SerilogRelayReceivedEvent
                    {
                        ProtocolVersion = batch.ProtocolVersion,
                        BatchId = batch.BatchId,
                        BatchTimestamp = TruncateForStorage(batch.Timestamp, 64),
                        BatchCount = batch.Count,
                        SenderLocalId = logEvent.Id,
                        EventId = logEvent.EventId,
                        ApplicationId = logEvent.ApplicationId,
                        ApplicationVersion = logEvent.ApplicationVersion,
                        MachineId = TruncateForStorage(logEvent.MachineId, 256),
                        ProcessId = logEvent.ProcessId,
                        Timestamp = TruncateForStorage(logEvent.Timestamp, 64),
                        Level = TruncateForStorage(logEvent.Level, 32),
                        RenderMessage = logEvent.RenderMessage,
                        MessageTemplate = logEvent.MessageTemplate,
                        TraceId = TruncateForStorage(logEvent.TraceId, 64),
                        SpanId = TruncateForStorage(logEvent.SpanId, 32),
                        Exception = logEvent.Exception,
                        Properties = logEvent.Properties,
                        ReceivedAtUtc = receivedAtUtc,
                    });
            }

            database.Set<SerilogRelayReceivedEvent>().AddRange(receivedEvents);
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        private static string? TruncateForStorage(string? value, int maximumLength)
        {
            if (value is null || value.Length <= maximumLength)
                return value;

            int length = maximumLength;
            // Keep a surrogate pair intact at the storage boundary.
            if (char.IsHighSurrogate(value[length - 1]))
                length--;

            return value[..length];
        }
    }
}
