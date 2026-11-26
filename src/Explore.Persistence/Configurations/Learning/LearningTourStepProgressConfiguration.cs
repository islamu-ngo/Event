using Explore.Domain.Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Learning;

public sealed class LearningTourStepProgressConfiguration : IEntityTypeConfiguration<LearningTourStepProgress>
{
    public void Configure(EntityTypeBuilder<LearningTourStepProgress> builder)
    {
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.StepKey).HasMaxLength(96).IsUnicode(false).IsRequired();
        builder.HasIndex(value => new { value.TenantId, value.TourProgressId, value.StepKey, value.StepVersion })
            .IsUnique();
    }
}
