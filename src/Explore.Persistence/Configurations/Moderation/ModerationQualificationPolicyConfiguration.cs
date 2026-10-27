using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationQualificationPolicyConfiguration :
    IEntityTypeConfiguration<ModerationQualificationPolicySource>,
    IEntityTypeConfiguration<ModerationQualificationPolicyAdoption>
{
    public void Configure(EntityTypeBuilder<ModerationQualificationPolicySource> builder)
    {
        builder.ToTable("moderation_qualification_policy_sources");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Ignore(row => row.MaximumReviewerLifetime);
        builder.Ignore(row => row.MaximumUrgencyLifetime);
        builder.HasIndex(row => new { row.HostBindingId, row.TrustDomainId, row.TenantId, row.CaseId }).IsUnique();
        builder.HasAlternateKey(row => new { row.Id, row.HostBindingId, row.TrustDomainId, row.TenantId, row.CaseId });
        builder.HasOne<ModerationHostBinding>().WithMany().HasForeignKey(row => row.HostBindingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCase>().WithMany().HasForeignKey(row => row.CaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.PublishedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_moderation_qualification_policy_source_generation",
                "revision > 0 AND case_revision > 0 AND writer_generation > 0");
            table.HasCheckConstraint("ck_moderation_qualification_policy_source_limits",
                "maximum_reviewer_lifetime_ticks > 0 AND maximum_reviewer_lifetime_ticks < 9223372036854775807 " +
                "AND maximum_urgency_lifetime_ticks > 0 AND maximum_urgency_lifetime_ticks < 9223372036854775807");
            table.HasCheckConstraint("ck_moderation_qualification_policy_source_validity", "valid_until > published_at");
        });
    }

    public void Configure(EntityTypeBuilder<ModerationQualificationPolicyAdoption> builder)
    {
        builder.ToTable("moderation_qualification_policy_adoptions");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Ignore(row => row.MaximumReviewerLifetime);
        builder.Ignore(row => row.MaximumUrgencyLifetime);
        builder.HasIndex(row => new { row.SourceId, row.SourceRevision }).IsUnique();
        // Retained approvals reference stable source coordinates, not mutable current revision/limits.
        builder.HasOne<ModerationQualificationPolicySource>().WithMany()
            .HasForeignKey(row => new { row.SourceId, row.HostBindingId, row.TrustDomainId, row.TenantId, row.CaseId })
            .HasPrincipalKey(row => new { row.Id, row.HostBindingId, row.TrustDomainId, row.TenantId, row.CaseId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.AdoptedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.RevokedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_moderation_qualification_policy_adoption_generation",
                "id <> source_id AND source_revision > 0 AND case_revision > 0 AND writer_generation > 0");
            table.HasCheckConstraint("ck_moderation_qualification_policy_adoption_limits",
                "maximum_reviewer_lifetime_ticks > 0 AND maximum_reviewer_lifetime_ticks < 9223372036854775807 " +
                "AND maximum_urgency_lifetime_ticks > 0 AND maximum_urgency_lifetime_ticks < 9223372036854775807");
            table.HasCheckConstraint("ck_moderation_qualification_policy_adoption_validity", "valid_until > adopted_at");
            table.HasCheckConstraint("ck_moderation_qualification_policy_adoption_revocation",
                "(revision = 1 AND revoked_at IS NULL AND revoked_by_human_id IS NULL) " +
                "OR (revision = 2 AND revoked_at IS NOT NULL AND revoked_by_human_id IS NOT NULL AND revoked_at >= adopted_at)");
        });
    }
}
