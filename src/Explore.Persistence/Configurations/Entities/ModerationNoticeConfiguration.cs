using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ModerationNoticeConfiguration :
    IEntityTypeConfiguration<ModerationNoticeSource>, IEntityTypeConfiguration<ModerationNotice>
{
    public void Configure(EntityTypeBuilder<ModerationNoticeSource> builder)
    {
        builder.ToTable("moderation_notice_sources", table =>
        {
            table.HasCheckConstraint("ck_moderation_notice_source_revision", "decision_revision > 0");
            table.HasCheckConstraint("ck_moderation_notice_source_subject", "subject_kind BETWEEN 1 AND 4");
            table.HasCheckConstraint("ck_moderation_notice_source_scope", "scope_kind BETWEEN 1 AND 7");
            table.HasCheckConstraint("ck_moderation_notice_source_retention",
                "retain_until IS NULL OR retain_until > captured_at");
            table.HasCheckConstraint("ck_moderation_notice_source_status",
                "(status_metadata_json IS NULL AND status_metadata_hash IS NULL) OR " +
                "(status_metadata_json IS NOT NULL AND status_metadata_hash IS NOT NULL AND retain_until IS NOT NULL AND subject_kind IN (1, 4))");
        });
        builder.HasKey(row => row.EffectId);
        builder.HasQueryFilter("ModerationAuthority", row => false);
        builder.Property(row => row.EffectId).ValueGeneratedNever();
        builder.Property(row => row.SnapshotJson).IsRequired();
        builder.Property(row => row.SnapshotHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.EffectBindingHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.StatusMetadataJson).HasMaxLength(8192);
        builder.Property(row => row.StatusMetadataHash).HasMaxLength(64);
        builder.HasOne<ModerationEffectIntent>().WithOne().HasForeignKey<ModerationNoticeSource>(row => row.EffectId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => new { row.TrustDomainId, row.SubjectKind, row.SubjectId, row.CapturedAt });
        builder.HasIndex(row => new { row.TrustDomainId, row.RetainUntil, row.EffectId });
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }

    public void Configure(EntityTypeBuilder<ModerationNotice> builder)
    {
        builder.ToTable("moderation_notices", table =>
        {
            table.HasCheckConstraint("ck_moderation_notice_recipient",
                "recipient_subject_kind BETWEEN 1 AND 4 AND audience BETWEEN 1 AND 3 " +
                "AND (audience <> 3 OR recipient_subject_kind = 1)");
            table.HasCheckConstraint("ck_moderation_notice_native",
                "(native_intent_id IS NULL AND delivery_disposition BETWEEN 1 AND 4) OR " +
                "(native_intent_id IS NOT NULL AND delivery_disposition IN (5, 6) AND tenant_id IS NOT NULL " +
                "AND recipient_subject_kind = 1)");
        });
        builder.HasKey(row => row.Id);
        builder.HasQueryFilter("ModerationAuthority", row => false);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.HasOne<ModerationNoticeSource>().WithMany().HasForeignKey(row => row.EffectId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NotificationIntent>().WithMany().HasForeignKey(row => row.NativeIntentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => new { row.EffectId, row.Audience, row.RecipientSubjectKind, row.RecipientSubjectId })
            .IsUnique();
        builder.HasIndex(row => new { row.TrustDomainId, row.RecipientSubjectKind, row.RecipientSubjectId, row.CreatedAt });
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
