using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class ActorSubscriptionConfiguration : IEntityTypeConfiguration<ActorSubscription>
{
    public void Configure(EntityTypeBuilder<ActorSubscription> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "ck_actor_subscriptions_target_actor_type",
                $"target_actor_type_id IN ({(int)ActorTypeEnum.Organization}, {(int)ActorTypeEnum.Group})");
            t.HasCheckConstraint(
                "ck_actor_subscriptions_status",
                $"status_id IN ({(int)ActorSubscriptionStatusEnum.Active}, {(int)ActorSubscriptionStatusEnum.Unsubscribed}, {(int)ActorSubscriptionStatusEnum.Blocked})");
            t.HasCheckConstraint(
                "ck_actor_subscriptions_notification_level",
                $"notification_level_id IN ({(int)ActorSubscriptionNotificationLevelEnum.None}, {(int)ActorSubscriptionNotificationLevelEnum.All}, {(int)ActorSubscriptionNotificationLevelEnum.Personalized})");
            t.HasCheckConstraint(
                "ck_actor_subscriptions_unsubscribed_at",
                $"(status_id = {(int)ActorSubscriptionStatusEnum.Unsubscribed} AND unsubscribed_at IS NOT NULL) OR (status_id <> {(int)ActorSubscriptionStatusEnum.Unsubscribed})");
        });

        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.StatusId).HasDefaultValue((int)ActorSubscriptionStatusEnum.Active);
        builder.Property(e => e.NotificationLevelId).HasDefaultValue((int)ActorSubscriptionNotificationLevelEnum.All);
        builder.Property(e => e.SubscribedAt).HasDefaultValueSql("NOW()").IsRequired();
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("NOW()").IsRequired();
        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.SubscriberTenantUser)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.SubscriberTenantUserId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.SubscriberUser)
            .WithMany()
            .HasForeignKey(e => e.SubscriberUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.TargetActor)
            .WithMany()
            .HasForeignKey(e => e.TargetActorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.TargetActorType)
            .WithMany()
            .HasForeignKey(e => e.TargetActorTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Status)
            .WithMany()
            .HasForeignKey(e => e.StatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.NotificationLevel)
            .WithMany()
            .HasForeignKey(e => e.NotificationLevelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.SubscriberTenantUserId, e.TargetActorId })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(e => new { e.TenantId, e.TargetActorId, e.StatusId, e.NotificationLevelId });

        builder.HasIndex(e => new { e.TenantId, e.SubscriberUserId });
    }
}
