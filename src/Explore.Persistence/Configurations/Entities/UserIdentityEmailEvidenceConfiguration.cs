using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class UserIdentityEmailEvidenceConfiguration : IEntityTypeConfiguration<UserIdentityEmailEvidence>
{
    public void Configure(EntityTypeBuilder<UserIdentityEmailEvidence> builder)
    {
        builder.HasKey(evidence => evidence.Id);
        builder.Property(evidence => evidence.Id).ValueGeneratedNever();
        builder.HasIndex(evidence => evidence.ExternalLoginId).IsUnique();
        builder.HasOne<UserIdentityEmailClaim>().WithMany(claim => claim.Evidence)
            .HasForeignKey(evidence => new { evidence.ClaimId, evidence.UserId })
            .HasPrincipalKey(claim => new { claim.Id, claim.UserId })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<UserExternalLogin>().WithMany()
            .HasForeignKey(evidence => new { evidence.ExternalLoginId, evidence.UserId })
            .HasPrincipalKey(binding => new { binding.Id, binding.UserId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
