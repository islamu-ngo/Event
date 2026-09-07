using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class WebhookMessageConfiguration : IEntityTypeConfiguration<WebhookMessage>
{
    public void Configure(EntityTypeBuilder<WebhookMessage> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_webhook_messages_payload_hash",
                "payload_hash ~ '^sha256:[0-9a-f]{64}$'");
            table.HasCheckConstraint(
                "ck_webhook_messages_payload_provenance",
                "payload_provenance_id > 0");
            table.HasCheckConstraint(
                "ck_webhook_messages_payload_byte_length",
                "payload_byte_length > 0");
        });

        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.EventType).HasMaxLength(WebhookMessage.MaxEventTypeLength).IsRequired();
        builder.Property(e => e.EventId).HasMaxLength(WebhookMessage.MaxEventIdLength).IsRequired();
        builder.Property(e => e.AggregateKind).HasMaxLength(WebhookMessage.MaxAggregateKindLength).IsRequired();
        builder.Property<byte[]?>("_payloadBytes")
            .HasColumnName("payload_bytes")
            .HasColumnType("bytea");
        builder.Property(e => e.PayloadHash).HasMaxLength(WebhookMessage.PayloadHashLength).IsRequired();
        builder.Property(e => e.PayloadByteLength).IsRequired();
        builder.Property(e => e.PayloadProvenanceId).IsRequired();
        builder.Property(e => e.ContentType).HasMaxLength(WebhookMessage.MaxContentTypeLength).IsRequired();
        builder.Property(e => e.ContentEncoding).HasMaxLength(WebhookMessage.MaxContentEncodingLength).IsRequired();

        builder.HasAlternateKey(e => new { e.TenantId, e.Id });

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Consumer)
            .WithMany()
            .HasForeignKey(e => e.ConsumerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.PayloadProvenanceLookup)
            .WithMany()
            .HasForeignKey(e => e.PayloadProvenanceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.CreatedAt, e.Id });

        builder.HasIndex(e => new { e.TenantId, e.EventType, e.EventId })
            .IsUnique();

        builder.HasIndex(e => new { e.TenantId, e.AggregateKind, e.AggregateId });

        builder.HasIndex(e => new { e.TenantId, e.PayloadRetentionUntil })
            .HasFilter("payload_bytes IS NOT NULL");
    }
}
