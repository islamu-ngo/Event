using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventReportExternalLinkConfiguration : IEntityTypeConfiguration<EventReportExternalLink>
{
    public void Configure(EntityTypeBuilder<EventReportExternalLink> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(e => e.Provider).HasConversion<int>().IsRequired();
        builder.Property(e => e.ProviderTargetScope).HasConversion<int>().IsRequired();
        builder.Property(e => e.ProviderTargetId).HasMaxLength(200).IsRequired();
        builder.Property(e => e.ProviderCaseId).HasMaxLength(200);
        builder.Property(e => e.ProviderSignalId).HasMaxLength(200);
        builder.Property(e => e.ProviderUrl).HasMaxLength(500);
        builder.Property(e => e.SyncState).HasConversion<int>().IsRequired();
        builder.Property(e => e.LastErrorCategory).HasMaxLength(100);
        builder.Property(e => e.CorrelationId).HasMaxLength(100).IsRequired();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Report)
            .WithMany(e => e.ExternalLinks)
            .HasForeignKey(e => new { e.TenantId, e.ReportId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.ClientNoAction);

        builder.HasOne(e => e.Case)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.ReportId, e.CaseId })
            .HasPrincipalKey(e => new { e.TenantId, e.ReportId, e.Id })
            .OnDelete(DeleteBehavior.ClientNoAction);

        builder.HasIndex(e => new { e.TenantId, e.Provider, e.ProviderTargetScope, e.ProviderTargetId, e.SyncState, e.CreatedAt })
            .HasDatabaseName("ix_event_report_external_links_tenant_provider_target_state_created")
            .IsDescending(false, false, false, false, false, true);

        builder.HasIndex(e => new { e.TenantId, e.Provider, e.ProviderTargetScope, e.ProviderTargetId, e.CorrelationId })
            .IsUnique()
            .HasDatabaseName("ux_event_report_external_links_tenant_provider_target_correlation");

        builder.HasIndex(e => new { e.TenantId, e.Provider, e.ProviderTargetScope, e.ProviderTargetId, e.ProviderCaseId })
            .IsUnique()
            .HasFilter("provider_case_id IS NOT NULL")
            .HasDatabaseName("ux_event_report_external_links_tenant_provider_target_case");

        builder.HasIndex(e => new { e.TenantId, e.Provider, e.ProviderTargetScope, e.ProviderTargetId, e.ProviderSignalId })
            .IsUnique()
            .HasFilter("provider_signal_id IS NOT NULL")
            .HasDatabaseName("ux_event_report_external_links_tenant_provider_target_signal");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_event_report_external_links_provider", "provider BETWEEN 1 AND 2");
            t.HasCheckConstraint("ck_event_report_external_links_provider_target_scope", "provider_target_scope BETWEEN 1 AND 3");
            t.HasCheckConstraint("ck_event_report_external_links_provider_target_id_not_blank", "length(btrim(provider_target_id)) > 0");
            t.HasCheckConstraint("ck_event_report_external_links_sync_state", "sync_state BETWEEN 1 AND 5");
            t.HasCheckConstraint("ck_event_report_external_links_retry_count_nonnegative", "retry_count >= 0");
            t.HasCheckConstraint("ck_event_report_external_links_provider_case_id_not_blank", "provider_case_id IS NULL OR length(btrim(provider_case_id)) > 0");
            t.HasCheckConstraint("ck_event_report_external_links_provider_signal_id_not_blank", "provider_signal_id IS NULL OR length(btrim(provider_signal_id)) > 0");
            t.HasCheckConstraint("ck_event_report_external_links_provider_url_not_blank", "provider_url IS NULL OR length(btrim(provider_url)) > 0");
            t.HasCheckConstraint("ck_event_report_external_links_last_error_category_not_blank", "last_error_category IS NULL OR length(btrim(last_error_category)) > 0");
            t.HasCheckConstraint("ck_event_report_external_links_correlation_id_not_blank", "length(btrim(correlation_id)) > 0");
        });
    }
}
