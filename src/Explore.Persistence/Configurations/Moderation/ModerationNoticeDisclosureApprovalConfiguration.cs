using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationNoticeDisclosureApprovalConfiguration : IEntityTypeConfiguration<ModerationNoticeDisclosureApproval>
{
    public void Configure(EntityTypeBuilder<ModerationNoticeDisclosureApproval> builder)
    {
        builder.ToTable("moderation_notice_disclosure_approvals");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Ignore(row => row.Intent);
        builder.Property(row => row.SelectionDigest).HasMaxLength(64).IsRequired();
        builder.Property(row => row.FactsJson).HasMaxLength(128 * 1024).IsRequired();
        builder.Property(row => row.ContentRepresentativesJson).HasMaxLength(8192).IsRequired();
        builder.Property(row => row.RuleMeaning).HasMaxLength(4000).IsRequired();
        builder.Property(row => row.ReasonMeaning).HasMaxLength(4000).IsRequired();
        builder.Property(row => row.RemainingActions).HasMaxLength(4000).IsRequired();
        builder.Property(row => row.Recourse).HasMaxLength(4000).IsRequired();
        builder.Property(row => row.Automation).HasMaxLength(4000).IsRequired();
        builder.Property(row => row.Retention).HasMaxLength(4000).IsRequired();
        builder.HasIndex(row => new { row.HostBindingId, row.TrustDomainId, row.TenantId, row.EffectId }).IsUnique();
        builder.HasOne<ModerationHostBinding>().WithMany().HasForeignKey(row => row.HostBindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCase>().WithMany().HasForeignKey(row => row.CaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationDecision>().WithMany().HasForeignKey(row => row.DecisionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationPolicyPublication>().WithMany().HasForeignKey(row => row.PublicationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.ApprovedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.RevokedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_moderation_notice_disclosure_generations",
                "decision_revision > 1 AND case_revision > 0 AND policy_revision > 0 AND writer_generation > 0");
            table.HasCheckConstraint("ck_moderation_notice_disclosure_validity", "valid_until > approved_at");
            table.HasCheckConstraint("ck_moderation_notice_disclosure_retention", "retain_until > approved_at");
            table.HasCheckConstraint("ck_moderation_notice_disclosure_revocation",
                "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL) OR " +
                "(revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL AND revoked_at >= approved_at)");
        });
    }
}
