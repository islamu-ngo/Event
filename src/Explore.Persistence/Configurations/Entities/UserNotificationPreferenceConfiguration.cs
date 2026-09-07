using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class UserNotificationPreferenceConfiguration : IEntityTypeConfiguration<UserNotificationPreference>
{
    public void Configure(EntityTypeBuilder<UserNotificationPreference> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();

        builder.Property(e => e.Category)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(e => e.IsEnabled)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .HasDefaultValueSql("NOW()")
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .HasDefaultValueSql("NOW()");

        builder.HasIndex(e => new { e.TenantId, e.UserId, e.Category })
            .IsUnique();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
