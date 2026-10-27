using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ModerationCaseAttributionSourceConfiguration : IEntityTypeConfiguration<ModerationCaseAttributionSource>
{
    public void Configure(EntityTypeBuilder<ModerationCaseAttributionSource> builder)
    {
        builder.ToTable("moderation_case_attribution_sources");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        // Scalar history survives physical assessment deletion; native absence must fail closed.
        builder.Property(row => row.InvestigationSourceId).IsRequired(false);
        builder.Property(row => row.InvestigationSourceRevision).IsRequired(false);
        builder.Property(row => row.NativeResponsibilityDigest).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(row => row.QualificationFactsDigest).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.HasIndex(row => new { row.CaseId, row.Generation }).IsUnique();
        builder.HasIndex(row => new
        {
            row.TenantId,
            row.TrustDomainId,
            row.OccurrenceCaseId,
            row.SubjectKind,
            row.SubjectId,
            row.Generation
        }).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCase>().WithMany().HasForeignKey(row => row.OccurrenceCaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCase>().WithMany().HasForeignKey(row => row.CaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCaseTargetSource>().WithMany().HasForeignKey(row => row.EvidenceSourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCaseReadGrant>().WithMany().HasForeignKey(row => row.ReadGrantId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_moderation_attribution_evidence",
                "generation > 0 AND case_id <> occurrence_case_id AND evidence_case_revision > 0 " +
                "AND occurrence_case_revision >= evidence_case_revision AND qualification_revision > 0 " +
                "AND read_grant_revision > 0 AND lifetime_policy_revision > 0");
            table.HasCheckConstraint("ck_moderation_attribution_basis",
                "(investigation_source_id IS NULL AND investigation_source_revision IS NULL AND (" +
                "(basis = 1 AND subject_kind = 1 AND subject_id <> qualified_by_human_id) OR " +
                "(basis = 2 AND subject_kind = 4) OR (basis = 3 AND subject_kind = 3) OR (basis = 4 AND subject_kind = 2))) OR " +
                "(basis = 5 AND subject_kind = 1 AND subject_id <> qualified_by_human_id " +
                "AND investigation_source_id IS NOT NULL AND investigation_source_revision IS NOT NULL " +
                "AND investigation_source_revision = 1)");
            table.HasCheckConstraint("ck_moderation_attribution_validity",
                "valid_until > qualified_at AND ((revision = 1 AND withdrawn_at IS NULL " +
                "AND withdrawn_by_human_id IS NULL AND withdrawal_operation_id IS NULL) OR " +
                "(revision = 2 AND withdrawn_at >= qualified_at AND withdrawn_by_human_id IS NOT NULL " +
                "AND withdrawal_operation_id IS NOT NULL))");
        });
    }
}
