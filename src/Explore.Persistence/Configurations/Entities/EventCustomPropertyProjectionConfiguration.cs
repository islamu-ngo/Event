using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class EventCustomPropertyProjectionConfiguration : IEntityTypeConfiguration<EventCustomPropertyProjection>
{
    public void Configure(EntityTypeBuilder<EventCustomPropertyProjection> builder)
    {
        builder.Property(e => e.Id)
            .HasValueGenerator<GuidVersion7ValueGenerator>();

        builder.Property(e => e.Namespace)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Key)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.PropertyType)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(e => e.ExposureLevel)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(e => e.TextValue)
            .HasMaxLength(4000);

        builder.Property(e => e.NumberValue)
            .HasPrecision(19, 4);

        builder.Property(e => e.NormalizedValue)
            .HasMaxLength(4000);

        builder.Property(e => e.Ordinal)
            .HasDefaultValue(0);

        builder.HasOne(e => e.Definition)
            .WithMany()
            .HasForeignKey(e => e.EventCustomPropertyDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Value)
            .WithMany()
            .HasForeignKey(e => e.EventCustomPropertyValueId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Event)
            .WithMany()
            .HasForeignKey(e => e.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Option)
            .WithMany()
            .HasForeignKey(e => e.OptionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.EventCustomPropertyValueId)
            .IsUnique();

        builder.HasIndex(e => new { e.TenantId, e.Namespace, e.Key, e.NormalizedValue });

        builder.HasIndex(e => new { e.TenantId, e.ExposureLevel });

        builder.HasIndex(e => new { e.TenantId, e.EventId, e.Namespace, e.Key, e.Ordinal });
    }
}
