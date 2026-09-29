using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Eigenverft.WebLib.SerilogRelayReceiver
{
    /// <summary>
    /// Represents one physically received SerilogRelay event persisted through the built-in
    /// Entity Framework Core integration.
    /// </summary>
    [Table("SerilogRelayReceivedEvents")]
    public sealed class SerilogRelayReceivedEvent
    {
        /// <summary>
        /// Gets or sets the receiver-local generated identifier for this physical receive.
        /// </summary>
        public long ReceiveId { get; set; }

        /// <summary>
        /// Gets or sets the wire protocol version of the containing batch.
        /// </summary>
        public int ProtocolVersion { get; set; }

        /// <summary>
        /// Gets or sets the sender-generated canonical batch identifier.
        /// </summary>
        public string BatchId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the sender-generated batch timestamp.
        /// </summary>
        public string? BatchTimestamp { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the sender-declared number of events in the containing batch.
        /// </summary>
        public int BatchCount { get; set; }

        /// <summary>
        /// Gets or sets the sender-local durable row identifier.
        /// </summary>
        public long SenderLocalId { get; set; }

        /// <summary>
        /// Gets or sets the stable canonical event identifier supplied by the sender.
        /// Repeated values are allowed and represent independent physical receives.
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
        /// Gets or sets the sender-generated event timestamp.
        /// </summary>
        public string? Timestamp { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the serialized Serilog level name.
        /// </summary>
        public string? Level { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the rendered log message.
        /// </summary>
        public string? RenderMessage { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the original Serilog message template.
        /// </summary>
        public string? MessageTemplate { get; set; } = string.Empty;

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

        /// <summary>
        /// Gets or sets the UTC time at which this receiver accepted the physical event for persistence.
        /// </summary>
        public DateTimeOffset ReceivedAtUtc { get; set; }
    }
}
