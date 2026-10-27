using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationClassificationControlSelectionConfiguration :
    IEntityTypeConfiguration<ModerationClassificationControlSelection>,
    IEntityTypeConfiguration<ModerationClassificationControlBinding>
{
    public void Configure(EntityTypeBuilder<ModerationClassificationControlSelection> builder)
    {
        builder.ToTable("moderation_classification_control_selections", table =>
        {
            table.HasCheckConstraint("ck_classification_control_generations",
                "revision = 1 AND source_revision = 1 AND review_revision = 1 " +
                "AND generation_revision > 0 AND generation_revision < 9223372036854775807 " +
                "AND generation_writer_generation > 0 AND source_writer_generation > generation_writer_generation " +
                "AND review_writer_generation > source_writer_generation AND writer_generation > review_writer_generation " +
                "AND writer_generation < 9223372036854775807 AND policy_revision > 0 " +
                "AND policy_revision < 9223372036854775807 AND mode_code IN (1, 2)");
            table.HasCheckConstraint("ck_classification_control_identity",
                "id <> source_id AND id <> review_id AND id <> generation_id " +
                "AND source_id <> review_id AND source_id <> generation_id AND review_id <> generation_id");
            table.HasCheckConstraint("ck_classification_control_validity",
                "source_valid_until > source_recorded_at AND review_valid_until > starts_at " +
                "AND generation_valid_until > generation_observed_at AND valid_until > recorded_at " +
                "AND starts_at >= source_recorded_at AND starts_at >= generation_observed_at AND recorded_at >= starts_at " +
                "AND source_valid_until <= generation_valid_until AND review_valid_until <= source_valid_until " +
                "AND valid_until <= review_valid_until");
            table.HasCheckConstraint("ck_classification_control_independence",
                "reviewer_human_id <> owner_human_id AND reviewer_human_id <> owner_controller_human_id " +
                "AND reviewer_controller_human_id <> owner_human_id AND reviewer_controller_human_id <> owner_controller_human_id " +
                "AND authored_by_human_id <> owner_human_id AND authored_by_human_id <> owner_controller_human_id " +
                "AND authored_by_human_id <> reviewer_human_id AND authored_by_human_id <> reviewer_controller_human_id");
            table.HasCheckConstraint("ck_classification_control_digest", "length(selection_digest) = 64");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.SelectionDigest).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.HasIndex(row => new { row.HostBindingId, row.TrustDomainId, row.TenantId, row.EventId, row.ReviewId });
        builder.HasOne<ModerationHostBinding>().WithMany().HasForeignKey(row => row.HostBindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationClassificationMaterialSource>().WithMany().HasForeignKey(row => row.SourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationClassificationMaterialReview>().WithMany().HasForeignKey(row => row.ReviewId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationEventGeneration>().WithMany().HasForeignKey(row => row.GenerationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationPolicyPublication>().WithMany().HasForeignKey(row => row.PublicationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.AuthoredByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Bindings).WithOne().HasForeignKey(row => row.SelectionId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(row => row.Bindings).UsePropertyAccessMode(PropertyAccessMode.Field);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }

    public void Configure(EntityTypeBuilder<ModerationClassificationControlBinding> builder)
    {
        builder.ToTable("moderation_classification_control_bindings", table =>
        {
            table.HasCheckConstraint("ck_classification_control_binding_partition",
                "ordinal >= 0 AND ordinal < 64 AND target_revision > 0 AND target_revision < 9223372036854775807 " +
                "AND subject_kind = 4 AND subject_id = target_id AND target_id = scope_event_id AND window_end IS NULL");
            table.HasCheckConstraint("ck_classification_control_binding_meaning",
                "(kind = 1 AND effect_kind BETWEEN 1 AND 26 AND effect_kind IS NOT NULL " +
                "AND effect_mode BETWEEN 1 AND 8 AND effect_mode IS NOT NULL AND effect_target = 12 AND effect_target IS NOT NULL " +
                "AND execution = 2 AND execution IS NOT NULL AND strategy IS NULL " +
                "AND (capability IS NULL OR capability BETWEEN 1 AND 12)) OR " +
                "(kind = 2 AND effect_kind IS NULL AND effect_mode IS NULL AND effect_target IS NULL " +
                "AND capability IS NULL AND execution IS NULL AND (strategy IS NULL OR strategy IN (1, 2, 3)))");
            table.HasCheckConstraint("ck_classification_control_binding_event", "scope_kind = 7");
        });
        builder.HasKey(row => new { row.SelectionId, row.Ordinal });
        builder.Property(row => row.Ordinal).ValueGeneratedNever();
        builder.Property(row => row.Operation).HasMaxLength(100).IsRequired();
        builder.Ignore(row => row.Value);
        ModerationLedgerConfiguration.ConfigureScope(builder, "scope");
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
