using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class EventSessionCustomPropertyProjectionConfiguration : IEntityTypeConfiguration<EventSessionCustomPropertyProjection>
{
    public void Configure(EntityTypeBuilder<EventSessionCustomPropertyProjection> builder)
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
            .HasForeignKey(e => e.EventSessionCustomPropertyDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Value)
            .WithMany()
            .HasForeignKey(e => e.EventSessionCustomPropertyValueId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.EventSession)
            .WithMany()
            .HasForeignKey(e => e.EventSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Option)
            .WithMany()
            .HasForeignKey(e => e.OptionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.EventSessionCustomPropertyValueId)
            .IsUnique();

        builder.HasIndex(e => new { e.TenantId, e.Namespace, e.Key, e.NormalizedValue });

        builder.HasIndex(e => new { e.TenantId, e.ExposureLevel });

        builder.HasIndex(e => new { e.TenantId, e.EventSessionId, e.Namespace, e.Key, e.Ordinal });
    }
}
