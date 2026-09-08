using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class EventTemplateCustomPropertyOptionConfiguration : IEntityTypeConfiguration<EventTemplateCustomPropertyOption>
{
    public void Configure(EntityTypeBuilder<EventTemplateCustomPropertyOption> builder)
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

        builder.Property(e => e.Value)
            .HasMaxLength(500)
            .IsRequired();

        builder.HasOne(e => e.ParentOption)
            .WithMany(e => e.ChildOptions)
            .HasForeignKey(e => e.ParentOptionId)
            .HasConstraintName("fk_event_template_custom_property_options_parent")
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Definition)
            .WithMany(e => e.Options)
            .HasForeignKey(e => e.EventTemplateCustomPropertyDefinitionId)
            .HasConstraintName("fk_event_template_custom_property_options_definition")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.EventTemplateCustomPropertyDefinitionId, e.SortOrder });

        builder.HasIndex(e => new { e.EventTemplateCustomPropertyDefinitionId, e.Namespace, e.Key })
            .IsUnique();
    }
}
