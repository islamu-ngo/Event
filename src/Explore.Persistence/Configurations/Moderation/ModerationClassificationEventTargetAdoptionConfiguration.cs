using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationClassificationEventTargetAdoptionConfiguration :
    IEntityTypeConfiguration<ModerationClassificationEventTargetAdoption>
{
    public void Configure(EntityTypeBuilder<ModerationClassificationEventTargetAdoption> builder)
    {
        builder.ToTable("moderation_classification_event_target_adoptions", table =>
        {
            table.HasCheckConstraint("ck_classification_event_adoption_generations",
                "selection_revision = 1 AND selection_writer_generation > 0 " +
                "AND generation_revision > 0 AND generation_revision < 9223372036854775807 " +
                "AND generation_writer_generation > 0 AND writer_generation > selection_writer_generation " +
                "AND writer_generation > generation_writer_generation AND writer_generation < 9223372036854775807 " +
                "AND binding_ordinal >= 0 AND binding_ordinal < 64");
            table.HasCheckConstraint("ck_classification_event_adoption_identity",
                "id <> selection_id AND id <> generation_id AND selection_id <> generation_id");
            table.HasCheckConstraint("ck_classification_event_adoption_validity",
                "selection_valid_until > selection_recorded_at AND generation_valid_until > generation_observed_at " +
                "AND valid_until > adopted_at AND adopted_at >= selection_recorded_at AND adopted_at >= generation_observed_at " +
                "AND valid_until <= selection_valid_until AND valid_until <= generation_valid_until");
            table.HasCheckConstraint("ck_classification_event_adoption_independence",
                "adopted_by_human_id <> owner_human_id AND adopted_by_human_id <> owner_controller_human_id " +
                "AND adopted_by_human_id <> reviewer_human_id AND adopted_by_human_id <> reviewer_controller_human_id " +
                "AND adopted_by_human_id <> selection_authored_by_human_id");
            table.HasCheckConstraint("ck_classification_event_adoption_revocation",
                "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL " +
                "AND revocation_writer_generation IS NULL AND revocation_grounds IS NULL) OR " +
                "(revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL " +
                "AND revocation_writer_generation IS NOT NULL AND revocation_writer_generation > writer_generation " +
                "AND revocation_writer_generation < 9223372036854775807 AND revocation_grounds IS NOT NULL " +
                "AND length(trim(revocation_grounds)) > 0 AND length(revocation_grounds) <= 2048 AND revoked_at >= adopted_at)");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Property(row => row.RevocationGrounds).HasMaxLength(2048);
        builder.HasIndex(row => new { row.SelectionId, row.BindingOrdinal }).IsUnique();
        builder.HasOne<ModerationClassificationControlBinding>().WithMany()
            .HasForeignKey(row => new { row.SelectionId, row.BindingOrdinal }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationEventGeneration>().WithMany().HasForeignKey(row => row.GenerationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationHostBinding>().WithMany().HasForeignKey(row => row.HostBindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.AdoptedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.RevokedByHumanId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties().Where(property =>
            property.Name is not (nameof(ModerationClassificationEventTargetAdoption.Revision)
                or nameof(ModerationClassificationEventTargetAdoption.RevokedByHumanId)
                or nameof(ModerationClassificationEventTargetAdoption.RevocationWriterGeneration)
                or nameof(ModerationClassificationEventTargetAdoption.RevokedAt)
                or nameof(ModerationClassificationEventTargetAdoption.RevocationGrounds))))
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
