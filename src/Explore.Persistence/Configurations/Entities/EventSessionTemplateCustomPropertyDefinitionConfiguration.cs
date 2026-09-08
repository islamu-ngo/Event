using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class EventSessionTemplateCustomPropertyDefinitionConfiguration : IEntityTypeConfiguration<EventSessionTemplateCustomPropertyDefinition>
{
    public void Configure(EntityTypeBuilder<EventSessionTemplateCustomPropertyDefinition> builder)
    {
        builder.Property(e => e.Id)
            .HasValueGenerator<GuidVersion7ValueGenerator>();

        builder.Property(e => e.ConcurrencyStamp)
            .IsConcurrencyToken();

        builder.Property(e => e.Namespace)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Key)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.DisplayName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Description)
            .HasMaxLength(500);

        builder.Property(e => e.PropertyType)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(e => e.ExposureLevel)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(e => e.DefaultTextValue)
            .HasMaxLength(1000);

        builder.Property(e => e.RegexPattern)
            .HasMaxLength(1000);

        builder.Property(e => e.AllowedUrlSchemes)
            .HasMaxLength(500);

        builder.Property(e => e.DefaultNumberValue)
            .HasPrecision(19, 4);

        builder.Property(e => e.MinNumber)
            .HasPrecision(19, 4);

        builder.Property(e => e.MaxNumber)
            .HasPrecision(19, 4);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.DefaultOption)
            .WithMany()
            .HasForeignKey(e => e.DefaultOptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Options)
            .WithOne(o => o.Definition)
            .HasForeignKey(o => o.EventSessionTemplateCustomPropertyDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.EventSessionTemplateId, e.Namespace, e.Key })
            .IsUnique();

        builder.HasIndex(e => new { e.TenantId, e.IsSearchable, e.IsFilterable });
    }
}
