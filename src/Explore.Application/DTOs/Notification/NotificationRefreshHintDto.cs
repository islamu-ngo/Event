namespace Explore.Application.DTOs.Notification;

public sealed record NotificationRefreshHintDto
{
    public int UnreadCount { get; init; }

    public bool HasUnread { get; init; }

    public required string Reason { get; init; }

    public DateTimeOffset GeneratedAt { get; init; }
}
