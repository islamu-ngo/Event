namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Notification;
using Explore.Application.Hateoas;

/// <summary>
/// Link policy for NotificationDto (detail view).
/// All links require authentication since notifications are personal data.
/// </summary>
public sealed class NotificationDetailLinkPolicy : ILinkPolicy<NotificationDto>
{
    public IEnumerable<LinkDefinition> GetLinks(NotificationDto dto, ClaimsPrincipal? user)
    {
        // Self link
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetNotificationById,
            new { id = dto.Id },
            "GET",
            dto.Title,
            RequiresAuth: true);

        // Collection link
        yield return new LinkDefinition(
            LinkRelations.Collection,
            RouteNames.GetNotifications,
            null,
            "GET",
            "All notifications",
            RequiresAuth: true);

        // Mark as read (only if not already read)
        if (!dto.IsRead)
        {
            yield return new LinkDefinition(
                "mark-read",
                RouteNames.MarkNotificationAsRead,
                new { id = dto.Id },
                "PATCH",
                "Mark as read",
                RequiresAuth: true);
        }

        // Related entity link
        if (dto.NotificationEntityTypeId.HasValue && !string.IsNullOrEmpty(dto.EntityId))
        {
            yield return new LinkDefinition(
                "related-entity",
                RouteNames.GetNotificationById,
                new { id = dto.Id },
                "GET",
                $"{dto.NotificationEntityTypeName}: {dto.EntityId}",
                RequiresAuth: true);
        }

        // Delete link
        yield return new LinkDefinition(
            "delete",
            RouteNames.DeleteNotification,
            new { id = dto.Id },
            "DELETE",
            "Delete notification",
            RequiresAuth: true);
    }
}

/// <summary>
/// Link policy for NotificationListDto in collection context.
/// </summary>
public sealed class NotificationCollectionLinkPolicy : ICollectionLinkPolicy<NotificationListDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(NotificationListDto dto, ClaimsPrincipal? user)
    {
        // Self link for item
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetNotificationById,
            new { id = dto.Id },
            "GET",
            dto.Title,
            RequiresAuth: true);

        // Mark as read (only if not already read)
        if (!dto.IsRead)
        {
            yield return new LinkDefinition(
                "mark-read",
                RouteNames.MarkNotificationAsRead,
                new { id = dto.Id },
                "PATCH",
                "Mark as read",
                RequiresAuth: true);
        }
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        // Mark all as read
        yield return new LinkDefinition(
            "mark-all-read",
            RouteNames.MarkAllNotificationsAsRead,
            null,
            "POST",
            "Mark all notifications as read",
            RequiresAuth: true);

        // Unread count
        yield return new LinkDefinition(
            "unread-count",
            RouteNames.GetUnreadNotificationCount,
            null,
            "GET",
            "Unread notification count",
            RequiresAuth: true);
    }
}

public sealed class NotificationPreferenceMatrixLinkPolicy :
    ILinkPolicy<NotificationPreferenceMatrixDto>,
    ICollectionLinkPolicy<NotificationPreferenceMatrixDto>
{
    public IEnumerable<LinkDefinition> GetLinks(NotificationPreferenceMatrixDto dto, ClaimsPrincipal? user)
    {
        var (selfRoute, saveRoute, muteRoute, routeValues, titlePrefix, permissionResourceKind, permissionResourceId) = dto.Scope switch
        {
            "organization" => (
                RouteNames.GetOrganizationNotificationPreferences,
                RouteNames.UpdateOrganizationNotificationPreferences,
                RouteNames.SetOrganizationNotificationPreferenceMute,
                (object?)new { id = dto.OrganizationId },
                "Organization",
                ResourceKinds.Organization,
                dto.OrganizationId?.ToString()),
            "group" => (
                RouteNames.GetGroupNotificationPreferences,
                RouteNames.UpdateGroupNotificationPreferences,
                RouteNames.SetGroupNotificationPreferenceMute,
                (object?)new { id = dto.GroupId },
                "Group",
                ResourceKinds.Group,
                dto.GroupId?.ToString()),
            _ => (
                RouteNames.GetCurrentUserNotificationPreferences,
                RouteNames.UpdateCurrentUserNotificationPreferences,
                RouteNames.SetCurrentUserNotificationPreferenceMute,
                null,
                "Current user",
                (string?)null,
                (string?)null)
        };

        yield return new LinkDefinition(
            LinkRelations.Self,
            selfRoute,
            routeValues,
            "GET",
            $"{titlePrefix} notification preferences",
            RequiresAuth: true);

        yield return new LinkDefinition(
            "save",
            saveRoute,
            routeValues,
            "PATCH",
            "Patch notification preference choices",
            RequiresAuth: true,
            PermissionResourceKind: permissionResourceKind,
            PermissionAction: permissionResourceKind is null ? null : AuthorizationActions.Update,
            PermissionResourceId: permissionResourceId);

        yield return new LinkDefinition(
            "set-mute",
            muteRoute,
            routeValues,
            "PUT",
            "Set notification preference mute state",
            RequiresAuth: true,
            PermissionResourceKind: permissionResourceKind,
            PermissionAction: permissionResourceKind is null ? null : AuthorizationActions.Update,
            PermissionResourceId: permissionResourceId);

        if (dto.Scope == "user")
        {
            yield return new LinkDefinition(
                "subscribe-web-push",
                RouteNames.SubscribeCurrentUserWebPushSubscription,
                null,
                "POST",
                "Subscribe current browser to Web Push",
                RequiresAuth: true);
        }
    }

    public IEnumerable<LinkDefinition> GetItemLinks(NotificationPreferenceMatrixDto dto, ClaimsPrincipal? user)
    {
        return GetLinks(dto, user);
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        return [];
    }
}

public sealed class WebPushSubscriptionLinkPolicy :
    ILinkPolicy<WebPushSubscriptionDto>,
    ICollectionLinkPolicy<WebPushSubscriptionDto>
{
    public IEnumerable<LinkDefinition> GetLinks(WebPushSubscriptionDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetCurrentUserWebPushSubscription,
            new { deviceIdentifier = dto.DeviceIdentifier },
            "GET",
            "Current Web Push subscription",
            RequiresAuth: true);

        yield return new LinkDefinition(
            "unsubscribe",
            RouteNames.UnsubscribeCurrentUserWebPushSubscription,
            new { subscriptionId = dto.Id },
            "DELETE",
            "Unsubscribe this Web Push subscription",
            RequiresAuth: true);
    }

    public IEnumerable<LinkDefinition> GetItemLinks(WebPushSubscriptionDto dto, ClaimsPrincipal? user)
    {
        return GetLinks(dto, user);
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            "subscribe",
            RouteNames.SubscribeCurrentUserWebPushSubscription,
            null,
            "POST",
            "Subscribe current browser to Web Push",
            RequiresAuth: true);
    }
}
