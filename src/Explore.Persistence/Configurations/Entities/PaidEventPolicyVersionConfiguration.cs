using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class PaidEventPolicyVersionConfiguration : IEntityTypeConfiguration<PaidEventPolicyVersion>
{
    public void Configure(EntityTypeBuilder<PaidEventPolicyVersion> builder)
    {
        builder.Property(policy => policy.Id).ValueGeneratedNever();
        builder.Property(policy => policy.PolicyScopeKey).IsRequired().HasMaxLength(48);
        builder.Property(policy => policy.ActiveUniquenessSlot).IsRequired();
        builder.Property(policy => policy.DefaultCurrencyCode).HasMaxLength(3);
        builder.Property(policy => policy.CreatedAt).IsRequired();

        builder.Ignore(policy => policy.AllowedOrganizerKinds);
        builder.Ignore(policy => policy.AllowedCurrencyCodes);
        builder.Ignore(policy => policy.RefundProtections);
        builder.Ignore(policy => policy.CurrencyRiskLimits);

        builder.HasAlternateKey(policy => new { policy.PolicyScopeKey, policy.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(policy => policy.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany<PaidEventPolicyAllowedOrganizerKind>("AllowedOrganizerKindRows")
            .WithOne()
            .HasForeignKey(nameof(PaidEventPolicyAllowedOrganizerKind.PolicyScopeKey), nameof(PaidEventPolicyAllowedOrganizerKind.PaidEventPolicyVersionId))
            .HasPrincipalKey(nameof(PaidEventPolicyVersion.PolicyScopeKey), nameof(PaidEventPolicyVersion.Id))
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany<PaidEventPolicyAllowedCurrency>("AllowedCurrencyRows")
            .WithOne()
            .HasForeignKey(nameof(PaidEventPolicyAllowedCurrency.PolicyScopeKey), nameof(PaidEventPolicyAllowedCurrency.PaidEventPolicyVersionId))
            .HasPrincipalKey(nameof(PaidEventPolicyVersion.PolicyScopeKey), nameof(PaidEventPolicyVersion.Id))
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany<PaidEventPolicyRefundProtection>("RefundProtectionRows")
            .WithOne()
            .HasForeignKey(nameof(PaidEventPolicyRefundProtection.PolicyScopeKey), nameof(PaidEventPolicyRefundProtection.PaidEventPolicyVersionId))
            .HasPrincipalKey(nameof(PaidEventPolicyVersion.PolicyScopeKey), nameof(PaidEventPolicyVersion.Id))
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany<PaidEventPolicyCurrencyRiskLimitRow>("CurrencyRiskLimitRows")
            .WithOne()
            .HasForeignKey(nameof(PaidEventPolicyCurrencyRiskLimitRow.PolicyScopeKey), nameof(PaidEventPolicyCurrencyRiskLimitRow.PaidEventPolicyVersionId))
            .HasPrincipalKey(nameof(PaidEventPolicyVersion.PolicyScopeKey), nameof(PaidEventPolicyVersion.Id))
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation("AllowedOrganizerKindRows")
            .HasField("_allowedOrganizerKinds")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();
        builder.Navigation("AllowedCurrencyRows")
            .HasField("_allowedCurrencyCodes")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();
        builder.Navigation("RefundProtectionRows")
            .HasField("_refundProtections")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();
        builder.Navigation("CurrencyRiskLimitRows")
            .HasField("_currencyRiskLimits")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();

        builder.HasIndex(policy => new { policy.PolicyScopeKey, policy.VersionNumber }).IsUnique();
        builder.HasIndex(policy => new { policy.PolicyScopeKey, policy.ActiveUniquenessSlot }).IsUnique();
    }
}

public sealed class PaidEventPolicyAllowedOrganizerKindConfiguration : IEntityTypeConfiguration<PaidEventPolicyAllowedOrganizerKind>
{
    public void Configure(EntityTypeBuilder<PaidEventPolicyAllowedOrganizerKind> builder)
    {
        builder.HasKey(row => new { row.PolicyScopeKey, row.PaidEventPolicyVersionId, row.Ordinal });
        builder.Property(row => row.TenantId);
        builder.Property(row => row.PolicyScopeKey).IsRequired().HasMaxLength(48);
        builder.Property(row => row.ActorTypeId).IsRequired();
        builder.Ignore(row => row.ActorType);
        builder.HasIndex(row => new { row.PolicyScopeKey, row.PaidEventPolicyVersionId, row.ActorTypeId }).IsUnique();
    }
}

public sealed class PaidEventPolicyAllowedCurrencyConfiguration : IEntityTypeConfiguration<PaidEventPolicyAllowedCurrency>
{
    public void Configure(EntityTypeBuilder<PaidEventPolicyAllowedCurrency> builder)
    {
        builder.HasKey(row => new { row.PolicyScopeKey, row.PaidEventPolicyVersionId, row.Ordinal });
        builder.Property(row => row.TenantId);
        builder.Property(row => row.PolicyScopeKey).IsRequired().HasMaxLength(48);
        builder.Property(row => row.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.HasIndex(row => new { row.PolicyScopeKey, row.PaidEventPolicyVersionId, row.CurrencyCode }).IsUnique();
    }
}

public sealed class PaidEventPolicyRefundProtectionConfiguration : IEntityTypeConfiguration<PaidEventPolicyRefundProtection>
{
    public void Configure(EntityTypeBuilder<PaidEventPolicyRefundProtection> builder)
    {
        builder.HasKey(row => new { row.PolicyScopeKey, row.PaidEventPolicyVersionId, row.Ordinal });
        builder.Property(row => row.TenantId);
        builder.Property(row => row.PolicyScopeKey).IsRequired().HasMaxLength(48);
        builder.Property(row => row.RefundProtectionId).IsRequired();
        builder.Ignore(row => row.Protection);
        builder.HasIndex(row => new { row.PolicyScopeKey, row.PaidEventPolicyVersionId, row.RefundProtectionId }).IsUnique();
    }
}

public sealed class PaidEventPolicyCurrencyRiskLimitRowConfiguration : IEntityTypeConfiguration<PaidEventPolicyCurrencyRiskLimitRow>
{
    public void Configure(EntityTypeBuilder<PaidEventPolicyCurrencyRiskLimitRow> builder)
    {
        builder.HasKey(row => new { row.PolicyScopeKey, row.PaidEventPolicyVersionId, row.Ordinal });
        builder.Property(row => row.TenantId);
        builder.Property(row => row.PolicyScopeKey).IsRequired().HasMaxLength(48);
        builder.Property(row => row.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(row => row.PerEventSalesCeilingMinor).HasColumnType("bigint");
        builder.Property(row => row.PerEventSalesCountCeiling);
        builder.Property(row => row.RollingOrganizerSalesCeilingMinor).HasColumnType("bigint");
        builder.Property(row => row.RollingOrganizerSalesCountCeiling);
        builder.Property(row => row.RollingOrganizerWindowDays);
        builder.Property(row => row.HighValueReviewThresholdMinor).HasColumnType("bigint");
        builder.HasIndex(row => new { row.PolicyScopeKey, row.PaidEventPolicyVersionId, row.CurrencyCode }).IsUnique();
    }
}
