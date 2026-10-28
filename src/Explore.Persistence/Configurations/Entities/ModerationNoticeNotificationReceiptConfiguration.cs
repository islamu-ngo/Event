using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ModerationNoticeNotificationReceiptConfiguration
    : IEntityTypeConfiguration<ModerationNoticeNotificationReceipt>
{
    public void Configure(EntityTypeBuilder<ModerationNoticeNotificationReceipt> builder)
    {
        builder.ToTable("moderation_notice_notification_receipts", table =>
        {
            table.HasCheckConstraint("ck_moderation_notice_notification_time",
                "remedy_until >= notified_at AND retain_until >= remedy_until AND retain_until > notified_at");
            table.HasCheckConstraint("ck_moderation_notice_notification_calendar",
                "contestation_calendar_months >= 0 AND contestation_calendar_months <= 120");
            table.HasCheckConstraint("ck_moderation_notice_notification_subject",
                "subject_kind IN (1, 4) AND (subject_kind <> 1 OR acknowledged_by_human_id = subject_id)");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.SnapshotHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(row => row.NoticeId).IsUnique();
        builder.HasIndex(row => row.EffectId).IsUnique();
        builder.HasQueryFilter("ModerationAuthority", row => false);
        builder.HasOne<ModerationNotice>().WithOne().HasForeignKey<ModerationNoticeNotificationReceipt>(row => row.NoticeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationNoticeSource>().WithOne().HasForeignKey<ModerationNoticeNotificationReceipt>(row => row.EffectId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
