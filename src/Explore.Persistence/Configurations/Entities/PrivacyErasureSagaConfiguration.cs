using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class PrivacyErasureSagaConfiguration : IEntityTypeConfiguration<PrivacyErasureSaga>
{
    public void Configure(EntityTypeBuilder<PrivacyErasureSaga> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_privacy_erasure_sagas_subject_kind", "subject_kind = 1");
            table.HasCheckConstraint(
                "ck_privacy_erasure_sagas_subject_nonempty",
                "subject_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_privacy_erasure_sagas_policy_version", "policy_version > 0");
            table.HasCheckConstraint("ck_privacy_erasure_sagas_fence", "fence_token > 0");
            table.HasCheckConstraint("ck_privacy_erasure_sagas_status", "status IN (1, 2, 3)");
            table.HasCheckConstraint(
                "ck_privacy_erasure_sagas_provider_counts",
                "provider_work_count >= 0 AND completed_provider_work_count >= 0 AND completed_provider_work_count <= provider_work_count");
            table.HasCheckConstraint(
                "ck_privacy_erasure_sagas_receipt_hash",
                "receipt_hash IS NULL OR octet_length(receipt_hash) = 32");
            table.HasCheckConstraint(
                "ck_privacy_erasure_sagas_receipt_window",
                "receipt_expires_at_utc > fenced_at_utc");
            table.HasCheckConstraint(
                "ck_privacy_erasure_sagas_concurrency_uuid_v7",
                "substring(concurrency_token::text, 15, 1) = '7' AND " +
                "substring(concurrency_token::text, 20, 1) IN ('8', '9', 'a', 'b')");
        });

        builder.HasKey(item => item.IntentId);
        builder.Property(item => item.SubjectKind).HasConversion<short>();
        builder.Property(item => item.ReceiptHash).HasMaxLength(32).IsFixedLength().IsRequired(false);
        builder.Property(item => item.Status).HasConversion<short>();
        builder.Property(item => item.ConcurrencyToken).IsConcurrencyToken();
        builder.HasIndex(item => item.ReceiptHash).IsUnique();
        builder.HasIndex(item => new { item.SubjectKind, item.SubjectId }).IsUnique();
        builder.HasIndex(item => new { item.IntentId, item.SubjectKind, item.PolicyVersion })
            .IsUnique();
    }
}
