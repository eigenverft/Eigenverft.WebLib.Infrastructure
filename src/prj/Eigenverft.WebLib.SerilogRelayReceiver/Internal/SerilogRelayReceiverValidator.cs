using System;

namespace Eigenverft.WebLib.SerilogRelayReceiver
{
    internal static class SerilogRelayReceiverValidator
    {
        internal const int CurrentProtocolVersion = 1;

        internal static string? Validate(
            SerilogRelayBatch batch,
            int maximumBatchEvents)
        {
            if (batch.ProtocolVersion != CurrentProtocolVersion)
            {
                return $"Unsupported protocolVersion {batch.ProtocolVersion}; expected {CurrentProtocolVersion}.";
            }

            if (!Guid.TryParseExact(batch.BatchId, "D", out _))
                return "batchId must be a canonical GUID.";

            if (batch.Logs is null)
                return "logs must not be null.";

            if (batch.Count != batch.Logs.Count)
                return "count must match the number of logs.";

            if (batch.Logs.Count > maximumBatchEvents)
            {
                return $"Batch contains {batch.Logs.Count} events; maximum is {maximumBatchEvents}.";
            }

            foreach (SerilogRelayEvent logEvent in batch.Logs)
            {
                if (!Guid.TryParseExact(logEvent.EventId, "D", out _))
                    return "Every log event must contain a canonical eventId GUID.";

                if (string.IsNullOrWhiteSpace(logEvent.ApplicationId))
                    return "Every log event must contain a non-empty applicationId.";

                if (logEvent.ProcessId <= 0)
                    return "Every log event must contain a positive processId.";
            }

            return null;
        }
    }
}
