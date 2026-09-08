using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class CustomPropertyOptionConfiguration : IEntityTypeConfiguration<CustomPropertyOption>
{
    public void Configure(EntityTypeBuilder<CustomPropertyOption> builder)
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

        builder.Property(e => e.Value)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(e => e.Description)
            .HasMaxLength(500);

        // Self-referencing hierarchy
        builder.HasOne(e => e.ParentOption)
            .WithMany(e => e.ChildOptions)
            .HasForeignKey(e => e.ParentOptionId)
            .OnDelete(DeleteBehavior.SetNull);

        // Ordered options within a definition
        builder.HasIndex(e => new { e.CustomPropertyDefinitionId, e.SortOrder })
            .HasDatabaseName("ix_cpo_definition_sort");

        builder.HasIndex(e => new { e.CustomPropertyDefinitionId, e.Namespace, e.Key })
            .HasDatabaseName("ix_cpo_definition_namespace_key")
            .IsUnique();
    }
}
