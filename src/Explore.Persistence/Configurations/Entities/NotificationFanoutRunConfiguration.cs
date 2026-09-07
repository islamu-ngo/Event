using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class NotificationFanoutRunConfiguration : IEntityTypeConfiguration<NotificationFanoutRun>
{
    public void Configure(EntityTypeBuilder<NotificationFanoutRun> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_notification_fanout_runs_processed_count_nonnegative", "processed_count >= 0");
            t.HasCheckConstraint("ck_notification_fanout_runs_created_count_nonnegative", "created_notification_count >= 0");
            t.HasCheckConstraint("ck_notification_fanout_runs_status", "status IN ('pending', 'processing', 'completed', 'failed')");
            t.HasCheckConstraint("ck_notification_fanout_runs_generation_nonnegative", "processing_generation >= 0 AND processing_fence >= 0");
            t.HasCheckConstraint(
                "ck_notification_fanout_runs_cursor_pair",
                "(cursor_first_eligible_registration_created_at IS NULL) = (cursor_user_id IS NULL)");
            t.HasCheckConstraint(
                "ck_notification_fanout_runs_occurrence_lease",
                "fanout_occurrence_id IS NULL OR " +
                "(status = 'processing' AND processing_lease_owner IS NOT NULL AND btrim(processing_lease_owner) <> '' AND processing_lease_token IS NOT NULL AND processing_lease_expires_at IS NOT NULL) OR " +
                "(status <> 'processing' AND processing_lease_owner IS NULL AND processing_lease_token IS NULL AND processing_lease_expires_at IS NULL)");
        });

        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.FanoutKind).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(50).HasDefaultValue("pending");
        builder.Property(e => e.LastError).HasMaxLength(2000);
        builder.Property(e => e.ProcessingLeaseOwner).HasMaxLength(NotificationFanoutRun.MaxLeaseOwnerLength);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("NOW()").IsRequired();
        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.NotificationEntityType)
            .WithMany()
            .HasForeignKey(e => e.NotificationEntityTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.SourceActor)
            .WithMany()
            .HasForeignKey(e => e.SourceActorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.FanoutOccurrence)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.FanoutOccurrenceId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .HasConstraintName("fk_fanout_runs_occurrence_tenant")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.FanoutKind, e.NotificationEntityTypeId, e.EntityId, e.SourceActorId })
            .IsUnique()
            .HasFilter("fanout_occurrence_id IS NULL");

        builder.HasIndex(e => new { e.TenantId, e.FanoutOccurrenceId })
            .IsUnique();

        builder.HasIndex(e => new { e.Status, e.ProcessingLeaseExpiresAt, e.CreatedAt });
    }
}
