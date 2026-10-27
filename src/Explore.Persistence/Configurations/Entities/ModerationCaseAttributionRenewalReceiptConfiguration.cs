using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ModerationCaseAttributionRenewalReceiptConfiguration : IEntityTypeConfiguration<ModerationCaseAttributionRenewalReceipt>
{
    public void Configure(EntityTypeBuilder<ModerationCaseAttributionRenewalReceipt> builder)
    {
        builder.ToTable("moderation_case_attribution_renewal_receipts");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.HasIndex(row => new { row.HeadId, row.ExpectedGeneration }).IsUnique();
        builder.HasIndex(row => new { row.CaseId, row.ExpectedGeneration }).IsUnique();
        builder.HasOne<ModerationCaseAttributionSource>().WithMany().HasForeignKey(row => row.Id).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCase>().WithMany().HasForeignKey(row => row.CaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCaseAttributionHead>().WithMany().HasForeignKey(row => row.HeadId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCaseAttributionSource>().WithMany().HasForeignKey(row => row.ExpectedSourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_moderation_attribution_renewal_expected",
                "expected_case_revision > 0 AND expected_generation > 0 AND " +
                "expected_head_revision > 0 AND expected_head_revision = expected_generation");
            table.HasCheckConstraint("ck_moderation_attribution_renewal_distinct_source",
                "id <> expected_source_id");
        });
    }
}
