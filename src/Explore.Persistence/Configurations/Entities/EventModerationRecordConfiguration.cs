using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class EventModerationRecordConfiguration : IEntityTypeConfiguration<EventModerationRecord>
{
    public void Configure(EntityTypeBuilder<EventModerationRecord> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.HasAlternateKey(e => new { e.TenantId, e.Id });

        builder.Property(e => e.ActionKind)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.ModeratorUserId)
            .IsRequired(false);

        builder.Property(e => e.ReasonCode)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.CorrelationId)
            .HasMaxLength(100);

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Event)
            .WithMany(e => e.ModerationRecords)
            .HasForeignKey(e => new { e.TenantId, e.EventId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.SourceModerationRecord)
            .WithMany()
            .HasForeignKey(e => e.SourceModerationRecordId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.SourceReport)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.SourceReportId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne(e => e.SourceReportDecision)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.SourceReportId, e.SourceReportDecisionId })
            .HasPrincipalKey(e => new { e.TenantId, e.ReportId, e.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasIndex(e => new { e.TenantId, e.EventId, e.CreatedAt })
            .IsDescending(false, false, true);

        builder.HasIndex(e => new { e.TenantId, e.ActionKind, e.CreatedAt })
            .IsDescending(false, false, true);

        builder.HasIndex(e => new { e.TenantId, e.CorrelationId })
            .IsUnique()
            .HasFilter("correlation_id IS NOT NULL");

        builder.HasIndex(e => new { e.TenantId, e.SourceReportId })
            .HasFilter("source_report_id IS NOT NULL");

        builder.HasIndex(e => new { e.TenantId, e.SourceReportDecisionId })
            .HasFilter("source_report_decision_id IS NOT NULL");

        builder.HasIndex(e => new { e.TenantId, e.SourceReportId, e.SourceReportDecisionId })
            .IsUnique()
            .HasFilter("source_report_id IS NOT NULL AND source_report_decision_id IS NOT NULL");

        builder.HasIndex(e => new { e.TenantId, e.SourceReportId, e.SourceReportDecisionId, e.Id })
            .IsUnique();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "ck_event_moderation_records_reason_code_not_blank",
                "length(btrim(reason_code)) > 0");
            t.HasCheckConstraint(
                "ck_event_moderation_records_status_transition",
                "previous_status_id <> resulting_status_id");
            t.HasCheckConstraint(
                "ck_event_moderation_records_correlation_not_blank",
                "correlation_id IS NULL OR length(btrim(correlation_id)) > 0");
            t.HasCheckConstraint(
                "ck_event_moderation_records_source_decision_requires_report",
                "source_report_decision_id IS NULL OR source_report_id IS NOT NULL");
        });
    }
}
