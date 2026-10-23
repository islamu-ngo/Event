using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.EntityConfigurations;

public sealed class ModerationClassificationMaterialSourceConfiguration : IEntityTypeConfiguration<ModerationClassificationMaterialSource>
{
    public void Configure(EntityTypeBuilder<ModerationClassificationMaterialSource> builder)
    {
        builder.ToTable("moderation_classification_material_sources", table =>
        {
            table.HasCheckConstraint("ck_moderation_material_generation",
                "revision = 1 AND previous_generation >= 0 AND event_generation_revision > 0 AND event_writer_generation > 0 " +
                "AND taxonomy_source_revision = 1 AND taxonomy_adoption_revision = 1 AND taxonomy_source_generation > 0 " +
                "AND taxonomy_adoption_generation > taxonomy_source_generation AND writer_generation > taxonomy_adoption_generation " +
                "AND writer_generation > event_writer_generation AND writer_generation > previous_generation " +
                "AND owner_assignment_revision > 0 AND policy_revision > 0 AND mode_code BETWEEN 1 AND 2");
            table.HasCheckConstraint("ck_moderation_material_validity", "valid_until > recorded_at");
            table.HasCheckConstraint("ck_moderation_material_independence",
                "owner_human_id <> reviewer_human_id AND owner_controller_human_id <> reviewer_controller_human_id " +
                "AND owner_human_id <> reviewer_controller_human_id AND reviewer_human_id <> owner_controller_human_id");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Property(row => row.OwnershipGrounds).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.ReviewGrounds).HasMaxLength(2048).IsRequired();
        builder.Ignore(row => row.Mandate);
        builder.HasMany(row => row.Materials).WithOne().HasForeignKey(row => row.SourceId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(row => row.Materials).HasField("_materials").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(row => row.Components).WithOne().HasForeignKey(row => row.SourceId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(row => row.Components).HasField("_components").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(row => new { row.HostBindingId, row.TenantId, row.EventId, row.WriterGeneration }).IsUnique();
        builder.HasOne<ModerationEventGeneration>().WithMany().HasForeignKey(row => row.EventGenerationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationClassificationTaxonomySource>().WithMany().HasForeignKey(row => row.TaxonomySourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationClassificationTaxonomyAdoption>().WithMany().HasForeignKey(row => row.TaxonomyAdoptionId).OnDelete(DeleteBehavior.Restrict);
        // Assignment identity is retained scalar provenance. Revocation/deletion must not prevent
        // accountable post-expiry correction or make an earlier Event manager current again.
        foreach (var property in builder.Metadata.GetProperties()) property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class ModerationClassificationMaterialEntryConfiguration : IEntityTypeConfiguration<ModerationClassificationMaterialEntry>
{
    public void Configure(EntityTypeBuilder<ModerationClassificationMaterialEntry> builder)
    {
        builder.ToTable("moderation_classification_material_entries", table =>
        {
            table.HasCheckConstraint("ck_moderation_material_entry_ordinal", "ordinal BETWEEN 0 AND 4095");
            table.HasCheckConstraint("ck_moderation_material_entry_ancestry",
                "((session_id IS NULL AND session_stamp IS NULL) OR (session_id IS NOT NULL AND session_stamp IS NOT NULL)) " +
                "AND ((resource_id IS NULL AND resource_stamp IS NULL) OR (resource_id IS NOT NULL AND resource_stamp IS NOT NULL))");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Ignore(row => row.Material);
        builder.Ignore(row => row.Selection);
        builder.HasIndex(row => new { row.SourceId, row.Ordinal }).IsUnique();
        builder.HasMany(row => row.Categories).WithOne().HasForeignKey(row => row.EntryId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(row => row.Categories).HasField("_categories").UsePropertyAccessMode(PropertyAccessMode.Field);
        foreach (var property in builder.Metadata.GetProperties()) property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class ModerationClassificationMaterialCategoryConfiguration : IEntityTypeConfiguration<ModerationClassificationMaterialCategory>
{
    public void Configure(EntityTypeBuilder<ModerationClassificationMaterialCategory> builder)
    {
        builder.ToTable("moderation_classification_material_categories",
            table => table.HasCheckConstraint("ck_moderation_material_category_ordinal", "ordinal BETWEEN 0 AND 63"));
        builder.HasKey(row => new { row.EntryId, row.Ordinal });
        builder.HasIndex(row => new { row.EntryId, row.VersionId }).IsUnique();
        builder.HasOne<ModerationClassificationTaxonomyCategory>().WithMany().HasForeignKey(row => row.VersionId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties()) property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class ModerationClassificationMaterialComponentConfiguration : IEntityTypeConfiguration<ModerationClassificationMaterialComponent>
{
    public void Configure(EntityTypeBuilder<ModerationClassificationMaterialComponent> builder)
    {
        builder.ToTable("moderation_classification_material_components",
            table => table.HasCheckConstraint("ck_moderation_material_component_kind", "kind BETWEEN 1 AND 41 AND ordinal BETWEEN 0 AND 4095"));
        builder.HasKey(row => new { row.SourceId, row.Ordinal });
        builder.HasIndex(row => new { row.SourceId, row.Kind, row.NativeId }).IsUnique();
        builder.Property(row => row.Digest).HasMaxLength(64).IsRequired();
        builder.Ignore(row => row.Version);
        foreach (var property in builder.Metadata.GetProperties()) property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class ModerationClassificationMaterialReviewConfiguration : IEntityTypeConfiguration<ModerationClassificationMaterialReview>
{
    public void Configure(EntityTypeBuilder<ModerationClassificationMaterialReview> builder)
    {
        builder.ToTable("moderation_classification_material_reviews", table =>
        {
            table.HasCheckConstraint("ck_moderation_material_review_generation",
                "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
            table.HasCheckConstraint("ck_moderation_material_review_validity", "valid_until > accepted_at");
            table.HasCheckConstraint("ck_moderation_material_review_ending",
                "(revision = 1 AND end_kind IS NULL AND ended_at IS NULL AND ended_by_human_id IS NULL " +
                "AND end_writer_generation IS NULL AND end_grounds IS NULL) OR " +
                "(revision = 2 AND end_kind BETWEEN 1 AND 2 AND ended_at IS NOT NULL AND ended_by_human_id IS NOT NULL " +
                "AND end_writer_generation > writer_generation AND end_grounds IS NOT NULL AND ended_at >= accepted_at)");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Property(row => row.EndGrounds).HasMaxLength(2048);
        builder.HasIndex(row => row.SourceId).IsUnique();
        builder.HasOne<ModerationClassificationMaterialSource>().WithMany().HasForeignKey(row => row.SourceId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties().Where(property =>
            property.Name is not (nameof(ModerationClassificationMaterialReview.Revision)
                or nameof(ModerationClassificationMaterialReview.EndKind) or nameof(ModerationClassificationMaterialReview.EndedAt)
                or nameof(ModerationClassificationMaterialReview.EndedByHumanId) or nameof(ModerationClassificationMaterialReview.EndWriterGeneration)
                or nameof(ModerationClassificationMaterialReview.EndGrounds))))
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
