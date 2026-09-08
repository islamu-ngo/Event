namespace Explore.Application.DTOs.ActorSubscription;

public sealed record ActorSubscriptionDto
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid SubscriberTenantUserId { get; init; }
    public Guid SubscriberUserId { get; init; }
    public Guid TargetActorId { get; init; }
    public int TargetActorTypeId { get; init; }
    public string? TargetActorTypeName { get; init; }
    public string? TargetActorName { get; init; }
    public int StatusId { get; init; }
    public string? StatusCode { get; init; }
    public string? StatusName { get; init; }
    public int NotificationLevelId { get; init; }
    public string? NotificationLevelCode { get; init; }
    public string? NotificationLevelName { get; init; }
    public DateTime SubscribedAt { get; init; }
    public DateTime? UnsubscribedAt { get; init; }
    public Guid ConcurrencyStamp { get; init; }
}
