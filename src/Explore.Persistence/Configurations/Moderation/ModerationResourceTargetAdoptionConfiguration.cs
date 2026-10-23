using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationResourceTargetAdoptionConfiguration : IEntityTypeConfiguration<ModerationResourceTargetAdoption>
{
    public void Configure(EntityTypeBuilder<ModerationResourceTargetAdoption> builder)
    {
        builder.ToTable("moderation_resource_target_adoptions", table =>
        {
            table.HasCheckConstraint("ck_moderation_resource_target_adoption_revision",
                "selection_revision = 1 AND generation_revision > 0 AND generation_writer_generation > 0 AND case_revision > 0 AND writer_generation > 0");
            table.HasCheckConstraint("ck_moderation_resource_target_adoption_validity", "valid_until > adopted_at");
            table.HasCheckConstraint("ck_moderation_resource_target_adoption_revocation",
                "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL) OR " +
                "(revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL AND revoked_at >= adopted_at)");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.HasIndex(row => new { row.SelectionId, row.BindingOrdinal }).IsUnique();
        builder.HasOne<ModerationContributionBinding>().WithMany()
            .HasForeignKey(row => new { row.SelectionId, row.BindingOrdinal }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationResourceGeneration>().WithMany().HasForeignKey(row => row.GenerationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.AdoptedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.RevokedByHumanId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties().Where(property =>
            property.Name is not (nameof(ModerationResourceTargetAdoption.Revision)
                or nameof(ModerationResourceTargetAdoption.RevokedAt) or nameof(ModerationResourceTargetAdoption.RevokedByHumanId))))
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
