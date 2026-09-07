using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class WebhookAuditEventConfiguration : IEntityTypeConfiguration<WebhookAuditEvent>
{
    public void Configure(EntityTypeBuilder<WebhookAuditEvent> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_webhook_audit_events_tenant_scope",
                "effective_scope_kind_id <> 1 OR effective_scope_id = tenant_id");
            table.HasCheckConstraint(
                "ck_webhook_audit_events_effective_scope",
                "(effective_scope_kind_id = 2 AND tenant_id IS NULL AND effective_scope_id IS NOT NULL) OR " +
                "(effective_scope_kind_id IN (1, 3, 4, 5) AND tenant_id IS NOT NULL AND effective_scope_id IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_webhook_audit_events_safe_before_object",
                "safe_before_json IS NULL OR jsonb_typeof(safe_before_json) = 'object'");
            table.HasCheckConstraint(
                "ck_webhook_audit_events_safe_after_object",
                "safe_after_json IS NULL OR jsonb_typeof(safe_after_json) = 'object'");
        });

        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.PrincipalReference)
            .HasMaxLength(WebhookAuditEvent.MaxPrincipalReferenceLength)
            .IsRequired();
        builder.Property(e => e.ConfigurationVersion)
            .HasMaxLength(WebhookAuditEvent.MaxConfigurationVersionLength);
        builder.Property(e => e.CorrelationId)
            .HasMaxLength(WebhookAuditEvent.MaxCorrelationIdLength);
        builder.Property(e => e.ReasonCode)
            .HasMaxLength(WebhookAuditEvent.MaxReasonCodeLength)
            .IsRequired();
        builder.Property(e => e.RetentionPolicyVersion)
            .HasMaxLength(WebhookAuditEvent.MaxConfigurationVersionLength)
            .HasDefaultValue("legacy-retention-v1")
            .IsRequired();
        builder.Property(e => e.RetentionUntil)
            .HasDefaultValueSql("statement_timestamp() + INTERVAL '365 days'");
        builder.Property(e => e.SafeBeforeJson).HasColumnType("jsonb");
        builder.Property(e => e.SafeAfterJson).HasColumnType("jsonb");
        builder.Property(e => e.OccurredAt)
            .HasDefaultValueSql("statement_timestamp()")
            .ValueGeneratedOnAdd();

        builder.Ignore(e => e.PrincipalKind);
        builder.Ignore(e => e.EffectiveScopeKind);
        builder.Ignore(e => e.Action);
        builder.Ignore(e => e.TargetKind);
        builder.Ignore(e => e.Outcome);

        builder.HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.PrincipalKindLookup).WithMany().HasForeignKey(e => e.PrincipalKindId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.EffectiveScopeKindLookup).WithMany().HasForeignKey(e => e.EffectiveScopeKindId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.ActionLookup).WithMany().HasForeignKey(e => e.ActionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.TargetKindLookup).WithMany().HasForeignKey(e => e.TargetKindId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.OutcomeLookup).WithMany().HasForeignKey(e => e.OutcomeId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.OccurredAt })
            .IsDescending(false, true);
        builder.HasIndex(e => new { e.TenantId, e.TargetKindId, e.TargetId, e.OccurredAt })
            .IsDescending(false, false, false, true);
        builder.HasIndex(e => new { e.TenantId, e.CorrelationId });
        builder.HasIndex(e => new { e.TenantId, e.RetentionUntil });
        builder.HasIndex(e => new { e.EffectiveScopeKindId, e.EffectiveScopeId, e.OccurredAt })
            .IsDescending(false, false, true);
    }
}
