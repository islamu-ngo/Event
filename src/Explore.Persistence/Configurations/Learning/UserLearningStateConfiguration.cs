using Explore.Domain;
using Explore.Domain.Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Learning;

public sealed class UserLearningStateConfiguration : IEntityTypeConfiguration<UserLearningState>
{
    public void Configure(EntityTypeBuilder<UserLearningState> builder)
    {
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.Revision).IsConcurrencyToken();
        builder.Property(value => value.ActiveContextKey).HasMaxLength(42).IsUnicode(false);
        builder.Property(value => value.LastIntentFingerprint).HasMaxLength(64).IsUnicode(false);
        builder.Ignore(value => value.TotalPracticePayloadBytes);
        builder.HasIndex(value => new { value.TenantId, value.UserId }).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
        // SQL Server's provider convention marks the model before configurations run.
        // Keep orphan deletion in EF without introducing database cascade paths.
        var childDeleteBehavior = builder.Metadata.Model.FindAnnotation("SqlServer:ValueGenerationStrategy") is not null
            ? DeleteBehavior.ClientCascade : DeleteBehavior.Cascade;
        builder.HasMany(value => value.Tours).WithOne()
            .HasForeignKey(value => new { value.TenantId, value.UserLearningStateId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(childDeleteBehavior);
        builder.Navigation(value => value.Tours).HasField("_tours").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(value => value.Practices).WithOne()
            .HasForeignKey(value => new { value.TenantId, value.UserLearningStateId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(childDeleteBehavior);
        builder.Navigation(value => value.Practices).HasField("_practices").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
