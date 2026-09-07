using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventReportTargetConfiguration : IEntityTypeConfiguration<EventReportTarget>
{
    public void Configure(EntityTypeBuilder<EventReportTarget> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(e => e.TargetKind).HasConversion<int>().IsRequired();
        builder.Property(e => e.FieldPath).HasMaxLength(200);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Report)
            .WithMany(e => e.Targets)
            .HasForeignKey(e => new { e.TenantId, e.ReportId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.ClientNoAction);

        builder.HasOne(e => e.StorageObject)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.StorageObjectId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.ReportId, e.TargetKind, e.TargetId })
            .HasDatabaseName("ix_event_report_targets_tenant_report_target");

        builder.HasIndex(e => new { e.TenantId, e.TargetKind, e.TargetId })
            .HasDatabaseName("ix_event_report_targets_tenant_target");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_event_report_targets_target_kind", "target_kind BETWEEN 1 AND 6");
            t.HasCheckConstraint("ck_event_report_targets_field_path_not_blank", "field_path IS NULL OR length(btrim(field_path)) > 0");
        });
    }
}
