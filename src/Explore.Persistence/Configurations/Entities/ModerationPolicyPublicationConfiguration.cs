using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ModerationPolicyPublicationConfiguration :
    IEntityTypeConfiguration<ModerationPolicyPublication>,
    IEntityTypeConfiguration<ModerationPolicyPublicationClass>,
    IEntityTypeConfiguration<ModerationPolicyPublicationReason>,
    IEntityTypeConfiguration<ModerationPolicyPublicationMode>,
    IEntityTypeConfiguration<ModerationPolicyPublicationProfile>,
    IEntityTypeConfiguration<ModerationPolicyPublicationScope>,
    IEntityTypeConfiguration<ModerationPolicyPublicationSubject>,
    IEntityTypeConfiguration<ModerationPolicyPublicationEffect>,
    IEntityTypeConfiguration<ModerationPolicyPublicationPlan>,
    IEntityTypeConfiguration<ModerationPolicyPublicationConsumer>,
    IEntityTypeConfiguration<ModerationPolicyPublicationPrerequisite>,
    IEntityTypeConfiguration<ModerationPolicyPublicationEvidence>,
    IEntityTypeConfiguration<ModerationPolicySelection>
{
    public void Configure(EntityTypeBuilder<ModerationPolicyPublication> builder)
    {
        builder.ToTable("moderation_policy_publications", table =>
        {
            table.HasCheckConstraint("ck_moderation_publication_revision",
                "revision > 0 AND authority_revision > 0 AND qualification_revision = authority_revision + 1");
            table.HasCheckConstraint("ck_moderation_publication_history",
                "(revision = 1 AND previous_publication_id IS NULL) OR (revision > 1 AND previous_publication_id IS NOT NULL)");
            table.HasCheckConstraint("ck_moderation_publication_adoption",
                "(adopted_revision IS NOT NULL AND adopted_revision = revision) OR (adopted_revision IS NULL AND published_active = false AND evidence_valid_until IS NULL)");
            table.HasCheckConstraint("ck_moderation_publication_duration",
                "maximum_finite_duration_ticks > 0 AND maximum_review_delay_ticks > 0");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.HasAlternateKey(row => new { row.PolicyId, row.Id });
        builder.HasIndex(row => new { row.PolicyId, row.AdoptedRevision }).IsUnique();
        builder.HasIndex(row => row.OperationId).IsUnique();
        builder.Property(row => row.RequestDigest).HasMaxLength(64);
        builder.Property(row => row.DeclaredStateDigest).HasMaxLength(64);
        builder.Property(row => row.InventoryFingerprint).HasMaxLength(64);
        builder.Property(row => row.WriterGeneration).HasMaxLength(256);
        builder.Ignore(row => row.Jurisdiction);
        builder.HasOne<ModerationHostBinding>().WithMany().HasForeignKey(row => row.HostBindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationQualificationEpoch>().WithMany().HasForeignKey(row => row.AuthoritySourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationPolicyPublication>().WithMany()
            .HasForeignKey(row => new { row.PolicyId, row.PreviousPublicationId })
            .HasPrincipalKey(row => new { row.PolicyId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Classes).WithOne().HasForeignKey(row => row.PublicationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Reasons).WithOne().HasForeignKey(row => row.PublicationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Profiles).WithOne().HasForeignKey(row => row.PublicationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Modes).WithOne().HasForeignKey(row => row.PublicationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Evidence).WithOne().HasForeignKey(row => row.PublicationId).OnDelete(DeleteBehavior.Restrict);
        Immutable(builder);
    }

    public void Configure(EntityTypeBuilder<ModerationPolicySelection> builder)
    {
        builder.ToTable("moderation_policy_selections", table =>
            table.HasCheckConstraint("ck_moderation_selection_revision", "revision > 0"));
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.HasIndex(row => new { row.HostBindingId, row.JurisdictionId }).IsUnique();
        builder.HasOne<ModerationPolicyPublication>().WithMany()
            .HasForeignKey(row => new { row.Id, row.LatestPublicationId })
            .HasPrincipalKey(row => new { row.PolicyId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationPolicyPublication>().WithMany()
            .HasForeignKey(row => new { row.Id, row.ActivePublicationId })
            .HasPrincipalKey(row => new { row.PolicyId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter("ModerationAuthority", row => false);
    }

    public void Configure(EntityTypeBuilder<ModerationPolicyPublicationClass> builder)
    {
        builder.ToTable("moderation_publication_classes");
        builder.HasKey(row => new { row.PublicationId, row.Code, row.Revision });
        builder.Property(row => row.Code).HasMaxLength(100);
        builder.Property(row => row.Meaning).HasMaxLength(4000);
        Immutable(builder);
    }

    public void Configure(EntityTypeBuilder<ModerationPolicyPublicationReason> builder)
    {
        builder.ToTable("moderation_publication_reasons");
        builder.HasKey(row => new { row.PublicationId, row.Code, row.Revision });
        builder.Property(row => row.Code).HasMaxLength(100);
        builder.Property(row => row.ClassCode).HasMaxLength(100);
        builder.Property(row => row.Meaning).HasMaxLength(4000);
        builder.HasOne<ModerationPolicyPublicationClass>().WithMany()
            .HasForeignKey(row => new { row.PublicationId, row.ClassCode, row.ClassRevision })
            .OnDelete(DeleteBehavior.Restrict);
        Immutable(builder);
    }

    public void Configure(EntityTypeBuilder<ModerationPolicyPublicationMode> builder)
    {
        builder.ToTable("moderation_publication_modes", table =>
            table.HasCheckConstraint("ck_moderation_publication_mode", "mode BETWEEN 1 AND 2"));
        builder.HasKey(row => new { row.PublicationId, row.Mode });
        Immutable(builder);
    }

    public void Configure(EntityTypeBuilder<ModerationPolicyPublicationProfile> builder)
    {
        builder.ToTable("moderation_publication_profiles", table =>
        {
            table.HasCheckConstraint("ck_moderation_profile_revision", "rule_revision > 0 AND reason_revision > 0 AND reference_revision > 0");
            table.HasCheckConstraint("ck_moderation_profile_quorum", "required_approvals BETWEEN 0 AND 2");
            table.HasCheckConstraint("ck_moderation_profile_timing", "duration_ticks >= 0 AND timing_kind BETWEEN 1 AND 9");
            table.HasCheckConstraint("ck_moderation_profile_purpose",
                "(bundle = 10 AND protection_purpose BETWEEN 1 AND 2) OR (bundle BETWEEN 1 AND 9 AND protection_purpose IS NULL)");
        });
        builder.HasKey(row => new { row.PublicationId, row.ProfileId });
        builder.HasIndex(row => new { row.PublicationId, row.RuleCode, row.RuleRevision }).IsUnique();
        builder.Property(row => row.RuleCode).HasMaxLength(100);
        builder.Property(row => row.ReasonCode).HasMaxLength(100);
        builder.HasOne<ModerationPolicyPublicationReason>().WithMany()
            .HasForeignKey(row => new { row.PublicationId, row.ReasonCode, row.ReasonRevision }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Scopes).WithOne().HasForeignKey(row => new { row.PublicationId, row.ProfileId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Subjects).WithOne().HasForeignKey(row => new { row.PublicationId, row.ProfileId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Effects).WithOne().HasForeignKey(row => new { row.PublicationId, row.ProfileId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Plans).WithOne().HasForeignKey(row => new { row.PublicationId, row.ProfileId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Consumers).WithOne().HasForeignKey(row => new { row.PublicationId, row.ProfileId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Prerequisites).WithOne().HasForeignKey(row => new { row.PublicationId, row.ProfileId }).OnDelete(DeleteBehavior.Restrict);
        Immutable(builder);
    }

    public void Configure(EntityTypeBuilder<ModerationPolicyPublicationScope> builder)
    {
        builder.ToTable("moderation_publication_scopes");
        builder.HasKey(row => new { row.PublicationId, row.ProfileId, row.Kind });
        Immutable(builder);
    }

    public void Configure(EntityTypeBuilder<ModerationPolicyPublicationSubject> builder)
    {
        builder.ToTable("moderation_publication_subjects");
        builder.HasKey(row => new { row.PublicationId, row.ProfileId, row.Kind });
        Immutable(builder);
    }

    public void Configure(EntityTypeBuilder<ModerationPolicyPublicationEffect> builder)
    {
        builder.ToTable("moderation_publication_effects");
        builder.HasKey(row => new { row.PublicationId, row.ProfileId, row.Ordinal });
        builder.Ignore(row => row.Meaning);
        Immutable(builder);
    }

    public void Configure(EntityTypeBuilder<ModerationPolicyPublicationPlan> builder)
    {
        builder.ToTable("moderation_publication_plans", table =>
            table.HasCheckConstraint("ck_moderation_publication_strategy", "strategy BETWEEN 0 AND 3"));
        builder.HasKey(row => new { row.PublicationId, row.ProfileId, row.Strategy });
        builder.Ignore(row => row.Meaning);
        Immutable(builder);
    }

    public void Configure(EntityTypeBuilder<ModerationPolicyPublicationConsumer> builder)
    {
        builder.ToTable("moderation_publication_consumers");
        builder.HasKey(row => new { row.PublicationId, row.ProfileId, row.Mode, row.Capability, row.ConsumerId });
        builder.Property(row => row.ConsumerId).HasMaxLength(100);
        foreach (var property in builder.Metadata.GetProperties().Where(property => property.ClrType == typeof(string)))
            property.SetMaxLength(property.Name == nameof(ModerationPolicyPublicationConsumer.ConsumerId) ? 100 : 512);
        Immutable(builder);
    }

    public void Configure(EntityTypeBuilder<ModerationPolicyPublicationPrerequisite> builder)
    {
        builder.ToTable("moderation_publication_prerequisites", table =>
            table.HasCheckConstraint("ck_moderation_publication_prerequisite",
                "(activation_gate BETWEEN 1 AND 5 AND native_prerequisite IS NULL) OR " +
                "(activation_gate IS NULL AND native_prerequisite BETWEEN 1 AND 30)"));
        builder.HasKey(row => new { row.PublicationId, row.ProfileId, row.Mode, row.Ordinal });
        Immutable(builder);
    }

    public void Configure(EntityTypeBuilder<ModerationPolicyPublicationEvidence> builder)
    {
        builder.ToTable("moderation_publication_evidence", table =>
            table.HasCheckConstraint("ck_moderation_publication_evidence",
                "source_revision > 0 AND ((approval_id IS NULL AND approval_revision IS NULL) OR " +
                "(approval_id IS NOT NULL AND approval_revision > 0))"));
        builder.HasKey(row => new { row.PublicationId, row.RequirementDigest });
        builder.Property(row => row.RequirementDigest).HasMaxLength(64);
        builder.Property(row => row.WriterGeneration).HasMaxLength(256);
        Immutable(builder);
    }

    private static void Immutable<T>(EntityTypeBuilder<T> builder) where T : class
    {
        builder.HasQueryFilter("ModerationAuthority", row => false);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
