using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.EntityConfigurations;

public sealed class ModerationEventRightsSourceConfiguration : IEntityTypeConfiguration<ModerationEventRightsSource>
{
    public void Configure(EntityTypeBuilder<ModerationEventRightsSource> builder)
    {
        builder.ToTable("moderation_event_rights_sources", table =>
        {
            table.HasCheckConstraint("ck_moderation_event_rights_source_generation", "revision = 1 AND policy_revision > 0 AND writer_generation > 0");
            table.HasCheckConstraint("ck_moderation_event_rights_source_mode", "mode_code BETWEEN 1 AND 2");
            table.HasCheckConstraint("ck_moderation_event_rights_source_validity", "valid_until > recorded_at");
            table.HasCheckConstraint("ck_moderation_event_rights_terms",
                "continuity = 1 AND consent = 1 AND missing_steward = 1 AND physical_jurisdiction = 1 " +
                "AND settlement = 1 AND missing_settlement = 1 AND companions = 1 AND historical_commerce = 1 AND correction = 1");
            table.HasCheckConstraint("ck_moderation_event_rights_independence",
                "reviewer_human_id <> recorded_by_human_id AND reviewer_human_id <> continuity_mapping_owner_human_id " +
                "AND reviewer_human_id <> settlement_mapping_owner_human_id " +
                "AND reviewer_controller_human_id <> governance_controller_human_id " +
                "AND reviewer_controller_human_id <> continuity_mapping_controller_human_id " +
                "AND reviewer_controller_human_id <> settlement_mapping_controller_human_id");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Property(row => row.ProposedStateDigest).HasMaxLength(64).IsRequired();
        builder.Property(row => row.EventRightsGrounds).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.ContinuityGrounds).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.PremisesJurisdictionGrounds).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.SettlementMappingGrounds).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.ControllerAndIndependenceGrounds).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.RightsReviewMandateGrounds).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.ContinuityMappingMandateGrounds).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.SettlementMappingMandateGrounds).HasMaxLength(2048).IsRequired();
        builder.Ignore(row => row.Terms);
        builder.Ignore(row => row.Ownership);
        builder.HasIndex(row => new { row.HostBindingId, row.TenantId, row.PublicationId, row.ProfileId, row.ModeCode, row.WriterGeneration }).IsUnique();
        builder.HasOne<ModerationPolicyPublication>().WithMany().HasForeignKey(row => row.DefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.RecordedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.ReviewerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.ContinuityMappingOwnerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.SettlementMappingOwnerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.ReviewerControllerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.ContinuityMappingControllerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.SettlementMappingControllerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.GovernanceControllerHumanId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties()) property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class ModerationEventRightsAdoptionConfiguration : IEntityTypeConfiguration<ModerationEventRightsAdoption>
{
    public void Configure(EntityTypeBuilder<ModerationEventRightsAdoption> builder)
    {
        builder.ToTable("moderation_event_rights_adoptions", table =>
        {
            table.HasCheckConstraint("ck_moderation_event_rights_adoption_generation",
                "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
            table.HasCheckConstraint("ck_moderation_event_rights_adoption_validity", "valid_until > adopted_at");
            table.HasCheckConstraint("ck_moderation_event_rights_adoption_withdrawal",
                "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR " +
                "(revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.HasIndex(row => row.SourceId).IsUnique();
        builder.HasOne<ModerationEventRightsSource>().WithMany().HasForeignKey(row => row.SourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.AdoptedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.WithdrawnByHumanId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties().Where(property =>
            property.Name is not (nameof(ModerationEventRightsAdoption.Revision)
                or nameof(ModerationEventRightsAdoption.WithdrawnAt) or nameof(ModerationEventRightsAdoption.WithdrawnByHumanId))))
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
