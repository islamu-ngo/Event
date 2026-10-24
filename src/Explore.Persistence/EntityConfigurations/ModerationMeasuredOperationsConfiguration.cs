using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.EntityConfigurations;

public sealed class ModerationMeasuredOperationsSourceConfiguration : IEntityTypeConfiguration<ModerationMeasuredOperationsSource>
{
    public void Configure(EntityTypeBuilder<ModerationMeasuredOperationsSource> builder)
    {
        builder.ToTable("moderation_measured_operations_sources", table =>
        {
            table.HasCheckConstraint("ck_moderation_measured_source_generation", "revision = 1 AND policy_revision > 0 AND writer_generation > 0");
            table.HasCheckConstraint("ck_moderation_measured_source_mode", "mode_code BETWEEN 1 AND 2");
            table.HasCheckConstraint("ck_moderation_measured_source_validity", "valid_until > recorded_at");
            table.HasCheckConstraint("ck_moderation_measured_source_original",
                "(original_effect_id IS NULL AND original_snapshot_hash IS NULL AND original_effect_binding_hash IS NULL AND original_custody_until IS NULL) OR " +
                "(original_effect_id IS NOT NULL AND original_snapshot_hash IS NOT NULL AND original_effect_binding_hash IS NOT NULL " +
                "AND original_custody_until IS NOT NULL AND original_custody_until > recorded_at AND valid_until <= original_custody_until)");
            table.HasCheckConstraint("ck_moderation_measured_source_independence",
                "reviewer_human_id <> recorded_by_human_id AND reviewer_controller_human_id <> recorder_controller_human_id " +
                "AND reviewer_human_id <> recorder_controller_human_id AND recorded_by_human_id <> reviewer_controller_human_id");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Property(row => row.BindingDigest).HasMaxLength(64).IsRequired();
        builder.Property(row => row.ProposedStateDigest).HasMaxLength(64).IsRequired();
        builder.Property(row => row.OriginalSnapshotHash).HasMaxLength(64);
        builder.Property(row => row.OriginalEffectBindingHash).HasMaxLength(64);
        builder.Property(row => row.EvidenceJson).HasMaxLength(65536).IsRequired();
        builder.Ignore(row => row.Evidence);
        builder.HasIndex(row => new { row.HostBindingId, row.TenantId, row.BindingDigest, row.WriterGeneration }).IsUnique();
        builder.HasOne<ModerationPolicyPublication>().WithMany().HasForeignKey(row => row.DefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationEffectIntent>().WithMany().HasForeignKey(row => row.OriginalEffectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.RecordedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.ReviewerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.ReviewerControllerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.RecorderControllerHumanId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties()) property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class ModerationMeasuredOperationsAcceptanceConfiguration : IEntityTypeConfiguration<ModerationMeasuredOperationsAcceptance>
{
    public void Configure(EntityTypeBuilder<ModerationMeasuredOperationsAcceptance> builder)
    {
        builder.ToTable("moderation_measured_operations_acceptances", table =>
        {
            table.HasCheckConstraint("ck_moderation_measured_acceptance_generation",
                "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
            table.HasCheckConstraint("ck_moderation_measured_acceptance_validity", "valid_until > accepted_at");
            table.HasCheckConstraint("ck_moderation_measured_acceptance_withdrawal",
                "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR " +
                "(revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= accepted_at)");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.HasIndex(row => row.SourceId).IsUnique();
        builder.HasOne<ModerationMeasuredOperationsSource>().WithMany().HasForeignKey(row => row.SourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.AcceptedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.WithdrawnByHumanId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties().Where(property =>
            property.Name is not (nameof(ModerationMeasuredOperationsAcceptance.Revision)
                or nameof(ModerationMeasuredOperationsAcceptance.WithdrawnAt) or nameof(ModerationMeasuredOperationsAcceptance.WithdrawnByHumanId))))
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
