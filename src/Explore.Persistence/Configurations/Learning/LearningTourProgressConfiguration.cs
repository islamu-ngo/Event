using Explore.Domain.Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Learning;

public sealed class LearningTourProgressConfiguration : IEntityTypeConfiguration<LearningTourProgress>
{
    public void Configure(EntityTypeBuilder<LearningTourProgress> builder)
    {
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.LastIntentFingerprint).HasMaxLength(64).IsUnicode(false);
        builder.HasIndex(value => new { value.TenantId, value.UserLearningStateId, value.Family, value.MaterialVersion })
            .IsUnique();
    }
}
