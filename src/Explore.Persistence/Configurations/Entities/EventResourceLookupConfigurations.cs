using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventResourceKindConfiguration : IEntityTypeConfiguration<EventResourceKind>
{
    public void Configure(EntityTypeBuilder<EventResourceKind> builder) => ConfigureLookup(builder);

    private static void ConfigureLookup(EntityTypeBuilder<EventResourceKind> builder)
    {
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.MasterCode).IsRequired().HasMaxLength(100);
        builder.Property(value => value.FullName).IsRequired().HasMaxLength(200);
        builder.Property(value => value.Description).HasMaxLength(500);
        builder.HasIndex(value => value.MasterCode).IsUnique();
    }
}

public sealed class EventResourceDeliveryTypeConfiguration : IEntityTypeConfiguration<EventResourceDeliveryType>
{
    public void Configure(EntityTypeBuilder<EventResourceDeliveryType> builder)
    {
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.MasterCode).IsRequired().HasMaxLength(100);
        builder.Property(value => value.FullName).IsRequired().HasMaxLength(200);
        builder.Property(value => value.Description).HasMaxLength(500);
        builder.HasIndex(value => value.MasterCode).IsUnique();
    }
}
