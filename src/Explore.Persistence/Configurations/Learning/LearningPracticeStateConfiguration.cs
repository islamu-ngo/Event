using Explore.Domain.Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Learning;

public sealed class LearningPracticeStateConfiguration : IEntityTypeConfiguration<LearningPracticeState>
{
    public void Configure(EntityTypeBuilder<LearningPracticeState> builder)
    {
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.Revision).IsConcurrencyToken();
        builder.Property(value => value.ContextKey).HasMaxLength(42).IsUnicode(false).IsRequired();
        builder.Property(value => value.RecipeKey).HasMaxLength(32).IsUnicode(false).IsRequired();
        builder.Property(value => value.LastIntentFingerprint).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Ignore(value => value.PayloadBytes);
        builder.HasIndex(value => new { value.TenantId, value.UserLearningStateId, value.ContextKey, value.RecipeKey })
            .IsUnique();
    }
}
