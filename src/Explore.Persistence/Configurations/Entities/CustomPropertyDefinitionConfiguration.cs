using Explore.Domain;
using Explore.Domain.References;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class CustomPropertyDefinitionConfiguration : IEntityTypeConfiguration<CustomPropertyDefinition>
{
    public void Configure(EntityTypeBuilder<CustomPropertyDefinition> builder)
    {
        builder.ToTable(t =>
            t.HasCheckConstraint(
                "ck_custom_property_definitions_shared_entity_type",
                BuildSharedEntityTypeConstraint()));

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

        builder.Property(e => e.EntityTypeName)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(e => e.PropertyType)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(e => e.ExposureLevel)
            .HasConversion<string>()
            .HasMaxLength(50);

        // Relationships
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
            .HasForeignKey(o => o.CustomPropertyDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Values)
            .WithOne(v => v.Definition)
            .HasForeignKey(v => v.CustomPropertyDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.EntityTypeName, e.Namespace, e.Key })
            .HasDatabaseName("ix_cpd_tenant_entity_namespace_key")
            .IsUnique();

        builder.HasIndex(e => new { e.TenantId, e.EntityTypeName, e.IsActive })
            .HasDatabaseName("ix_cpd_tenant_entity_active");

        builder.HasIndex(e => new { e.TenantId, e.EntityTypeName, e.IsSearchable, e.IsFilterable })
            .HasDatabaseName("ix_cpd_tenant_entity_search_filter");
    }

    private static string BuildSharedEntityTypeConstraint()
    {
        var sharedEntityTypes = ReferenceTypeRegistry.AllCustomPropertyTargets
            .Where(target => target.SupportsSharedDefinitions)
            .Select(target => SqlLiteral(target.EntityTypeName.ToString()));

        return $"entity_type_name IN ({string.Join(", ", sharedEntityTypes)})";
    }

    private static string SqlLiteral(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
}
