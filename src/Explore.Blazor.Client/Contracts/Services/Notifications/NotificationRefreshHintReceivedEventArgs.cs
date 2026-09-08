namespace Explore.Blazor.Client.Contracts.Services.Notifications;

public sealed record NotificationRefreshHintReceivedEventArgs(
    int UnreadCount,
    bool HasUnread,
    string Reason,
    DateTimeOffset GeneratedAt);
