using Explore.Domain.Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Learning;

public sealed class LearningRequirementAcknowledgementConfiguration
    : IEntityTypeConfiguration<LearningRequirementAcknowledgement>
{
    public void Configure(EntityTypeBuilder<LearningRequirementAcknowledgement> builder)
    {
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.RequirementKey).HasMaxLength(32).IsUnicode(false).IsRequired();
        builder.Property(value => value.PolicySourceStamp).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(value => value.LastIntentFingerprint).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.HasIndex(value => new
            { value.TenantId, value.UserLearningStateId, value.RequirementKey, value.MaterialVersion }).IsUnique();
    }
}
