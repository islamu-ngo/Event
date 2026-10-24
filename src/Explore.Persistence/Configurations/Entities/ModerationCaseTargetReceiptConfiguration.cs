using Explore.Domain;
using Explore.Persistence.Configurations.Moderation;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ModerationCaseTargetReceiptConfiguration : IEntityTypeConfiguration<ModerationCaseTargetReceipt>
{
    public void Configure(EntityTypeBuilder<ModerationCaseTargetReceipt> builder)
    {
        builder.ToTable("moderation_case_target_receipts");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.Revision).IsConcurrencyToken();
        builder.Property(entity => entity.TokenDigest).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(entity => entity.RelationshipFactsDigest).HasMaxLength(64).IsUnicode(false);
        builder.Ignore(entity => entity.Evidence);
        builder.Ignore(entity => entity.RelationshipGeneration);
        builder.HasIndex(entity => entity.TokenDigest).IsUnique();
        builder.HasIndex(entity => new { entity.TenantId, entity.CallerUserId, entity.ExpiresAt });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(entity => entity.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationHostBinding>().WithMany()
            .HasForeignKey(entity => entity.HostBindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationQualificationEpoch>().WithMany()
            .HasForeignKey(entity => entity.QualificationEpochId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCase>().WithMany()
            .HasForeignKey(entity => entity.ConsumedCaseId).OnDelete(DeleteBehavior.Restrict);
        ModerationLedgerConfiguration.ConfigureScope(builder, "target_origin");
        ModerationLedgerConfiguration.ConfigureScope(builder, "source_origin");
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_moderation_intake_receipt_validity", "expires_at > issued_at");
            table.HasCheckConstraint("ck_moderation_intake_receipt_generations",
                "qualification_revision > 0 AND lifetime_policy_revision > 0 AND " +
                "((relationship_kind IN (2, 5) AND relationship_revision IS NOT NULL AND relationship_revision > 0 " +
                "AND relationship_concurrency_stamp IS NULL AND relationship_facts_digest IS NULL) OR " +
                "(relationship_kind IN (1, 3, 4, 6) AND relationship_revision IS NULL " +
                "AND relationship_concurrency_stamp IS NOT NULL AND relationship_facts_digest IS NULL) OR " +
                "(relationship_kind = 7 AND target_kind IN (3, 4) AND relationship_revision IS NULL " +
                "AND relationship_concurrency_stamp IS NULL AND relationship_facts_digest IS NOT NULL " +
                "AND length(relationship_facts_digest) = 64))");
            table.HasCheckConstraint("ck_moderation_intake_receipt_origin",
                "target_origin_trust_domain_id = source_origin_trust_domain_id " +
                "AND target_origin_solution_id = source_origin_solution_id " +
                "AND target_origin_instance_id = source_origin_instance_id " +
                "AND source_origin_kind IN (3, 4) " +
                "AND (source_origin_tenant_id IS NULL OR source_origin_tenant_id = tenant_id) " +
                "AND ((target_kind IN (3, 4) AND target_origin_kind = 3) " +
                "OR (target_kind IN (1, 2, 5, 6, 7, 8, 9) AND target_origin_kind = 4 AND target_origin_tenant_id = tenant_id))");
            table.HasCheckConstraint("ck_moderation_intake_receipt_consumption",
                "(revision = 1 AND consumed_case_id IS NULL AND consumed_operation_id IS NULL AND consumed_at IS NULL) " +
                "OR (relationship_kind <> 5 AND revision = 2 AND consumed_case_id IS NOT NULL AND consumed_operation_id IS NOT NULL " +
                "AND consumed_at IS NOT NULL AND consumed_at >= issued_at AND consumed_at < expires_at)");
        });
    }
}
