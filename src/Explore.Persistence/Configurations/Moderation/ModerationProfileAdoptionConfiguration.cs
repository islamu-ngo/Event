using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationProfileAdoptionConfiguration : IEntityTypeConfiguration<ModerationProfileAdoption>
{
    public void Configure(EntityTypeBuilder<ModerationProfileAdoption> builder)
    {
        builder.ToTable("moderation_profile_adoptions", table =>
        {
            table.HasCheckConstraint("ck_moderation_profile_adoption_revision",
                "selection_revision = 1 AND selection_writer_generation > 0 AND case_revision > 0 AND writer_generation > 0");
            table.HasCheckConstraint("ck_moderation_profile_adoption_mode", "mode_code BETWEEN 1 AND 2");
            table.HasCheckConstraint("ck_moderation_profile_adoption_validity", "valid_until > adopted_at");
            table.HasCheckConstraint("ck_moderation_profile_adoption_revocation",
                "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL) OR " +
                "(revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL AND revoked_at >= adopted_at)");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.HasIndex(row => row.SelectionId).IsUnique();
        builder.HasOne<ModerationContributionSelection>().WithMany().HasForeignKey(row => row.SelectionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationPolicyPublication>().WithMany().HasForeignKey(row => row.PublicationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.AdoptedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.RevokedByHumanId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties().Where(property =>
            property.Name is not (nameof(ModerationProfileAdoption.Revision)
                or nameof(ModerationProfileAdoption.RevokedAt) or nameof(ModerationProfileAdoption.RevokedByHumanId))))
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
