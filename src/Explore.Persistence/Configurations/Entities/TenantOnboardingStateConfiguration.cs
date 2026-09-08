using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class TenantOnboardingStateConfiguration : IEntityTypeConfiguration<TenantOnboardingState>
{
    public void Configure(EntityTypeBuilder<TenantOnboardingState> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasValueGenerator<GuidVersion7ValueGenerator>();

        builder.Property(e => e.IsCompleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CurrentStep)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.TotalSteps)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.CompletedStepsJson)
            .HasColumnType("jsonb");

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.HasIndex(e => e.TenantId)
            .IsUnique();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
