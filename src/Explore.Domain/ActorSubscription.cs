using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class ActorSubscription : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }

    public Guid SubscriberTenantUserId { get; set; }
    public required TenantUser SubscriberTenantUser { get; set; }

    public Guid SubscriberUserId { get; set; }
    public required User SubscriberUser { get; set; }

    public Guid TargetActorId { get; set; }
    public required Actor TargetActor { get; set; }

    public int TargetActorTypeId { get; set; }
    public required ActorType TargetActorType { get; set; }

    public int StatusId { get; set; } = (int)ActorSubscriptionStatusEnum.Active;
    public required ActorSubscriptionStatus Status { get; set; }

    public int NotificationLevelId { get; set; } = (int)ActorSubscriptionNotificationLevelEnum.All;
    public required ActorSubscriptionNotificationLevel NotificationLevel { get; set; }

    public DateTime SubscribedAt { get; set; }
    public DateTime? UnsubscribedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public Guid ConcurrencyStamp { get; set; }
}
