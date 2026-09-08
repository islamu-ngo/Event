namespace Explore.Application.DTOs.ActorSubscription;

public sealed record UnsubscribeFromActorDto
{
    public Guid TargetActorId { get; init; }
    public Guid ExpectedConcurrencyStamp { get; init; }
}
