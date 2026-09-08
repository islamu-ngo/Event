using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Identity.Configurations;

public sealed class IdentityUserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder)
    {
        builder.HasKey(claim => claim.Id);
        builder.Property(claim => claim.ClaimType).HasMaxLength(256);
        builder.Property(claim => claim.ClaimValue).HasMaxLength(2_048);
    }
}
