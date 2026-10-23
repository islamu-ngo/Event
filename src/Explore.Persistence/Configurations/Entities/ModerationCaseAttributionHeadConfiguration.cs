using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ModerationCaseAttributionHeadConfiguration : IEntityTypeConfiguration<ModerationCaseAttributionHead>
{
    public void Configure(EntityTypeBuilder<ModerationCaseAttributionHead> builder)
    {
        builder.ToTable("moderation_case_attribution_heads");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.HasIndex(row => row.CaseId).IsUnique();
        builder.HasIndex(row => new
        {
            row.TenantId,
            row.TrustDomainId,
            row.OccurrenceCaseId,
            row.SubjectKind,
            row.SubjectId
        }).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationHostBinding>().WithMany().HasForeignKey(row => row.HostBindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCase>().WithMany().HasForeignKey(row => row.CaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCase>().WithMany().HasForeignKey(row => row.OccurrenceCaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCaseAttributionSource>().WithMany().HasForeignKey(row => row.OpeningSourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCaseAttributionSource>().WithMany().HasForeignKey(row => row.SourceId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_moderation_attribution_head_scope",
                "case_id <> occurrence_case_id AND occurrence_evidence_revision > 0 AND subject_kind IN (1, 2, 3, 4)");
            table.HasCheckConstraint("ck_moderation_attribution_head_generation",
                "generation > 0 AND revision = generation AND " +
                "((generation = 1 AND source_id = opening_source_id) OR " +
                "(generation > 1 AND source_id <> opening_source_id))");
        });
    }
}
