using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class PlatformFeePolicyConfiguration : IEntityTypeConfiguration<PlatformFeePolicy>
{
    public void Configure(EntityTypeBuilder<PlatformFeePolicy> builder)
    {
        builder.Property(policy => policy.Id).ValueGeneratedNever();
        builder.Property(policy => policy.CreatedAt).IsRequired();
        builder.Property(policy => policy.FeeBasisPoints).HasColumnType("integer");
        builder.HasMany(policy => policy.FixedCharges).WithOne().HasForeignKey("PlatformFeePolicyId").OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(policy => policy.FixedCharges).HasField("_fixedCharges").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(policy => policy.VersionNumber).IsUnique();
        builder.HasIndex(policy => policy.IsActive).IsUnique().HasFilter("is_active = true");
    }
}
