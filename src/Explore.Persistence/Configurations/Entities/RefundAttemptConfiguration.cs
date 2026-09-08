using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RefundAttemptConfiguration : IEntityTypeConfiguration<RefundAttempt>
{
    public void Configure(EntityTypeBuilder<RefundAttempt> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_refund_attempts_status", "status BETWEEN 1 AND 8");
            table.HasCheckConstraint(
                "ck_refund_attempts_allocation",
                "allocation_organizer_amount_minor >= 0 AND allocation_platform_fee_minor >= 0 AND allocation_platform_contribution_minor >= 0 AND allocation_total_minor > 0 AND allocation_platform_fee_minor <= allocation_organizer_amount_minor AND allocation_total_minor = allocation_organizer_amount_minor + allocation_platform_contribution_minor");
            table.HasCheckConstraint("ck_refund_attempts_policy_version", "refund_policy_version > 0");
            table.HasCheckConstraint(
                "ck_refund_attempts_fee_refund",
                "application_fee_refunded_amount_minor >= 0");
            table.HasCheckConstraint(
                "ck_refund_attempts_buyer_success_capacity",
                "buyer_refund_succeeded_at IS NULL OR status NOT IN (7, 8)");
        });
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.ProviderCode).IsRequired().HasMaxLength(40);
        builder.Property(value => value.ExternalAccountId).IsRequired().HasMaxLength(200);
        builder.Property(value => value.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(value => value.ProviderPaymentId).IsRequired().HasMaxLength(200);
        builder.Property(value => value.ProviderIdempotencyKey).IsRequired().HasMaxLength(160);
        builder.Property(value => value.ReservationSourceKey).IsRequired().HasMaxLength(48);
        builder.Property(value => value.AuthorityCode).IsRequired().HasMaxLength(40);
        builder.Property(value => value.ReasonCode).IsRequired().HasMaxLength(80);
        builder.Property(value => value.RefundPolicyText).IsRequired().HasMaxLength(PaidOrderAcceptanceSnapshot.MaxDisclosureLength);
        builder.Property(value => value.RefundPolicyLanguageTag).IsRequired().HasMaxLength(35);
        builder.Property(value => value.ProviderRefundId).HasMaxLength(200);
        builder.Property(value => value.LastProviderRequestId).HasMaxLength(120);
        builder.Property(value => value.FailureCode).HasMaxLength(80);
        builder.Property(value => value.Status).HasConversion<int>();
        builder.Property(value => value.CreatedAt).IsRequired();
        builder.Property(value => value.LastObservedAt).IsRequired();
        builder.Property(value => value.ConcurrencyStamp).IsConcurrencyToken();
        builder.OwnsOne(value => value.Allocation, owned =>
        {
            owned.Property(value => value.OrganizerAmountMinor).HasColumnName("allocation_organizer_amount_minor");
            owned.Property(value => value.PlatformFeeMinor).HasColumnName("allocation_platform_fee_minor");
            owned.Property(value => value.PlatformContributionMinor).HasColumnName("allocation_platform_contribution_minor");
            owned.Property(value => value.TotalMinor).HasColumnName("allocation_total_minor");
        });
        builder.Navigation(value => value.Allocation).IsRequired();
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PaymentAttempt>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.PaymentAttemptId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationOrder>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.RegistrationOrderId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PaidOrderAcceptanceSnapshot>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.PaidOrderAcceptanceSnapshotId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RefundCampaign>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.SourceCampaignId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(value => value.Lines).WithOne()
            .HasForeignKey(value => new { value.TenantId, value.RefundAttemptId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(value => new { value.TenantId, value.ProviderIdempotencyKey }).IsUnique();
        builder.HasIndex(value => new
        {
            value.TenantId,
            value.ReservationSourceKey,
            value.PaymentAttemptId,
            value.PaidOrderAcceptanceSnapshotId
        }).IsUnique();
        builder.HasIndex(value => new { value.TenantId, value.PaymentAttemptId, value.Status });
        builder.HasIndex(value => new { value.TenantId, value.SourceCampaignId, value.Status });
        builder.HasIndex(value => new { value.TenantId, value.ProviderCode, value.ExternalAccountId, value.ProviderRefundId });
    }
}

public sealed class RefundLineAllocationConfiguration : IEntityTypeConfiguration<RefundLineAllocation>
{
    public void Configure(EntityTypeBuilder<RefundLineAllocation> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_refund_line_allocations_money",
            "ordinal >= 0 AND organizer_amount_minor >= 0 AND platform_fee_minor >= 0 AND platform_contribution_minor >= 0 AND total_minor >= 0 AND platform_fee_minor <= organizer_amount_minor AND total_minor = organizer_amount_minor + platform_contribution_minor"));
        builder.HasKey(value => new { value.TenantId, value.RefundAttemptId, value.OrderLineId });
        builder.Property(value => value.TenantId).ValueGeneratedNever();
        builder.Property(value => value.RefundAttemptId).ValueGeneratedNever();
        builder.Property(value => value.OrderLineId).ValueGeneratedNever();
        builder.HasIndex(value => new { value.TenantId, value.RefundAttemptId, value.Ordinal }).IsUnique();
        builder.HasOne<PaidOrderAcceptanceLine>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.PaidOrderAcceptanceSnapshotId, value.OrderLineId })
            .HasPrincipalKey(value => new { value.TenantId, value.PaidOrderAcceptanceSnapshotId, value.OrderLineId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
