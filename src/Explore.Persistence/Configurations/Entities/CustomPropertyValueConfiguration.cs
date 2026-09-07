using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class CustomPropertyValueConfiguration : IEntityTypeConfiguration<CustomPropertyValue>
{
    public void Configure(EntityTypeBuilder<CustomPropertyValue> builder)
    {

        builder.Property(e => e.Id)
            .HasValueGenerator<GuidVersion7ValueGenerator>();

        builder.Property(e => e.ConcurrencyStamp)
            .IsConcurrencyToken();

        builder.Property(e => e.TextValue)
            .HasMaxLength(4000);

        builder.Property(e => e.NumberValue)
            .HasPrecision(19, 4);

        builder.Property(e => e.Ordinal)
            .HasDefaultValue(0);

        // Relationships
        builder.HasOne(e => e.Definition)
            .WithMany(d => d.Values)
            .HasForeignKey(e => e.CustomPropertyDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Option)
            .WithMany()
            .HasForeignKey(e => e.OptionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.EntityId })
            .HasDatabaseName("ix_cpv_tenant_entity");

        builder.HasIndex(e => new { e.CustomPropertyDefinitionId, e.EntityId, e.Ordinal })
            .HasDatabaseName("ix_cpv_definition_entity_ordinal")
            .IsUnique();

        builder.HasIndex(e => new { e.TenantId, e.CustomPropertyDefinitionId })
            .HasDatabaseName("ix_cpv_tenant_definition");
    }
}
