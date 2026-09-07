using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventReportDecisionExecutionConfiguration
    : IEntityTypeConfiguration<EventReportDecisionExecution>
{
    public void Configure(EntityTypeBuilder<EventReportDecisionExecution> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_event_report_decision_executions_state",
                "state BETWEEN 1 AND 4");
            table.HasCheckConstraint(
                "ck_event_report_decision_executions_receipt_kind",
                "enforcement_receipt_kind BETWEEN 0 AND 5");
            table.HasCheckConstraint(
                "ck_event_report_decision_executions_lease_pair",
                "(processing_lease_token IS NULL) = (processing_lease_expires_at_utc IS NULL)");
            table.HasCheckConstraint(
                "ck_event_report_decision_executions_state_shape",
                "(state = 1 AND enforcement_receipt_kind = 0 AND enforcement_completed_at_utc IS NULL AND completed_at_utc IS NULL AND processing_lease_token IS NULL) " +
                "OR (state = 2 AND enforcement_receipt_kind = 0 AND enforcement_completed_at_utc IS NULL AND completed_at_utc IS NULL AND processing_lease_token IS NOT NULL) " +
                "OR (state = 3 AND enforcement_receipt_kind <> 0 AND enforcement_completed_at_utc IS NOT NULL AND completed_at_utc IS NULL) " +
                "OR (state = 4 AND enforcement_receipt_kind <> 0 AND enforcement_completed_at_utc IS NOT NULL AND completed_at_utc IS NOT NULL AND processing_lease_token IS NULL)");
            table.HasCheckConstraint(
                "ck_event_report_decision_executions_receipt_id_shape",
                "(enforcement_receipt_kind IN (2, 3) AND enforcement_receipt_id IS NOT NULL) " +
                "OR (enforcement_receipt_kind NOT IN (2, 3) AND enforcement_receipt_id IS NULL)");
            table.HasCheckConstraint(
                "ck_event_report_decision_executions_moderation_record_shape",
                "(enforcement_receipt_kind IN (2, 3) AND moderation_record_id IS NOT NULL AND moderation_record_id = enforcement_receipt_id) " +
                "OR (enforcement_receipt_kind NOT IN (2, 3) AND moderation_record_id IS NULL)");
            table.HasCheckConstraint(
                "ck_event_report_decision_executions_failure_code_not_blank",
                "last_failure_code IS NULL OR length(btrim(last_failure_code)) > 0");
        });

        builder.Property(execution => execution.Id)
            .HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(execution => execution.State).HasConversion<int>().IsRequired();
        builder.Property(execution => execution.EnforcementReceiptKind).HasConversion<int>().IsRequired();
        builder.Property(execution => execution.LastFailureCode).HasMaxLength(EventReportDecisionExecution.MaxFailureCodeLength);
        builder.Property(execution => execution.Version).IsConcurrencyToken();

        builder.HasAlternateKey(execution => new { execution.TenantId, execution.Id })
            .HasName("ak_event_report_decision_executions_tenant_id_id");

        builder.HasOne(execution => execution.Tenant)
            .WithMany()
            .HasForeignKey(execution => execution.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(execution => execution.Report)
            .WithMany()
            .HasForeignKey(execution => new { execution.TenantId, execution.ReportId })
            .HasPrincipalKey(report => new { report.TenantId, report.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(execution => execution.Decision)
            .WithOne(decision => decision.Execution)
            .HasForeignKey<EventReportDecisionExecution>(execution => new
            {
                execution.TenantId,
                execution.ReportId,
                execution.DecisionId
            })
            .HasPrincipalKey<EventReportDecision>(decision => new
            {
                decision.TenantId,
                decision.ReportId,
                decision.Id
            })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(execution => execution.ModerationRecord)
            .WithMany()
            .HasForeignKey(execution => new { execution.TenantId, execution.ModerationRecordId })
            .HasPrincipalKey(record => new { record.TenantId, record.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasIndex(execution => new { execution.TenantId, execution.DecisionId })
            .HasDatabaseName("ux_event_report_decision_executions_tenant_decision")
            .IsUnique();

        builder.HasIndex(execution => new
        {
            execution.TenantId,
            execution.ReportId,
            execution.DecisionId
        })
            .HasDatabaseName("ux_event_report_decision_executions_tenant_report_decision")
            .IsUnique();

        builder.HasIndex(execution => new
        {
            execution.State,
            execution.ProcessingLeaseExpiresAtUtc,
            execution.CreatedAt
        })
            .HasDatabaseName("ix_event_report_decision_executions_runnable");
    }
}
