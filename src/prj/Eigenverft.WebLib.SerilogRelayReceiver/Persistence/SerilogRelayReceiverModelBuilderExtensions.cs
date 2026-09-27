using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Eigenverft.WebLib.SerilogRelayReceiver
{
    /// <summary>
    /// Configures the Entity Framework Core model used by the built-in SerilogRelay persistence handler.
    /// </summary>
    public static class SerilogRelayReceiverModelBuilderExtensions
    {
        /// <summary>
        /// Adds the SerilogRelay received-event entity and its provider-neutral EF Core model.
        /// Call this from the host DbContext's <c>OnModelCreating</c>.
        /// </summary>
        public static ModelBuilder ConfigureSerilogRelayReceiver(
            this ModelBuilder modelBuilder)
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);

            EntityTypeBuilder<SerilogRelayReceivedEvent> entity =
                modelBuilder.Entity<SerilogRelayReceivedEvent>();

            entity.HasKey(row => row.ReceiveId);
            entity.Property(row => row.ReceiveId).ValueGeneratedOnAdd();

            entity.Property(row => row.BatchId)
                .HasMaxLength(36)
                .IsRequired();
            entity.Property(row => row.BatchTimestamp)
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(row => row.EventId)
                .HasMaxLength(36)
                .IsRequired();
            entity.Property(row => row.ApplicationId)
                .HasMaxLength(256)
                .IsRequired();
            entity.Property(row => row.MachineId)
                .HasMaxLength(256);
            entity.Property(row => row.Timestamp)
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(row => row.Level)
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(row => row.RenderMessage)
                .IsRequired();
            entity.Property(row => row.MessageTemplate)
                .IsRequired();
            entity.Property(row => row.TraceId)
                .HasMaxLength(64);
            entity.Property(row => row.SpanId)
                .HasMaxLength(32);

            entity.HasIndex(row => row.BatchId);
            entity.HasIndex(row => row.EventId);
            entity.HasIndex(row => row.ReceivedAtUtc);
            entity.HasIndex(
                row => new
                {
                    row.ApplicationId,
                    row.ReceivedAtUtc,
                });

            return modelBuilder;
        }
    }
}
