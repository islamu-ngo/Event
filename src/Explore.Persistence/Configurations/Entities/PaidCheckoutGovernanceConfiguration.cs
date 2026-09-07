using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class PaidCheckoutSaleControlConfiguration : IEntityTypeConfiguration<PaidCheckoutSaleControl>
{
    public void Configure(EntityTypeBuilder<PaidCheckoutSaleControl> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_paid_checkout_sale_controls_version", "version > 0"));
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.ScopeKey).IsRequired().HasMaxLength(48);
        builder.Property(value => value.Version).IsConcurrencyToken();
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.HasIndex(value => new { value.TenantId, value.ScopeKey }).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Event>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.EventId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(value => value.AuditTrail)
            .WithOne()
            .HasForeignKey(value => new { value.TenantId, value.PaidCheckoutSaleControlId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(value => value.AuditTrail).HasField("_auditTrail").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

public sealed class PaidCheckoutSaleControlAuditConfiguration : IEntityTypeConfiguration<PaidCheckoutSaleControlAudit>
{
    public void Configure(EntityTypeBuilder<PaidCheckoutSaleControlAudit> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_paid_checkout_sale_control_audits_sequence", "sequence > 0"));
        builder.HasKey(value => new { value.TenantId, value.PaidCheckoutSaleControlId, value.Sequence });
        builder.Property(value => value.ActionCode).IsRequired().HasMaxLength(32);
        builder.Property(value => value.ReasonCode).IsRequired().HasMaxLength(80);
        builder.HasIndex(value => new { value.TenantId, value.EventId, value.OccurredAt });
    }
}

public sealed class PaidCheckoutReviewApprovalConfiguration : IEntityTypeConfiguration<PaidCheckoutReviewApproval>
{
    public void Configure(EntityTypeBuilder<PaidCheckoutReviewApproval> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_paid_checkout_review_approvals_trigger", "trigger_id IN (1, 2)");
            table.HasCheckConstraint("ck_paid_checkout_review_approvals_status", "status_code IN ('pending', 'approved', 'rejected')");
            table.HasCheckConstraint("ck_paid_checkout_review_approvals_amount", "(trigger_id = 1 AND maximum_order_amount_minor IS NULL) OR (trigger_id = 2 AND maximum_order_amount_minor > 0)");
            table.HasCheckConstraint("ck_paid_checkout_review_approvals_separation", "reviewed_by_user_id IS NULL OR reviewed_by_user_id <> requested_by_user_id");
        });
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(value => value.StatusCode).IsRequired().HasMaxLength(16);
        builder.Property(value => value.RequestReasonCode).IsRequired().HasMaxLength(80);
        builder.Property(value => value.ReviewReasonCode).HasMaxLength(80);
        builder.Ignore(value => value.Trigger);
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Event>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.EventId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new
        {
            value.TenantId,
            value.EventId,
            value.OrganizerActorId,
            value.PaidEventPolicyVersionId,
            value.CurrencyCode,
            value.TriggerId,
            value.StatusCode
        });
    }
}
