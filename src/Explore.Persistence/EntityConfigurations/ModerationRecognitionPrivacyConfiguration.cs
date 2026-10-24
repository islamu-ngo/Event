using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.EntityConfigurations;

public sealed class ModerationRecognitionPrivacySourceConfiguration : IEntityTypeConfiguration<ModerationRecognitionPrivacySource>
{
    public void Configure(EntityTypeBuilder<ModerationRecognitionPrivacySource> builder)
    {
        builder.ToTable("moderation_recognition_privacy_sources", table =>
        {
            table.HasCheckConstraint("ck_moderation_recognition_privacy_source_generation", "revision = 1 AND policy_revision > 0 AND writer_generation > 0");
            table.HasCheckConstraint("ck_moderation_recognition_privacy_source_mode", "mode_code BETWEEN 1 AND 2");
            table.HasCheckConstraint("ck_moderation_recognition_privacy_source_validity", "valid_until > recorded_at");
            table.HasCheckConstraint("ck_moderation_recognition_privacy_terms",
                "purposes BETWEEN 1 AND 3 AND linkage = 1 AND ordinary_erasure = 1 AND correction = 1 " +
                "AND catastrophic_disposition = 1 AND key_obligations = 1 AND protected_remedy_calendar_months BETWEEN 1 AND 1200 " +
                "AND escalation_after_ticks > 0 AND lawful_request_deadline_ticks > escalation_after_ticks " +
                "AND maximum_backup_horizon_ticks > 0 AND backup_safety_margin_ticks > 0");
            table.HasCheckConstraint("ck_moderation_recognition_privacy_independence",
                "privacy_owner_human_id <> recorded_by_human_id AND recovery_owner_human_id <> recorded_by_human_id " +
                "AND privacy_owner_human_id <> recovery_owner_human_id AND privacy_controller_human_id <> recovery_controller_human_id " +
                "AND privacy_controller_human_id <> governance_controller_human_id AND recovery_controller_human_id <> governance_controller_human_id");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Property(row => row.ProposedStateDigest).HasMaxLength(64).IsRequired();
        builder.Property(row => row.IdentityAndIndependenceBasis).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.PrivacyLegalMandateBasis).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.RecoveryMandateBasis).HasMaxLength(2048).IsRequired();
        builder.Ignore(row => row.Terms);
        builder.Ignore(row => row.Ownership);
        builder.Ignore(row => row.MinimumReadableKeyHorizon);
        builder.HasIndex(row => new { row.HostBindingId, row.TenantId, row.PublicationId, row.ProfileId, row.ModeCode, row.WriterGeneration }).IsUnique();
        builder.HasOne<ModerationPolicyPublication>().WithMany().HasForeignKey(row => row.DefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.RecordedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.PrivacyOwnerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.RecoveryOwnerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.PrivacyControllerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.RecoveryControllerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.GovernanceControllerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Retention).WithOne().HasForeignKey(row => row.SourceId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(row => row.Retention).HasField("retention").UsePropertyAccessMode(PropertyAccessMode.Field);
        foreach (var property in builder.Metadata.GetProperties()) property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class ModerationRecognitionPrivacyRetentionConfiguration : IEntityTypeConfiguration<ModerationRecognitionPrivacyRetention>
{
    public void Configure(EntityTypeBuilder<ModerationRecognitionPrivacyRetention> builder)
    {
        builder.ToTable("moderation_recognition_privacy_retention", table =>
        {
            table.HasCheckConstraint("ck_moderation_recognition_privacy_retention", "record_class BETWEEN 1 AND 4 AND maximum_retention_ticks > 0");
            table.HasCheckConstraint("ck_moderation_recognition_privacy_hold",
                "(hold = 1 AND maximum_hold_ticks = 0 AND hold_review_within_ticks = 0) OR " +
                "(hold = 2 AND record_class IN (3, 4) AND maximum_hold_ticks > 0 AND hold_review_within_ticks > 0 AND hold_review_within_ticks <= maximum_hold_ticks)");
        });
        builder.HasKey(row => new { row.SourceId, row.RecordClass });
        foreach (var property in builder.Metadata.GetProperties()) property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class ModerationRecognitionPrivacyAdoptionConfiguration : IEntityTypeConfiguration<ModerationRecognitionPrivacyAdoption>
{
    public void Configure(EntityTypeBuilder<ModerationRecognitionPrivacyAdoption> builder)
    {
        builder.ToTable("moderation_recognition_privacy_adoptions", table =>
        {
            table.HasCheckConstraint("ck_moderation_recognition_privacy_adoption_generation",
                "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
            table.HasCheckConstraint("ck_moderation_recognition_privacy_adoption_validity", "valid_until > adopted_at");
            table.HasCheckConstraint("ck_moderation_recognition_privacy_adoption_withdrawal",
                "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR " +
                "(revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.HasIndex(row => row.SourceId).IsUnique();
        builder.HasOne<ModerationRecognitionPrivacySource>().WithMany().HasForeignKey(row => row.SourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.AdoptedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.WithdrawnByHumanId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties().Where(property =>
            property.Name is not (nameof(ModerationRecognitionPrivacyAdoption.Revision)
                or nameof(ModerationRecognitionPrivacyAdoption.WithdrawnAt) or nameof(ModerationRecognitionPrivacyAdoption.WithdrawnByHumanId))))
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
