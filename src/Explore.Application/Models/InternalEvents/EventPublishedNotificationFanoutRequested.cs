namespace Explore.Application.Models.InternalEvents;

public sealed record EventPublishedNotificationFanoutRequested
{
    public required Guid TenantId { get; init; }
    public required Guid EventId { get; init; }
    public required string EventTitle { get; init; }
    public required Guid SourceActorId { get; init; }
    public required DateTimeOffset StartDate { get; init; }
    public DateTimeOffset? EndDate { get; init; }
    public required DateTimeOffset PublishedAt { get; init; }
}
