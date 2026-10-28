using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.EntityConfigurations;

public sealed class ModerationChildAgePolicySourceConfiguration : IEntityTypeConfiguration<ModerationChildAgePolicySource>
{
    public void Configure(EntityTypeBuilder<ModerationChildAgePolicySource> builder)
    {
        builder.ToTable("moderation_child_age_policy_sources", table =>
        {
            table.HasCheckConstraint("ck_moderation_child_age_source_generation", "revision = 1 AND policy_revision > 0 AND writer_generation > 0");
            table.HasCheckConstraint("ck_moderation_child_age_source_mode", "mode_code BETWEEN 1 AND 2");
            table.HasCheckConstraint("ck_moderation_child_age_source_validity", "valid_until > recorded_at");
            table.HasCheckConstraint("ck_moderation_child_age_terms",
                "collection = 1 AND consent = 1 AND assurance = 1 AND evidence = 1 AND classification = 1 " +
                "AND debate_minimum_age = 18 AND audience = 1 AND participant = 1 AND correction = 1 " +
                "AND protected_access = 1 AND prohibited_inference = 1 AND calendar = 1 AND time_zone = 1 " +
                "AND leap_day = 1 AND adulthood = 1 AND adulthood_age BETWEEN 1 AND 130");
            table.HasCheckConstraint("ck_moderation_child_age_provider_review",
                "(provider_assurance = 1 AND provider_assurance_review_grounds IS NULL) OR " +
                "(provider_assurance = 2 AND provider_assurance_review_grounds IS NOT NULL)");
            table.HasCheckConstraint("ck_moderation_child_age_independence",
                "reviewer_human_id <> recorded_by_human_id AND collection_owner_human_id <> recorded_by_human_id " +
                "AND reviewer_human_id <> collection_owner_human_id AND reviewer_controller_human_id <> collection_controller_human_id " +
                "AND reviewer_controller_human_id <> governance_controller_human_id AND collection_controller_human_id <> governance_controller_human_id " +
                "AND reviewer_human_id <> collection_controller_human_id AND reviewer_human_id <> governance_controller_human_id " +
                "AND collection_owner_human_id <> reviewer_controller_human_id AND collection_owner_human_id <> governance_controller_human_id " +
                "AND recorded_by_human_id <> reviewer_controller_human_id AND recorded_by_human_id <> collection_controller_human_id");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Property(row => row.ProposedStateDigest).HasMaxLength(64).IsRequired();
        builder.Property(row => row.ChildRiskGrounds).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.PrivacyLegalGrounds).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.LawfulCollectionPurpose).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.ConsentGuardianApplicabilityGrounds).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.ManualAssuranceQualificationGrounds).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.ProviderAssuranceReviewGrounds).HasMaxLength(2048);
        builder.Property(row => row.ClassificationRationale).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.CalendarJurisdictionGrounds).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.IdentityAndIndependenceBasis).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.PrivacyLegalMandateBasis).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.ChildSafeguardingMandateBasis).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.CollectionMandateBasis).HasMaxLength(2048).IsRequired();
        builder.Ignore(row => row.Terms);
        builder.Ignore(row => row.Ownership);
        builder.HasIndex(row => new { row.HostBindingId, row.TenantId, row.PublicationId, row.ProfileId, row.ModeCode, row.WriterGeneration }).IsUnique();
        builder.HasOne<ModerationPolicyPublication>().WithMany().HasForeignKey(row => row.DefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.RecordedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.ReviewerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.CollectionOwnerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.ReviewerControllerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.CollectionControllerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.GovernanceControllerHumanId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties()) property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class ModerationChildAgePolicyAdoptionConfiguration : IEntityTypeConfiguration<ModerationChildAgePolicyAdoption>
{
    public void Configure(EntityTypeBuilder<ModerationChildAgePolicyAdoption> builder)
    {
        builder.ToTable("moderation_child_age_policy_adoptions", table =>
        {
            table.HasCheckConstraint("ck_moderation_child_age_adoption_generation",
                "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
            table.HasCheckConstraint("ck_moderation_child_age_adoption_validity", "valid_until > adopted_at");
            table.HasCheckConstraint("ck_moderation_child_age_adoption_withdrawal",
                "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR " +
                "(revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.HasIndex(row => row.SourceId).IsUnique();
        builder.HasOne<ModerationChildAgePolicySource>().WithMany().HasForeignKey(row => row.SourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.AdoptedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.WithdrawnByHumanId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties().Where(property =>
            property.Name is not (nameof(ModerationChildAgePolicyAdoption.Revision)
                or nameof(ModerationChildAgePolicyAdoption.WithdrawnAt) or nameof(ModerationChildAgePolicyAdoption.WithdrawnByHumanId))))
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
