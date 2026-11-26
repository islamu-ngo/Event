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
        builder.Property(value => value.LastStepKey).HasMaxLength(96).IsUnicode(false);
        var childDeleteBehavior = builder.Metadata.Model.FindAnnotation("SqlServer:ValueGenerationStrategy") is not null
            ? DeleteBehavior.ClientCascade : DeleteBehavior.Cascade;
        builder.HasMany(value => value.Steps).WithOne()
            .HasForeignKey(value => new { value.TenantId, value.TourProgressId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(childDeleteBehavior);
        builder.Navigation(value => value.Steps).HasField("_steps").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(value => new { value.TenantId, value.UserLearningStateId, value.Family, value.MaterialVersion })
            .IsUnique();
    }
}
