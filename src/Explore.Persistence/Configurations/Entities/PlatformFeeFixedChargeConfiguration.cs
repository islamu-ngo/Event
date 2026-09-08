using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class PlatformFeeFixedChargeConfiguration : IEntityTypeConfiguration<PlatformFeeFixedCharge>
{
    public void Configure(EntityTypeBuilder<PlatformFeeFixedCharge> builder)
    {
        builder.Property(charge => charge.Id).ValueGeneratedNever();
        builder.Property(charge => charge.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(charge => charge.AmountMinor).HasColumnType("bigint");
        builder.HasIndex("PlatformFeePolicyId", nameof(PlatformFeeFixedCharge.CurrencyCode)).IsUnique();
    }
}
