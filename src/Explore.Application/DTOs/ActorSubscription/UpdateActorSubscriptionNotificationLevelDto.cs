using System.ComponentModel.DataAnnotations;

namespace Explore.Application.DTOs.ActorSubscription;

public sealed record UpdateActorSubscriptionNotificationLevelDto
{
    public UpdateActorSubscriptionNotificationLevelValueDto? NotificationLevel { get; init; }

    [Required]
    public required Guid ExpectedConcurrencyStamp { get; init; }
}

public sealed record UpdateActorSubscriptionNotificationLevelValueDto
{
    [Required]
    public required int Id { get; init; }
}
