namespace Explore.Application.DTOs.Notification;

public sealed record WebPushSubscriptionDto
{
    public Guid Id { get; init; }
    public required string DeviceIdentifier { get; init; }
    public DateTime LastSeenAt { get; init; }
    public DateTime? ExpirationTime { get; init; }
}
