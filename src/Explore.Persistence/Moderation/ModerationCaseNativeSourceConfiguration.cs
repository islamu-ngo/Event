using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Moderation;

public sealed class ModerationCaseNativeSourceConfiguration :
    IEntityTypeConfiguration<ModerationCaseTargetSource>,
    IEntityTypeConfiguration<ModerationCaseTargetPolicy>,
    IEntityTypeConfiguration<ModerationCaseReadGrant>
{
    public void Configure(EntityTypeBuilder<ModerationCaseTargetSource> builder)
    {
        builder.ToTable("moderation_case_target_sources");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Version).IsConcurrencyToken();
        builder.Property(row => row.OccurrenceDigest).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(row => row.MaterialDigest).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(row => row.NativeFactsDigest).HasMaxLength(64).IsUnicode(false).IsRequired();
        // Every indexed coordinate is nonnullable. The exact native fingerprint binds
        // actual Event identity/version when present, so NULL Event ancestry never
        // exempts standalone Actor/Service material from database uniqueness.
        builder.HasIndex(row => new
        {
            row.TenantId,
            row.TrustDomainId,
            row.Kind,
            row.NativeKind,
            row.NativeTargetId,
            row.NativeTargetVersion,
            row.NativeFactsDigest,
            row.OccurrenceDigest,
            row.MaterialDigest
        }).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Event>().WithMany().HasForeignKey(row => row.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_moderation_native_source_kind",
                "kind BETWEEN 1 AND 9 AND native_kind BETWEEN 1 AND 7 " +
                "AND (kind IN (8, 9) OR kind = native_kind) AND (kind <> 9 OR native_kind = 1)");
            table.HasCheckConstraint("ck_moderation_native_source_event",
                "(event_id IS NULL AND event_version IS NULL AND kind IN (3, 4)) " +
                "OR (event_id IS NOT NULL AND event_version IS NOT NULL)");
        });
    }

    public void Configure(EntityTypeBuilder<ModerationCaseTargetPolicy> builder)
    {
        builder.ToTable("moderation_case_target_policies");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Ignore(row => row.SubmissionLifetime);
        builder.Ignore(row => row.ReadLifetime);
        builder.HasIndex(row => new { row.TenantId, row.TrustDomainId, row.TargetKind }).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table => table.HasCheckConstraint("ck_moderation_target_policy_lifetime",
            "revision > 0 AND target_kind BETWEEN 1 AND 9 " +
            "AND submission_lifetime_ticks > 0 AND submission_lifetime_ticks <= 864000000000 " +
            "AND read_lifetime_ticks > 0 AND read_lifetime_ticks <= 864000000000"));
    }

    public void Configure(EntityTypeBuilder<ModerationCaseReadGrant> builder)
    {
        builder.ToTable("moderation_case_read_grants");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCase>().WithMany().HasForeignKey(row => row.CaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCaseTargetSource>().WithMany()
            .HasForeignKey(row => row.SourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => new { row.TenantId, row.CaseId, row.CallerUserId });
        builder.ToTable(table => table.HasCheckConstraint("ck_moderation_case_read_grant_validity",
            "expires_at > issued_at AND revision > 0 AND case_revision > 0 AND (revoked_at IS NULL OR revoked_at >= issued_at)"));
    }
}
