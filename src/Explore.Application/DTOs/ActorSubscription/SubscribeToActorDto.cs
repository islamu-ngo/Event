namespace Explore.Application.DTOs.ActorSubscription;

public sealed record SubscribeToActorDto
{
    public Guid TargetActorId { get; init; }
}
