using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ModerationOriginalEffectCompletionReceiptConfiguration
    : IEntityTypeConfiguration<ModerationOriginalEffectCompletionReceipt>
{
    public void Configure(EntityTypeBuilder<ModerationOriginalEffectCompletionReceipt> builder)
    {
        builder.ToTable("moderation_original_effect_completion_receipts");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.HasIndex(row => row.EffectId).IsUnique();
        builder.Property(row => row.SnapshotHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.EffectBindingHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.SelectionDigest).HasMaxLength(64).IsRequired();
        builder.Property(row => row.ProofDigest).HasMaxLength(64).IsRequired();
        // No independent retention exception: original cleanup removes the proof in its own transaction.
        builder.HasOne<ModerationNoticeSource>().WithMany().HasForeignKey(row => row.EffectId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_original_completion_count", "original_cause_count >= 1 AND original_cause_count <= 64");
            table.HasCheckConstraint("ck_original_completion_revision", "lease_revision > 0");
            table.HasCheckConstraint("ck_original_completion_window",
                "completed_at >= lease_started_at AND completed_at < qualified_until AND qualified_until <= lease_expires_at AND qualified_until <= retain_until");
        });
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
