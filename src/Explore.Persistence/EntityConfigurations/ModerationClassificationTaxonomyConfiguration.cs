using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.EntityConfigurations;

public sealed class ModerationClassificationTaxonomySourceConfiguration : IEntityTypeConfiguration<ModerationClassificationTaxonomySource>
{
    public void Configure(EntityTypeBuilder<ModerationClassificationTaxonomySource> builder)
    {
        builder.ToTable("moderation_classification_taxonomy_sources", table =>
        {
            table.HasCheckConstraint("ck_moderation_taxonomy_generation",
                "revision = 1 AND taxonomy_revision > 0 AND previous_generation >= 0 AND writer_generation > previous_generation " +
                "AND child_age_source_revision = 1 AND child_age_adoption_revision = 1 " +
                "AND constitution_source_revision = 1 AND constitution_adoption_revision = 1 " +
                "AND child_age_source_generation > 0 AND child_age_adoption_generation > child_age_source_generation " +
                "AND constitution_source_generation > 0 AND constitution_adoption_generation > constitution_source_generation " +
                "AND writer_generation > child_age_adoption_generation AND writer_generation > constitution_adoption_generation");
            table.HasCheckConstraint("ck_moderation_taxonomy_validity", "valid_until > recorded_at");
            table.HasCheckConstraint("ck_moderation_taxonomy_mode", "mode_code BETWEEN 1 AND 2 AND policy_revision > 0");
            table.HasCheckConstraint("ck_moderation_taxonomy_independence",
                "owner_human_id <> reviewer_human_id AND owner_controller_human_id <> reviewer_controller_human_id " +
                "AND owner_human_id <> reviewer_controller_human_id AND reviewer_human_id <> owner_controller_human_id");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Property(row => row.ProposedStateDigest).HasMaxLength(64).IsRequired();
        builder.Property(row => row.OwnershipGrounds).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.ReviewMandateGrounds).HasMaxLength(2048).IsRequired();
        builder.Ignore(row => row.Mandate);
        builder.HasMany(row => row.Categories).WithOne().HasForeignKey(row => row.SourceId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(row => row.Categories).HasField("_categories").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(row => new { row.HostBindingId, row.TenantId, row.TaxonomyId, row.WriterGeneration }).IsUnique();
        builder.HasIndex(row => new { row.HostBindingId, row.TenantId, row.TaxonomyId, row.TaxonomyRevision }).IsUnique();
        builder.HasOne<ModerationPolicyPublication>().WithMany().HasForeignKey(row => row.DefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationChildAgePolicySource>().WithMany().HasForeignKey(row => row.ChildAgeSourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationChildAgePolicyAdoption>().WithMany().HasForeignKey(row => row.ChildAgeAdoptionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationConstitutionSource>().WithMany().HasForeignKey(row => row.ConstitutionSourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationConstitutionAdoption>().WithMany().HasForeignKey(row => row.ConstitutionAdoptionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.OwnerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.OwnerControllerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.ReviewerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.ReviewerControllerHumanId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties()) property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class ModerationClassificationTaxonomyCategoryConfiguration : IEntityTypeConfiguration<ModerationClassificationTaxonomyCategory>
{
    public void Configure(EntityTypeBuilder<ModerationClassificationTaxonomyCategory> builder)
    {
        builder.ToTable("moderation_classification_taxonomy_categories", table =>
        {
            table.HasCheckConstraint("ck_moderation_taxonomy_category",
                "category_id > 0 AND ordinal BETWEEN 0 AND 63 AND kind BETWEEN 1 AND 4 AND minimum_age BETWEEN 0 AND 130");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Code).HasMaxLength(100).IsRequired();
        builder.Property(row => row.Meaning).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.ReviewRationale).HasMaxLength(2048).IsRequired();
        builder.Ignore(row => row.Terms);
        builder.HasIndex(row => new { row.SourceId, row.CategoryId }).IsUnique();
        builder.HasIndex(row => new { row.SourceId, row.Code }).IsUnique();
        builder.HasIndex(row => new { row.SourceId, row.Ordinal }).IsUnique();
        foreach (var property in builder.Metadata.GetProperties()) property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class ModerationClassificationTaxonomyAdoptionConfiguration : IEntityTypeConfiguration<ModerationClassificationTaxonomyAdoption>
{
    public void Configure(EntityTypeBuilder<ModerationClassificationTaxonomyAdoption> builder)
    {
        builder.ToTable("moderation_classification_taxonomy_adoptions", table =>
        {
            table.HasCheckConstraint("ck_moderation_taxonomy_adoption_generation",
                "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
            table.HasCheckConstraint("ck_moderation_taxonomy_adoption_validity", "valid_until > adopted_at");
            table.HasCheckConstraint("ck_moderation_taxonomy_adoption_withdrawal",
                "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR " +
                "(revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.HasIndex(row => row.SourceId).IsUnique();
        builder.HasOne<ModerationClassificationTaxonomySource>().WithMany().HasForeignKey(row => row.SourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.AdoptedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.WithdrawnByHumanId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties().Where(property =>
            property.Name is not (nameof(ModerationClassificationTaxonomyAdoption.Revision)
                or nameof(ModerationClassificationTaxonomyAdoption.WithdrawnAt)
                or nameof(ModerationClassificationTaxonomyAdoption.WithdrawnByHumanId))))
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
