using System.Collections.Generic;

namespace Eigenverft.WebLib.SerilogRelayReceiver
{
    /// <summary>
    /// Represents one SerilogRelay protocol batch received over HTTP.
    /// </summary>
    public sealed class SerilogRelayBatch
    {
        /// <summary>
        /// Gets or sets the wire protocol version.
        /// </summary>
        public int ProtocolVersion { get; set; }

        /// <summary>
        /// Gets or sets the canonical batch identifier.
        /// </summary>
        public string BatchId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the sender-generated batch timestamp.
        /// </summary>
        public string Timestamp { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the number of log events declared by the sender.
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// Gets or sets the log events contained in the batch.
        /// </summary>
        public List<SerilogRelayEvent>? Logs { get; set; } = new List<SerilogRelayEvent>();
    }

    /// <summary>
    /// Represents one materialized SerilogRelay log event.
    /// </summary>
    public sealed class SerilogRelayEvent
    {
        /// <summary>
        /// Gets or sets the sender-local durable row identifier.
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Gets or sets the stable canonical event identifier.
        /// </summary>
        public string EventId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the logical application identifier that produced the event.
        /// </summary>
        public string ApplicationId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the optional machine identifier that produced the event.
        /// </summary>
        public string? MachineId { get; set; }

        /// <summary>
        /// Gets or sets the originating operating-system process identifier.
        /// </summary>
        public int ProcessId { get; set; }

        /// <summary>
        /// Gets or sets the event timestamp serialized by the sender.
        /// </summary>
        public string Timestamp { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the serialized Serilog level name.
        /// </summary>
        public string Level { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the rendered log message.
        /// </summary>
        public string RenderMessage { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the original Serilog message template.
        /// </summary>
        public string MessageTemplate { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the optional distributed-tracing trace identifier.
        /// </summary>
        public string? TraceId { get; set; }

        /// <summary>
        /// Gets or sets the optional distributed-tracing span identifier.
        /// </summary>
        public string? SpanId { get; set; }

        /// <summary>
        /// Gets or sets the optional serialized exception text.
        /// </summary>
        public string? Exception { get; set; }

        /// <summary>
        /// Gets or sets the optional serialized Serilog property payload.
        /// </summary>
        public string? Properties { get; set; }
    }
}
