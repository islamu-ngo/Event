using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventReportSignalConfiguration : IEntityTypeConfiguration<EventReportSignal>
{
    public void Configure(EntityTypeBuilder<EventReportSignal> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(e => e.Provider).HasConversion<int>().IsRequired();
        builder.Property(e => e.ProviderTargetScope).HasConversion<int>().IsRequired();
        builder.Property(e => e.ProviderTargetId).HasMaxLength(200).IsRequired();
        builder.Property(e => e.SignalType).HasMaxLength(100).IsRequired();
        builder.Property(e => e.PolicyCode).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Score).HasPrecision(5, 4);
        builder.Property(e => e.Verdict).HasConversion<int>().IsRequired();
        builder.Property(e => e.SafeSummary).HasMaxLength(500);
        builder.Property(e => e.ExternalSignalId).HasMaxLength(200);
        builder.Property(e => e.CorrelationId).HasMaxLength(100).IsRequired();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Event)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.EventId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Report)
            .WithMany(e => e.Signals)
            .HasForeignKey(e => new { e.TenantId, e.EventId, e.ReportId })
            .HasPrincipalKey(e => new { e.TenantId, e.EventId, e.Id })
            .OnDelete(DeleteBehavior.ClientNoAction);

        builder.HasIndex(e => new { e.TenantId, e.EventId, e.Provider, e.ProviderTargetScope, e.ProviderTargetId, e.CreatedAt })
            .HasDatabaseName("ix_event_report_signals_tenant_event_provider_target_created")
            .IsDescending(false, false, false, false, false, true);

        builder.HasIndex(e => new { e.TenantId, e.ReportId, e.Provider, e.ProviderTargetScope, e.ProviderTargetId, e.CreatedAt })
            .HasFilter("report_id IS NOT NULL")
            .HasDatabaseName("ix_event_report_signals_tenant_report_provider_target_created")
            .IsDescending(false, false, false, false, false, true);

        builder.HasIndex(e => new { e.TenantId, e.Provider, e.ProviderTargetScope, e.ProviderTargetId, e.ExternalSignalId })
            .IsUnique()
            .HasFilter("external_signal_id IS NOT NULL")
            .HasDatabaseName("ux_event_report_signals_tenant_provider_target_external_signal");

        builder.HasIndex(e => new { e.TenantId, e.Provider, e.ProviderTargetScope, e.ProviderTargetId, e.CorrelationId })
            .IsUnique()
            .HasDatabaseName("ux_event_report_signals_tenant_provider_target_correlation");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_event_report_signals_provider", "provider BETWEEN 1 AND 5");
            t.HasCheckConstraint("ck_event_report_signals_provider_target_scope", "provider_target_scope BETWEEN 1 AND 3");
            t.HasCheckConstraint("ck_event_report_signals_provider_target_id_not_blank", "length(btrim(provider_target_id)) > 0");
            t.HasCheckConstraint("ck_event_report_signals_signal_type_not_blank", "length(btrim(signal_type)) > 0");
            t.HasCheckConstraint("ck_event_report_signals_policy_code_not_blank", "length(btrim(policy_code)) > 0");
            t.HasCheckConstraint("ck_event_report_signals_score_range", "score IS NULL OR (score >= 0 AND score <= 1)");
            t.HasCheckConstraint("ck_event_report_signals_verdict", "verdict BETWEEN 1 AND 5");
            t.HasCheckConstraint("ck_event_report_signals_recommended_action", "recommended_action IS NULL OR recommended_action BETWEEN 0 AND 4");
            t.HasCheckConstraint("ck_event_report_signals_safe_summary_not_blank", "safe_summary IS NULL OR length(btrim(safe_summary)) > 0");
            t.HasCheckConstraint("ck_event_report_signals_external_signal_id_not_blank", "external_signal_id IS NULL OR length(btrim(external_signal_id)) > 0");
            t.HasCheckConstraint("ck_event_report_signals_correlation_id_not_blank", "length(btrim(correlation_id)) > 0");
        });
    }
}
