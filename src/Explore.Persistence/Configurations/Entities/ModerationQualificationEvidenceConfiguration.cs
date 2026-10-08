using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ModerationQualificationEvidenceConfiguration :
    IEntityTypeConfiguration<ModerationQualificationEvidence>,
    IEntityTypeConfiguration<ModerationQualificationMutationReceipt>
{
    public void Configure(EntityTypeBuilder<ModerationQualificationEvidence> builder)
    {
        builder.ToTable("moderation_qualification_evidence");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.Justification).HasMaxLength(4000).IsRequired();
        builder.HasOne<ModerationCase>().WithMany().HasForeignKey(entity => entity.CaseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(entity => new { entity.TrustDomainId, entity.TenantId, entity.CaseId, entity.Purpose });
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_moderation_qualification_evidence_revision", "revision = 1");
            table.HasCheckConstraint("ck_moderation_qualification_evidence_shape",
                "(purpose = 1 AND subject_human_id IS NOT NULL AND asserted_by_human_id = subject_human_id " +
                "AND reviewer_human_id IS NULL AND controller_human_id IS NULL AND conflict_state IS NULL AND valid_until IS NULL) " +
                "OR (purpose = 2 AND subject_human_id IS NULL AND reviewer_human_id IS NOT NULL " +
                "AND controller_human_id IS NOT NULL AND conflict_state IS NULL AND valid_until IS NOT NULL) " +
                "OR (purpose = 3 AND subject_human_id IS NULL AND reviewer_human_id IS NOT NULL " +
                "AND controller_human_id IS NOT NULL AND conflict_state IS NOT NULL AND conflict_state IN (2, 3) AND valid_until IS NOT NULL) " +
                "OR (purpose = 4 AND reviewer_human_id IS NULL AND controller_human_id IS NULL " +
                "AND conflict_state IS NULL AND valid_until IS NOT NULL)");
            table.HasCheckConstraint("ck_moderation_qualification_evidence_assessor",
                "purpose = 1 OR (valid_until > recorded_at " +
                "AND (reviewer_human_id IS NULL OR asserted_by_human_id <> reviewer_human_id) " +
                "AND (controller_human_id IS NULL OR asserted_by_human_id <> controller_human_id) " +
                "AND (subject_human_id IS NULL OR asserted_by_human_id <> subject_human_id))");
        });
    }

    public void Configure(EntityTypeBuilder<ModerationQualificationMutationReceipt> builder)
    {
        builder.ToTable("moderation_qualification_mutation_receipts");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.RequestDigest).HasMaxLength(64).IsRequired();
        builder.HasOne<ModerationHostBinding>().WithMany().HasForeignKey(entity => entity.HostBindingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(entity => entity.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(entity => new { entity.TrustDomainId, entity.TenantId, entity.SourceKind, entity.SourceId });
        builder.ToTable(table => table.HasCheckConstraint("ck_moderation_qualification_receipt_result",
            "source_kind BETWEEN 1 AND 5 AND result_revision > 0 AND qualification_revision > 0"));
    }
}
