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
                        BatchTimestamp = batch.Timestamp,
                        BatchCount = batch.Count,
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

            database.Set<SerilogRelayReceivedEvent>().AddRange(receivedEvents);
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
