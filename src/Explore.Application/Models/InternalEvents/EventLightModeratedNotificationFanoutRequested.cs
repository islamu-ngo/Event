namespace Explore.Application.Models.InternalEvents;

public sealed record EventLightModeratedNotificationFanoutRequested
{
    public required Guid TenantId { get; init; }
    public required Guid EventId { get; init; }
    public required Guid ModerationRecordId { get; init; }
    public required string EventTitle { get; init; }
    public required Guid SourceActorId { get; init; }
    public required DateTimeOffset ModeratedAt { get; init; }
}
