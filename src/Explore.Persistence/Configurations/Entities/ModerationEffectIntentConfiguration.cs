using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ModerationEffectIntentConfiguration :
    IEntityTypeConfiguration<ModerationEffectIntent>, IEntityTypeConfiguration<ModerationOperationReceipt>
{
    public void Configure(EntityTypeBuilder<ModerationEffectIntent> builder)
    {
        builder.ToTable("moderation_effect_intents", table =>
        {
            table.HasCheckConstraint("ck_moderation_effect_revision", "revision > 0 AND decision_revision > 0");
            table.HasCheckConstraint("ck_moderation_effect_qualification", "policy_revision > 0 AND qualification_revision > 0");
            table.HasCheckConstraint("ck_moderation_effect_sanction_pair",
                "(sanction_id IS NULL AND sanction_revision IS NULL) OR " +
                "(sanction_id IS NOT NULL AND sanction_revision IS NOT NULL AND sanction_revision > 0)");
            table.HasCheckConstraint("ck_moderation_effect_supersession",
                "(operation_kind <> 10 AND superseded_sanction_id IS NULL AND superseded_sanction_revision IS NULL) OR " +
                "(operation_kind = 10 AND sanction_id IS NOT NULL AND superseded_sanction_id IS NOT NULL " +
                "AND superseded_sanction_revision IS NOT NULL AND superseded_sanction_revision > 0 " +
                "AND superseded_sanction_id <> sanction_id)");
        });
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.Revision).IsConcurrencyToken();
        builder.HasIndex(entity => entity.OperationId).IsUnique();
        builder.HasIndex(entity => new { entity.CompletedAt, entity.CreatedAt });
        builder.HasOne<ModerationOperationReceipt>().WithMany().HasForeignKey(entity => entity.OperationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationDecision>().WithMany().HasForeignKey(entity => entity.DecisionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationSanction>().WithMany().HasForeignKey(entity => entity.SupersededSanctionId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<ModerationOperationReceipt> builder)
    {
        builder.ToTable("moderation_operation_receipts", table =>
        {
            table.HasCheckConstraint("ck_moderation_receipt_revision", "result_revision > 0 AND policy_revision > 0 AND qualification_revision > 0");
            table.HasCheckConstraint("ck_moderation_receipt_scope", "scope_kind BETWEEN 1 AND 6 AND subject_kind BETWEEN 1 AND 4");
        });
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.RequestDigest).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.PrincipalDigest).HasMaxLength(64).IsRequired();
        builder.HasOne<ModerationDecision>().WithMany().HasForeignKey(entity => entity.DecisionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(entity => new { entity.TrustDomainId, entity.ScopeKind, entity.ScopeId, entity.SubjectKind, entity.SubjectId });
    }
}
