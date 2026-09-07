using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Helpers;

public static class NotificationNavigationHelper
{
    public static string? GetEntityUrl(NotificationListDto notification)
    {
        if (string.IsNullOrWhiteSpace(notification.EntityId)
            || string.IsNullOrWhiteSpace(notification.NotificationEntityTypeName))
        {
            return null;
        }

        return notification.NotificationEntityTypeName.Trim().ToLowerInvariant() switch
        {
            "event" => $"/events/{notification.EntityId}",
            "organization" => $"/organization/profile/{notification.EntityId}",
            "group" => $"/group/profile/{notification.EntityId}",
            _ => null
        };
    }
}
