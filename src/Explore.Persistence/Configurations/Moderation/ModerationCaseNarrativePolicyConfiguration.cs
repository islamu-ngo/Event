using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationCaseNarrativePolicyConfiguration :
    IEntityTypeConfiguration<ModerationCaseNarrativePolicySource>, IEntityTypeConfiguration<ModerationCaseNarrativeAdoption>
{
    public void Configure(EntityTypeBuilder<ModerationCaseNarrativePolicySource> builder)
    {
        builder.ToTable("moderation_case_narrative_policy_sources");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Property(row => row.IdentityAndIndependenceBasis).HasMaxLength(2048).IsRequired();
        builder.Property(row => row.PrivacyLegalMandateBasis).HasMaxLength(2048).IsRequired();
        builder.Ignore(row => row.Terms);
        builder.Ignore(row => row.Ownership);
        builder.HasAlternateKey(row => new { row.Id, row.HostBindingId, row.TrustDomainId, row.TenantId });
        builder.HasIndex(row => new { row.HostBindingId, row.TenantId, row.TargetKind, row.Purpose, row.WriterGeneration }).IsUnique();
        builder.HasOne<ModerationHostBinding>().WithMany().HasForeignKey(row => row.HostBindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.RecordedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.PrivacyOwnerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.PrivacyControllerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.GovernanceControllerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_case_narrative_policy_generation", "revision = 1 AND writer_generation > 0");
            table.HasCheckConstraint("ck_case_narrative_policy_purpose",
                "purpose IN (1, 3) OR (purpose = 2 AND target_kind = 2)");
            table.HasCheckConstraint("ck_case_narrative_policy_limits",
                "maximum_retention_ticks > 0 AND maximum_retention_ticks < 9223372036854775807 " +
                "AND maximum_material_bytes > 0 AND maximum_material_bytes <= 65536 AND valid_until > recorded_at");
            table.HasCheckConstraint("ck_case_narrative_policy_independence",
                "privacy_owner_human_id <> recorded_by_human_id AND privacy_controller_human_id <> governance_controller_human_id " +
                "AND privacy_controller_human_id <> recorded_by_human_id AND privacy_owner_human_id <> governance_controller_human_id");
        });
    }

    public void Configure(EntityTypeBuilder<ModerationCaseNarrativeAdoption> builder)
    {
        builder.ToTable("moderation_case_narrative_adoptions");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.HasIndex(row => row.SourceId).IsUnique();
        builder.HasAlternateKey(row => new { row.Id, row.HostBindingId, row.TrustDomainId, row.TenantId, row.SourceId });
        builder.HasOne<ModerationCaseNarrativePolicySource>().WithMany()
            .HasForeignKey(row => new { row.SourceId, row.HostBindingId, row.TrustDomainId, row.TenantId })
            .HasPrincipalKey(row => new { row.Id, row.HostBindingId, row.TrustDomainId, row.TenantId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.AdoptedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.WithdrawnByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_case_narrative_adoption_generation",
                "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
            table.HasCheckConstraint("ck_case_narrative_adoption_validity", "valid_until > adopted_at");
            table.HasCheckConstraint("ck_case_narrative_adoption_withdrawal",
                "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR " +
                "(revision = 2 AND withdrawn_at >= adopted_at AND withdrawn_by_human_id IS NOT NULL)");
        });
    }
}
