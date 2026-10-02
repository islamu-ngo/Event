using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class UserIdentityEmailClaimConfiguration : IEntityTypeConfiguration<UserIdentityEmailClaim>
{
    public void Configure(EntityTypeBuilder<UserIdentityEmailClaim> builder)
    {
        builder.HasKey(claim => claim.Id);
        builder.Property(claim => claim.Id).ValueGeneratedNever();
        builder.Property(claim => claim.NormalizedEmail).HasMaxLength(320).IsRequired();
        builder.HasIndex(claim => claim.NormalizedEmail).IsUnique();
        builder.HasAlternateKey(claim => new { claim.Id, claim.UserId });
        builder.HasOne<User>().WithMany(user => user.IdentityEmailClaims)
            .HasForeignKey(claim => claim.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
