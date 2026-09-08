using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventReportCaseConfiguration : IEntityTypeConfiguration<EventReportCase>
{
    public void Configure(EntityTypeBuilder<EventReportCase> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.HasAlternateKey(e => new { e.TenantId, e.Id })
            .HasName("ak_event_report_cases_tenant_id_id");
        builder.HasAlternateKey(e => new { e.TenantId, e.ReportId, e.Id })
            .HasName("ak_event_report_cases_tenant_id_report_id_id");

        builder.Property(e => e.QueueCode).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Status).HasConversion<int>().IsRequired();
        builder.Property(e => e.Priority).HasConversion<int>().IsRequired();
        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Report)
            .WithMany(e => e.Cases)
            .HasForeignKey(e => new { e.TenantId, e.ReportId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.ClientNoAction);

        builder.HasOne(e => e.AssignedModeratorUser)
            .WithMany()
            .HasForeignKey(e => e.AssignedModeratorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.CurrentDecision)
            .WithMany()
            .HasForeignKey(e => new
            {
                e.TenantId,
                e.ReportId,
                CaseId = e.Id,
                e.CurrentDecisionId
            })
            .HasPrincipalKey(e => new { e.TenantId, e.ReportId, e.CaseId, e.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasIndex(e => new { e.TenantId, e.QueueCode, e.Status, e.Priority, e.CreatedAt })
            .HasDatabaseName("ix_event_report_cases_tenant_queue_status_priority_created")
            .IsDescending(false, false, false, false, true);

        builder.HasIndex(e => new { e.TenantId, e.AssignedModeratorUserId, e.Status, e.UpdatedAt })
            .HasFilter("assigned_moderator_user_id IS NOT NULL")
            .HasDatabaseName("ix_event_report_cases_tenant_assignee_status_updated")
            .IsDescending(false, false, false, true);

        builder.HasIndex(e => new { e.TenantId, e.SlaDueAt })
            .HasFilter("sla_due_at IS NOT NULL")
            .HasDatabaseName("ix_event_report_cases_tenant_sla_due_at");

        builder.HasIndex(e => new { e.TenantId, e.ReportId, e.Id, e.CurrentDecisionId })
            .HasFilter("current_decision_id IS NOT NULL")
            .HasDatabaseName("ix_event_report_cases_current_decision");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_event_report_cases_queue_code_not_blank", "length(btrim(queue_code)) > 0");
            t.HasCheckConstraint("ck_event_report_cases_status", "status BETWEEN 1 AND 6");
            t.HasCheckConstraint("ck_event_report_cases_priority", "priority BETWEEN 1 AND 4");
        });
    }
}
