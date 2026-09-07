using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventReportConfiguration : IEntityTypeConfiguration<EventReport>
{
    public void Configure(EntityTypeBuilder<EventReport> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.HasAlternateKey(e => new { e.TenantId, e.Id })
            .HasName("ak_event_reports_tenant_id_id");
        builder.HasAlternateKey(e => new { e.TenantId, e.EventId, e.Id })
            .HasName("ak_event_reports_tenant_id_event_id_id");

        builder.Property(e => e.ReporterKind).HasConversion<int>().IsRequired();
        builder.Property(e => e.SourceKind).HasConversion<int>().IsRequired();
        builder.Property(e => e.ReasonCode).HasMaxLength(100).IsRequired();
        builder.Property(e => e.SubcategoryCode).HasMaxLength(100);
        builder.Property(e => e.Status).HasConversion<int>().IsRequired();
        builder.Property(e => e.Priority).HasConversion<int>().IsRequired();
        builder.Property(e => e.ReportCaseUpdatesConsent).IsRequired();
        builder.Property(e => e.ReportFollowUpContactConsent).IsRequired();
        builder.Property(e => e.ReporterLocale).HasMaxLength(10);
        builder.Property(e => e.ReporterIpHash).HasMaxLength(64);
        builder.Property(e => e.ReporterUserAgentHash).HasMaxLength(64);
        builder.Property(e => e.IsDeleted).HasDefaultValue(false);
        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Event)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.EventId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ReporterUser)
            .WithMany()
            .HasForeignKey(e => e.ReporterUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ReporterActor)
            .WithMany()
            .HasForeignKey(e => e.ReporterActorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.EventId, e.Status, e.CreatedAt })
            .HasDatabaseName("ix_event_reports_tenant_event_status_created")
            .IsDescending(false, false, false, true);

        builder.HasIndex(e => new { e.TenantId, e.ReporterUserId, e.EventId, e.ReasonCode, e.CreatedAt })
            .HasFilter("reporter_user_id IS NOT NULL")
            .HasDatabaseName("ix_event_reports_tenant_reporter_event_reason_created")
            .IsDescending(false, false, false, false, true);

        builder.HasIndex(e => new { e.TenantId, e.DuplicateGroupId })
            .HasFilter("duplicate_group_id IS NOT NULL")
            .HasDatabaseName("ix_event_reports_tenant_duplicate_group");

        builder.HasIndex(e => new { e.TenantId, e.Priority, e.Status, e.CreatedAt })
            .HasDatabaseName("ix_event_reports_tenant_priority_status_created")
            .IsDescending(false, false, false, true);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_event_reports_reason_code_not_blank", "length(btrim(reason_code)) > 0");
            t.HasCheckConstraint("ck_event_reports_subcategory_code_not_blank", "subcategory_code IS NULL OR length(btrim(subcategory_code)) > 0");
            t.HasCheckConstraint("ck_event_reports_reporter_kind", "reporter_kind BETWEEN 1 AND 4");
            t.HasCheckConstraint("ck_event_reports_source_kind", "source_kind BETWEEN 1 AND 5");
            t.HasCheckConstraint("ck_event_reports_status", "status BETWEEN 1 AND 8");
            t.HasCheckConstraint("ck_event_reports_priority", "priority BETWEEN 1 AND 4");
            t.HasCheckConstraint("ck_event_reports_severity_hint", "severity_hint IS NULL OR severity_hint BETWEEN 1 AND 4");
            t.HasCheckConstraint("ck_event_reports_reporter_locale_not_blank", "reporter_locale IS NULL OR length(btrim(reporter_locale)) > 0");
            t.HasCheckConstraint("ck_event_reports_reporter_ip_hash_not_blank", "reporter_ip_hash IS NULL OR length(btrim(reporter_ip_hash)) > 0");
            t.HasCheckConstraint("ck_event_reports_reporter_user_agent_hash_not_blank", "reporter_user_agent_hash IS NULL OR length(btrim(reporter_user_agent_hash)) > 0");
            t.HasCheckConstraint("ck_event_reports_closed_at_terminal_status", "(closed_at IS NULL AND status NOT IN (4, 5, 6, 8)) OR (closed_at IS NOT NULL AND status IN (4, 5, 6, 8))");
        });
    }
}
