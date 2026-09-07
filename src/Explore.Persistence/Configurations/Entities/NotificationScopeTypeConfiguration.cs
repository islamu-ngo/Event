using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class NotificationScopeTypeConfiguration : IEntityTypeConfiguration<NotificationScopeType>
{
    public void Configure(EntityTypeBuilder<NotificationScopeType> builder)
    {
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.MasterCode).IsRequired().HasMaxLength(50);
        builder.Property(e => e.FullName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.HasIndex(e => e.MasterCode).IsUnique();
    }
}
