using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class PaidOrderAcceptanceSnapshotConfiguration : IEntityTypeConfiguration<PaidOrderAcceptanceSnapshot>
{
    public void Configure(EntityTypeBuilder<PaidOrderAcceptanceSnapshot> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_paid_order_acceptance_amounts", "organizer_amount_minor > 0 AND platform_fee_minor >= 0 AND platform_fee_minor <= organizer_amount_minor AND platform_contribution_minor >= 0 AND total_minor = organizer_amount_minor + platform_contribution_minor");
            table.HasCheckConstraint("ck_paid_order_acceptance_refund_version", "refund_policy_version > 0");
            table.HasCheckConstraint("ck_paid_order_acceptance_delivery", "delivery_ends_at_utc > delivery_starts_at_utc");
        });
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.CompositionRevision).IsRequired().HasMaxLength(80);
        builder.Property(value => value.DisclosureRevision).IsRequired().HasMaxLength(80);
        builder.Property(value => value.AcceptanceTemplateIdentifier).IsRequired().HasMaxLength(80);
        builder.Property(value => value.AcceptanceTemplateText).IsRequired().HasMaxLength(PaidOrderAcceptanceSnapshot.MaxDisclosureLength);
        builder.Property(value => value.MerchantDisclosureText).IsRequired().HasMaxLength(PaidOrderAcceptanceSnapshot.MaxDisclosureLength);
        builder.Property(value => value.ConnectPlatformId).IsRequired().HasMaxLength(120);
        builder.Property(value => value.ExternalAccountId).IsRequired().HasMaxLength(200);
        builder.Property(value => value.MerchantCountryCode).IsRequired().HasMaxLength(2);
        builder.Property(value => value.TenantDirectoryOperatorPublicName).IsRequired().HasMaxLength(200);
        builder.Property(value => value.TenantDirectoryOperatorLegalName).IsRequired().HasMaxLength(300);
        builder.Property(value => value.TenantDirectoryOperatorKindCode).IsRequired().HasMaxLength(80);
        builder.Property(value => value.TenantDirectoryOperatorCountryCode).IsRequired().HasMaxLength(2);
        builder.Property(value => value.TenantDirectoryOperatorRegistrationIdentifier).HasMaxLength(120);
        builder.Property(value => value.TenantDirectoryOperatorPublicContactEmail).IsRequired().HasMaxLength(PaidOrderAcceptanceSnapshot.MaxContactLength);
        builder.Property(value => value.TenantDirectoryOperatorLegalNoticeUrl).IsRequired().HasMaxLength(500);
        builder.Property(value => value.TenantDirectoryOperatorTermsUrl).IsRequired().HasMaxLength(500);
        builder.Property(value => value.TenantDirectoryOperatorPrivacyUrl).IsRequired().HasMaxLength(500);
        builder.Property(value => value.OperatorDisplayName).IsRequired().HasMaxLength(PaidOrderAcceptanceSnapshot.MaxDisplayNameLength);
        builder.Property(value => value.OperatorLegalName).IsRequired().HasMaxLength(300);
        builder.Property(value => value.OperatorKindCode).IsRequired().HasMaxLength(80);
        builder.Property(value => value.OperatorRegistrationIdentifier).HasMaxLength(120);
        builder.Property(value => value.OfficialOrigin).IsRequired().HasMaxLength(500);
        builder.Property(value => value.OperatorRegionCode).IsRequired().HasMaxLength(8);
        builder.Property(value => value.OperatorWebsiteUrl).IsRequired().HasMaxLength(500);
        builder.Property(value => value.OperatorLegalNoticeUrl).IsRequired().HasMaxLength(500);
        builder.Property(value => value.OperatorTermsUrl).IsRequired().HasMaxLength(500);
        builder.Property(value => value.OperatorPrivacyUrl).IsRequired().HasMaxLength(500);
        builder.Property(value => value.ComplaintContact).IsRequired().HasMaxLength(PaidOrderAcceptanceSnapshot.MaxContactLength);
        builder.Property(value => value.ComplaintOwner).IsRequired().HasMaxLength(PaidOrderAcceptanceSnapshot.MaxDisplayNameLength);
        builder.Property(value => value.RefundOwner).IsRequired().HasMaxLength(PaidOrderAcceptanceSnapshot.MaxDisplayNameLength);
        builder.Property(value => value.DisputeOwner).IsRequired().HasMaxLength(PaidOrderAcceptanceSnapshot.MaxDisplayNameLength);
        builder.Property(value => value.ReconciliationOwner).IsRequired().HasMaxLength(PaidOrderAcceptanceSnapshot.MaxDisplayNameLength);
        builder.Property(value => value.ActivationStatus).IsRequired().HasMaxLength(32);
        builder.Property(value => value.DeliveryStartsAtUtc).IsRequired();
        builder.Property(value => value.DeliveryEndsAtUtc).IsRequired();
        builder.Property(value => value.EventTimeZoneId).IsRequired().HasMaxLength(100);
        builder.Property(value => value.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(value => value.RefundPolicyText).IsRequired().HasMaxLength(PaidOrderAcceptanceSnapshot.MaxDisclosureLength);
        builder.Property(value => value.RefundPolicyLanguageTag).IsRequired().HasMaxLength(35);
        builder.Property(value => value.SupportContact).IsRequired().HasMaxLength(PaidOrderAcceptanceSnapshot.MaxContactLength);
        builder.Property(value => value.ProviderCode).IsRequired().HasMaxLength(40);
        builder.Property(value => value.ProviderProfileCode).IsRequired().HasMaxLength(40);
        builder.Property(value => value.ChargeType).IsRequired().HasMaxLength(40);
        builder.Property(value => value.StatementDescriptor).IsRequired().HasMaxLength(22);
        builder.Property(value => value.ProviderEnvironment).IsRequired().HasMaxLength(16);
        builder.Property(value => value.ProviderCredentialOwner).IsRequired().HasMaxLength(80);
        builder.Property(value => value.AcceptedAt).IsRequired();
        builder.Property(value => value.CreatedAt).IsRequired();
        builder.Ignore(value => value.TenantDirectoryOperator);
        builder.Ignore(value => value.Operator);
        builder.Ignore(value => value.InstanceOperator);
        builder.Ignore(value => value.PaymentOperations);
        builder.Ignore(value => value.Delivery);
        builder.Ignore(value => value.Provider);
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationOrder>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.RegistrationOrderId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Event>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.EventId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(value => value.Lines)
            .WithOne()
            .HasForeignKey(value => new { value.TenantId, value.PaidOrderAcceptanceSnapshotId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(value => value.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        builder.HasIndex(value => new { value.TenantId, value.RegistrationOrderId, value.DisclosureRevision }).IsUnique();
        builder.HasIndex(value => new { value.TenantId, value.EventId, value.AcceptedAt });
    }
}

public sealed class PaidOrderAcceptanceLineConfiguration : IEntityTypeConfiguration<PaidOrderAcceptanceLine>
{
    public void Configure(EntityTypeBuilder<PaidOrderAcceptanceLine> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_paid_order_acceptance_lines_shape",
            "ordinal >= 0 AND quantity > 0 AND unit_amount_minor >= 0 AND discount_amount_minor >= 0 AND line_total_minor >= 0 AND discount_amount_minor <= unit_amount_minor * quantity AND line_total_minor = unit_amount_minor * quantity - discount_amount_minor"));
        builder.HasKey(value => new { value.TenantId, value.PaidOrderAcceptanceSnapshotId, value.Ordinal });
        builder.Property(value => value.Name).IsRequired().HasMaxLength(300);
        builder.HasIndex(value => new { value.TenantId, value.PaidOrderAcceptanceSnapshotId, value.OrderLineId }).IsUnique();
    }
}
