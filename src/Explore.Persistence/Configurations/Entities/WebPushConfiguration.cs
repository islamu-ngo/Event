using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class WebPushSubscriptionConfiguration : IEntityTypeConfiguration<WebPushSubscription>
{
    public void Configure(EntityTypeBuilder<WebPushSubscription> builder)
    {
        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.DeviceIdentifier).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Endpoint).HasMaxLength(2000).IsRequired();
        builder.Property(e => e.P256Dh).HasMaxLength(500).IsRequired();
        builder.Property(e => e.AuthSecret).HasMaxLength(500).IsRequired();
        builder.Property(e => e.DeactivationReason).HasMaxLength(100);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("NOW()").IsRequired();
        builder.Property(e => e.UpdatedAt).HasDefaultValueSql("NOW()");
        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.Endpoint)
            .IsUnique()
            .HasFilter("is_deleted = false AND is_active = true")
            .HasDatabaseName("ux_web_push_subscriptions_active_endpoint");

        builder.HasIndex(e => new { e.TenantId, e.UserId, e.DeviceIdentifier })
            .IsUnique()
            .HasFilter("is_deleted = false AND is_active = true")
            .HasDatabaseName("ux_web_push_subscriptions_active_user_device");

        builder.HasIndex(e => new { e.TenantId, e.UserId, e.IsActive })
            .HasDatabaseName("ix_web_push_subscriptions_tenant_user_active");
    }
}

public sealed class WebPushDispatchOutboxConfiguration : IEntityTypeConfiguration<WebPushDispatchOutbox>
{
    public void Configure(EntityTypeBuilder<WebPushDispatchOutbox> builder)
    {
        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.Status).IsRequired();
        builder.Property(e => e.MaxAttempts).HasDefaultValue(5);
        builder.Property(e => e.LastFailureCategory).HasMaxLength(100);
        builder.Property(e => e.LastError).HasMaxLength(2000);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("NOW()").IsRequired();
        builder.Property(e => e.UpdatedAt).HasDefaultValueSql("NOW()");

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Category)
            .WithMany()
            .HasForeignKey(e => e.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Subscription)
            .WithMany()
            .HasForeignKey(e => e.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.NotificationId, e.SubscriptionId })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ux_web_push_dispatch_outbox_notification_subscription");

        builder.HasIndex(e => new { e.Status, e.NextAttemptAt, e.CreatedAt })
            .HasDatabaseName("ix_web_push_dispatch_outbox_worker_poll");

        builder.HasIndex(e => new { e.TenantId, e.Status, e.LastFailureAt })
            .HasDatabaseName("ix_web_push_dispatch_outbox_tenant_status");
    }
}
