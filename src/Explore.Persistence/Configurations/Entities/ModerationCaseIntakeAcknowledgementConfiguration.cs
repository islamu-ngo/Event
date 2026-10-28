using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ModerationCaseIntakeAcknowledgementConfiguration : IEntityTypeConfiguration<ModerationCaseIntakeAcknowledgement>
{
    public void Configure(EntityTypeBuilder<ModerationCaseIntakeAcknowledgement> builder)
    {
        builder.ToTable("moderation_case_intake_acknowledgements", table =>
            table.HasCheckConstraint("ck_moderation_case_acknowledgement_revisions",
                "case_revision > 0 AND receipt_revision = 2 AND evidence_case_revision > 0 AND evidence_case_revision <= case_revision " +
                "AND budget_source_writer_generation > 0 AND budget_adoption_writer_generation > budget_source_writer_generation " +
                "AND budget_adoption_writer_generation < 9223372036854775807 AND budget_source_id <> budget_adoption_id"));
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.RequestDigest).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.HasIndex(entity => new { entity.TenantId, entity.CallerUserId, entity.OperationId }).IsUnique();
        builder.HasIndex(entity => entity.ReceiptId).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(entity => entity.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCase>().WithMany().HasForeignKey(entity => entity.CaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCaseTargetReceipt>().WithMany()
            .HasForeignKey(entity => entity.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationOperationalBudgetAdoption>().WithMany()
            .HasForeignKey(entity => new { entity.BudgetAdoptionId, entity.TrustDomainId, entity.TenantId, entity.BudgetSourceId })
            .HasPrincipalKey(entity => new { entity.Id, entity.TrustDomainId, entity.TenantId, entity.SourceId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
