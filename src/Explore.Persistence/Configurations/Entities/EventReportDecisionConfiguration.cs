using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventReportDecisionConfiguration : IEntityTypeConfiguration<EventReportDecision>
{
    public void Configure(EntityTypeBuilder<EventReportDecision> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.HasAlternateKey(e => new { e.TenantId, e.ReportId, e.Id })
            .HasName("ak_event_report_decisions_tenant_id_report_id_id");
        builder.HasAlternateKey(e => new { e.TenantId, e.ReportId, e.CaseId, e.Id })
            .HasName("ak_event_report_decisions_tenant_report_case_id");

        builder.Property(e => e.DecisionSource).HasConversion<int>().IsRequired();
        builder.Property(e => e.ProviderTargetScope).HasConversion<int>().IsRequired();
        builder.Property(e => e.ProviderTargetId).HasMaxLength(200).IsRequired();
        builder.Property(e => e.DecisionKind).HasConversion<int>().IsRequired();
        builder.Property(e => e.DuplicateGroupId);
        builder.Property(e => e.ReasonCode).HasMaxLength(100).IsRequired();
        builder.Property(e => e.SafeNote).HasMaxLength(1000);
        builder.Property(e => e.ExternalDecisionId).HasMaxLength(200);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Report)
            .WithMany(e => e.Decisions)
            .HasForeignKey(e => new { e.TenantId, e.ReportId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.ClientNoAction);

        builder.HasOne(e => e.Case)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.ReportId, e.CaseId })
            .HasPrincipalKey(e => new { e.TenantId, e.ReportId, e.Id })
            .OnDelete(DeleteBehavior.ClientNoAction);

        builder.HasOne(e => e.ModeratorUser)
            .WithMany()
            .HasForeignKey(e => e.ModeratorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.ReportId, e.CreatedAt })
            .HasDatabaseName("ix_event_report_decisions_tenant_report_created")
            .IsDescending(false, false, true);

        builder.HasIndex(e => new { e.TenantId, e.CaseId, e.CreatedAt })
            .HasDatabaseName("ix_event_report_decisions_tenant_case_created")
            .IsDescending(false, false, true);

        builder.HasIndex(e => new { e.TenantId, e.DecisionSource, e.ProviderTargetScope, e.ProviderTargetId, e.ExternalDecisionId })
            .IsUnique()
            .HasFilter("external_decision_id IS NOT NULL")
            .HasDatabaseName("ux_event_report_decisions_tenant_source_target_external");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_event_report_decisions_source", "decision_source BETWEEN 1 AND 4");
            t.HasCheckConstraint("ck_event_report_decisions_provider_target_scope", "provider_target_scope BETWEEN 1 AND 3");
            t.HasCheckConstraint("ck_event_report_decisions_provider_target_id_not_blank", "length(btrim(provider_target_id)) > 0");
            t.HasCheckConstraint("ck_event_report_decisions_kind", "decision_kind BETWEEN 1 AND 7");
            t.HasCheckConstraint("ck_event_report_decisions_duplicate_group_shape", "(decision_kind = 2 AND duplicate_group_id IS NOT NULL) OR (decision_kind <> 2 AND duplicate_group_id IS NULL)");
            t.HasCheckConstraint("ck_event_report_decisions_reason_code_not_blank", "length(btrim(reason_code)) > 0");
            t.HasCheckConstraint("ck_event_report_decisions_safe_note_not_blank", "safe_note IS NULL OR length(btrim(safe_note)) > 0");
            t.HasCheckConstraint("ck_event_report_decisions_external_decision_id_not_blank", "external_decision_id IS NULL OR length(btrim(external_decision_id)) > 0");
            t.HasCheckConstraint("ck_event_report_decisions_local_moderator_required", "decision_source <> 1 OR moderator_user_id IS NOT NULL");
        });
    }
}
