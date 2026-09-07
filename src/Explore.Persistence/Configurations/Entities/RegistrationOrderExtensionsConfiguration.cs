using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationOrderPiiConfiguration : IEntityTypeConfiguration<RegistrationOrderPii>
{
    public void Configure(EntityTypeBuilder<RegistrationOrderPii> builder)
    {
        builder.HasKey(pii => pii.RegistrationOrderId);
        builder.Property(pii => pii.ContactName).HasMaxLength(200);
        builder.Property(pii => pii.Email).HasMaxLength(320);
        builder.Property(pii => pii.NormalizedEmail).HasMaxLength(320);
        builder.Property(pii => pii.IsEmailVerified).HasDefaultValue(false);
        builder.Property(pii => pii.Phone).HasMaxLength(50);
        builder.Property(pii => pii.OrganizationName).HasMaxLength(200);
        builder.Property(pii => pii.RetentionUntil).HasColumnType("timestamp with time zone");
        builder.Property(pii => pii.CreatedAt).IsRequired();
        builder.HasAlternateKey(pii => new { pii.TenantId, pii.RegistrationOrderId });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(pii => pii.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(pii => pii.RegistrationOrder).WithOne(order => order.Pii)
            .HasForeignKey<RegistrationOrderPii>(pii => new { pii.TenantId, pii.RegistrationOrderId })
            .HasPrincipalKey<RegistrationOrder>(order => new { order.TenantId, order.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(pii => new { pii.TenantId, pii.NormalizedEmail });
    }
}

public sealed class RegistrationOrderPlatformContributionConfiguration : IEntityTypeConfiguration<RegistrationOrderPlatformContribution>
{
    public void Configure(EntityTypeBuilder<RegistrationOrderPlatformContribution> builder)
    {
        builder.Property(contribution => contribution.Id).ValueGeneratedNever();
        builder.Property(contribution => contribution.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(contribution => contribution.AmountMinor).HasColumnType("bigint");
        builder.Property(contribution => contribution.CreatedAt).IsRequired();
        builder.HasAlternateKey(contribution => new { contribution.TenantId, contribution.Id });
        builder.HasIndex(contribution => new { contribution.TenantId, contribution.RegistrationOrderId }).IsUnique();
    }
}

