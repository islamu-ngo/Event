using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class IntegrationSyncOutboxConfiguration : IEntityTypeConfiguration<IntegrationSyncOutbox>
{
    public void Configure(EntityTypeBuilder<IntegrationSyncOutbox> builder)
    {
        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.Kind).IsRequired();
        builder.Property(e => e.SourceType).HasMaxLength(100).IsRequired();
        builder.Property(e => e.SubscriberEmail).HasMaxLength(320).IsRequired();
        builder.Property(e => e.SubscriberName).HasMaxLength(320);
        builder.Property(e => e.SubscriberPayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.ListmonkListId).IsRequired();
        builder.Property(e => e.Status).IsRequired();
        builder.Property(e => e.MaxAttempts).HasDefaultValue(5);
        builder.Property(e => e.LastError).HasMaxLength(2000);
        builder.Property(e => e.CorrelationId).HasMaxLength(200);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.RegistrationOrder)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.RegistrationOrderId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Event)
            .WithMany()
            .HasForeignKey(e => e.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.Status, e.NextAttemptAt, e.CreatedAt })
            .HasDatabaseName("ix_integration_sync_outbox_worker_poll");

        builder.HasIndex(e => new { e.TenantId, e.SourceType, e.SourceId, e.Kind })
            .HasDatabaseName("ux_integration_sync_outbox_tenant_source_kind")
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(e => new { e.TenantId, e.Status, e.LastFailureAt })
            .HasDatabaseName("ix_integration_sync_outbox_tenant_status");
    }
}
