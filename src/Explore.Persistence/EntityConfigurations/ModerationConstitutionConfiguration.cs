using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.EntityConfigurations;

public sealed class ModerationConstitutionSourceConfiguration : IEntityTypeConfiguration<ModerationConstitutionSource>
{
    public void Configure(EntityTypeBuilder<ModerationConstitutionSource> builder)
    {
        builder.ToTable("moderation_constitution_sources", table =>
        {
            table.HasCheckConstraint("ck_moderation_constitution_source_revision", "revision = 1 AND policy_revision > 0 AND writer_generation > 0");
            table.HasCheckConstraint("ck_moderation_constitution_source_mode", "mode_code BETWEEN 1 AND 2");
            table.HasCheckConstraint("ck_moderation_constitution_source_validity", "valid_until > recorded_at");
            table.HasCheckConstraint("ck_moderation_constitution_original_purpose",
                "(original_effect_id IS NULL AND original_snapshot_hash IS NULL AND original_effect_binding_hash IS NULL AND original_custody_until IS NULL) OR " +
                "(original_effect_id IS NOT NULL AND original_snapshot_hash IS NOT NULL AND original_effect_binding_hash IS NOT NULL " +
                "AND original_custody_until IS NOT NULL AND valid_until <= original_custody_until)");
            table.HasCheckConstraint("ck_moderation_constitution_substance",
                "evidence = 1 AND remedies = 1 AND independence = 1 AND exposure = 1 AND overload = 1 " +
                "AND minimum_independent_reviewers BETWEEN 1 AND 32 AND protected_complaint_calendar_months >= 6 " +
                "AND maximum_review_interval_ticks > 0 AND escalation_after_ticks > 0 AND coordinator_human_id <> fallback_human_id");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Property(row => row.ProposedStateDigest).HasMaxLength(64).IsRequired();
        builder.Property(row => row.OriginalSnapshotHash).HasMaxLength(64);
        builder.Property(row => row.OriginalEffectBindingHash).HasMaxLength(64);
        builder.Property(row => row.SupportRoute).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.IndependentRedressRoute).HasMaxLength(2048).IsRequired();
        builder.Ignore(row => row.Terms);
        builder.HasIndex(row => new { row.HostBindingId, row.TenantId, row.PublicationId, row.ProfileId, row.ModeCode, row.WriterGeneration }).IsUnique();
        builder.HasOne<ModerationPolicyPublication>().WithMany().HasForeignKey(row => row.DefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationEffectIntent>().WithMany().HasForeignKey(row => row.OriginalEffectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.RecordedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.CoordinatorHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.FallbackHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Reviewers).WithOne().HasForeignKey(row => row.SourceId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties()) property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class ModerationConstitutionReviewerConfiguration : IEntityTypeConfiguration<ModerationConstitutionReviewer>
{
    public void Configure(EntityTypeBuilder<ModerationConstitutionReviewer> builder)
    {
        builder.ToTable("moderation_constitution_reviewers", table =>
            table.HasCheckConstraint("ck_moderation_constitution_reviewer_generation",
                "ordinal >= 0 AND ordinal < 32 AND availability_revision = 1 AND availability_writer_generation > 0"));
        builder.HasKey(row => new { row.SourceId, row.Ordinal });
        builder.Property(row => row.IdentityBasis).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.QualificationBasis).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.IndependenceBasis).HasMaxLength(2048).IsRequired();
        builder.HasIndex(row => new { row.SourceId, row.ReviewerHumanId }).IsUnique();
        builder.HasIndex(row => new { row.SourceId, row.ControllerHumanId }).IsUnique();
        builder.HasIndex(row => new { row.SourceId, row.AvailabilityId }).IsUnique();
        builder.HasOne<ModerationConstitutionAvailability>().WithMany().HasForeignKey(row => row.AvailabilityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.ReviewerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.ControllerHumanId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties()) property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class ModerationConstitutionAdoptionConfiguration : IEntityTypeConfiguration<ModerationConstitutionAdoption>
{
    public void Configure(EntityTypeBuilder<ModerationConstitutionAdoption> builder)
    {
        builder.ToTable("moderation_constitution_adoptions", table =>
        {
            table.HasCheckConstraint("ck_moderation_constitution_adoption_generation",
                "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
            table.HasCheckConstraint("ck_moderation_constitution_adoption_validity", "valid_until > adopted_at");
            table.HasCheckConstraint("ck_moderation_constitution_adoption_withdrawal",
                "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR " +
                "(revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.HasIndex(row => row.SourceId).IsUnique();
        builder.HasOne<ModerationConstitutionSource>().WithMany().HasForeignKey(row => row.SourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.AdoptedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.WithdrawnByHumanId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties().Where(property =>
            property.Name is not (nameof(ModerationConstitutionAdoption.Revision)
                or nameof(ModerationConstitutionAdoption.WithdrawnAt) or nameof(ModerationConstitutionAdoption.WithdrawnByHumanId))))
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class ModerationConstitutionAvailabilityConfiguration : IEntityTypeConfiguration<ModerationConstitutionAvailability>
{
    public void Configure(EntityTypeBuilder<ModerationConstitutionAvailability> builder)
    {
        builder.ToTable("moderation_constitution_availability", table =>
        {
            table.HasCheckConstraint("ck_moderation_constitution_availability_capacity", "maximum_caseload > 0 AND writer_generation > 0");
            table.HasCheckConstraint("ck_moderation_constitution_availability_validity", "valid_until > recorded_at");
            table.HasCheckConstraint("ck_moderation_constitution_availability_withdrawal",
                "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR " +
                "(revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= recorded_at)");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.HasIndex(row => new { row.HostBindingId, row.TenantId, row.ReviewerHumanId, row.WriterGeneration }).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.ReviewerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.WithdrawnByHumanId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties().Where(property =>
            property.Name is not (nameof(ModerationConstitutionAvailability.Revision)
                or nameof(ModerationConstitutionAvailability.WithdrawnAt) or nameof(ModerationConstitutionAvailability.WithdrawnByHumanId))))
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
