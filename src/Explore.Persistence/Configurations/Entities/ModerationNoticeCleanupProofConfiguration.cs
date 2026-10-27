using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

/// <summary>Minimal cleanup coordinates on the existing effect ledger; no separate retention lifetime.</summary>
public sealed class ModerationNoticeCleanupProofConfiguration : IEntityTypeConfiguration<ModerationEffectIntent>
{
    public void Configure(EntityTypeBuilder<ModerationEffectIntent> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_moderation_effect_notice_cleanup",
                "(original_notice_cleanup_id IS NULL AND original_notice_host_binding_id IS NULL " +
                "AND original_notice_writer_tenant_id IS NULL AND original_notice_retain_until IS NULL " +
                "AND original_notice_purged_at IS NULL) OR " +
                "(original_notice_cleanup_id IS NOT NULL AND original_notice_host_binding_id IS NOT NULL " +
                "AND original_notice_writer_tenant_id IS NOT NULL AND original_notice_retain_until IS NOT NULL " +
                "AND original_notice_purged_at IS NOT NULL AND original_notice_retain_until > created_at " +
                "AND original_notice_purged_at >= original_notice_retain_until AND revision > 1)");
        });
    }
}
