using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventReportEvidenceConfiguration : IEntityTypeConfiguration<EventReportEvidence>
{
    public void Configure(EntityTypeBuilder<EventReportEvidence> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(e => e.EvidenceKind).HasConversion<int>().IsRequired();
        builder.Property(e => e.TextBodyEncrypted).HasColumnType("text");
        builder.Property(e => e.ContentHash).HasMaxLength(128);
        builder.Property(e => e.Classification).HasConversion<int>().IsRequired();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Report)
            .WithMany(e => e.EvidenceItems)
            .HasForeignKey(e => new { e.TenantId, e.ReportId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.ClientNoAction);

        builder.HasOne(e => e.StorageObject)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.StorageObjectId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.CreatedByUser)
            .WithMany()
            .HasForeignKey(e => e.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.ReportId, e.EvidenceKind, e.CreatedAt })
            .HasDatabaseName("ix_event_report_evidence_tenant_report_kind_created")
            .IsDescending(false, false, false, true);

        builder.HasIndex(e => new { e.TenantId, e.RetentionUntil })
            .HasFilter("retention_until IS NOT NULL")
            .HasDatabaseName("ix_event_report_evidence_tenant_retention_until");

        builder.HasIndex(e => new { e.TenantId, e.ContentHash })
            .HasFilter("content_hash IS NOT NULL")
            .HasDatabaseName("ix_event_report_evidence_tenant_content_hash");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_event_report_evidence_kind", "evidence_kind BETWEEN 1 AND 5");
            t.HasCheckConstraint("ck_event_report_evidence_classification", "classification BETWEEN 1 AND 3");
            t.HasCheckConstraint("ck_event_report_evidence_reporter_text_required", "evidence_kind <> 1 OR (text_body_encrypted IS NOT NULL AND length(btrim(text_body_encrypted)) > 0)");
            t.HasCheckConstraint("ck_event_report_evidence_content_hash_not_blank", "content_hash IS NULL OR length(btrim(content_hash)) > 0");
        });
    }
}
