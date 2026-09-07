using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class NotificationFanoutOccurrenceConfiguration : IEntityTypeConfiguration<NotificationFanoutOccurrence>
{
    public void Configure(EntityTypeBuilder<NotificationFanoutOccurrence> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_notification_fanout_occurrences_versions", "template_version > 0 AND policy_version > 0");
            table.HasCheckConstraint("ck_notification_fanout_occurrences_state", "state IN (1, 2)");
            table.HasCheckConstraint(
                "ck_notification_fanout_occurrences_supersession",
                "(state = 1 AND superseded_by_occurrence_id IS NULL AND suppression_reason IS NULL AND superseded_at IS NULL) OR " +
                "(state = 2 AND superseded_by_occurrence_id IS NOT NULL AND suppression_reason IS NOT NULL AND superseded_at IS NOT NULL)");
        });

        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.ChangeSetJson).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.SafeBeforeSnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.SafeAfterSnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.TemplateKey).HasMaxLength(160).IsRequired();
        builder.Property(e => e.SourceType).HasMaxLength(100).IsRequired();
        builder.Property(e => e.CoalescingKey).HasMaxLength(300).IsRequired();
        builder.Property(e => e.SuppressionReason).HasMaxLength(100);
        builder.Property(e => e.State).IsRequired();

        builder.HasAlternateKey(e => new { e.TenantId, e.Id });

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Event)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.EventId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Session)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.SessionId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .HasConstraintName("fk_fanout_occurrences_session_tenant")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.DeliveryPolicy)
            .WithMany()
            .HasForeignKey(e => e.DeliveryPolicyId)
            .HasConstraintName("fk_fanout_occurrences_delivery_policy")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.SupersededByOccurrence)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.SupersededByOccurrenceId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .HasConstraintName("fk_fanout_occurrences_superseded_tenant")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.State, e.NotBefore, e.OccurredAt });
        builder.HasIndex(e => new { e.NotBefore, e.TenantId, e.Priority, e.OccurredAt, e.Id })
            .IsDescending(false, false, true, false, false)
            .HasFilter("state = 1");
        builder.HasIndex(e => new { e.TenantId, e.SourceType, e.SourceId, e.AggregateVersion });
        builder.HasIndex(e => new { e.TenantId, e.CoalescingKey, e.State, e.OccurredAt });
    }
}
