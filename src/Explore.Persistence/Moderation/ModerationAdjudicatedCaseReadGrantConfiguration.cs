using Explore.Domain;
using Explore.Persistence.Schema;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Moderation;

public sealed class ModerationAdjudicatedCaseReadGrantConfiguration
    : IEntityTypeConfiguration<ModerationAdjudicatedCaseReadGrant>
{
    public void Configure(EntityTypeBuilder<ModerationAdjudicatedCaseReadGrant> builder)
    {
        builder.ToTable("moderation_adjudicated_case_read_grants", table =>
        {
            table.HasCheckConstraint("ck_adjudicated_read_grant_case_revision",
                "case_revision > 0 AND case_revision < 9223372036854775807");
            table.HasCheckConstraint("ck_adjudicated_read_grant_window",
                "issued_at > 0 AND expires_at > issued_at AND expires_at <= original_retain_until " +
                "AND original_retain_until < 3155378975999999999 " +
                "AND issued_at % 10 = 0 AND expires_at % 10 = 0");
            table.HasCheckConstraint("ck_adjudicated_read_grant_revocation",
                "(revision = 1 AND revoked_at IS NULL AND revocation_operation_id IS NULL " +
                "AND revoked_by_user_id IS NULL AND revoked_by_role_grant_id IS NULL AND revoked_by_platform_grant IS NULL) OR " +
                "(revision = 2 AND revoked_at IS NOT NULL AND revoked_at >= issued_at " +
                "AND revoked_at < 3155378975999999999 AND revoked_at % 10 = 0 " +
                "AND revocation_operation_id IS NOT NULL AND revocation_operation_id <> id " +
                "AND revocation_operation_id <> '00000000-0000-0000-0000-000000000000' " +
                "AND revoked_by_user_id IS NOT NULL AND revoked_by_user_id <> '00000000-0000-0000-0000-000000000000' " +
                "AND revoked_by_role_grant_id IS NOT NULL AND revoked_by_role_grant_id <> '00000000-0000-0000-0000-000000000000' " +
                "AND revoked_by_platform_grant IS NOT NULL)");
            table.HasCheckConstraint("ck_adjudicated_read_grant_hashes",
                "length(original_snapshot_hash) = 64 AND length(original_effect_binding_hash) = 64");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Property(row => row.OriginalSnapshotHash).HasMaxLength(64).IsUnicode(false)
            .IsRequired().UsePortableOrdinalAscii();
        builder.Property(row => row.OriginalEffectBindingHash).HasMaxLength(64).IsUnicode(false)
            .IsRequired().UsePortableOrdinalAscii();
        // UTC ticks keep finite bounds, precision and comparisons identical across providers.
        builder.Property(row => row.IssuedAt).HasConversion(
            instant => instant.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
        builder.Property(row => row.ExpiresAt).HasConversion(
            instant => instant.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
        builder.Property(row => row.OriginalRetainUntil).HasConversion(
            instant => instant.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
        builder.Property(row => row.RevokedAt).HasConversion(
            instant => instant.HasValue ? instant.Value.UtcTicks : (long?)null,
            ticks => ticks.HasValue ? new DateTimeOffset(ticks.Value, TimeSpan.Zero) : (DateTimeOffset?)null);
        builder.HasIndex(row => new { row.TrustDomainId, row.TenantId, row.CaseId, row.RecipientUserId });
        builder.HasOne<ModerationHostBinding>().WithMany().HasForeignKey(row => row.HostBindingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCase>().WithMany().HasForeignKey(row => row.CaseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationEffectIntent>().WithMany().HasForeignKey(row => row.OriginalEffectId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
