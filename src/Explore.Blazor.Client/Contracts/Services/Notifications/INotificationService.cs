using Explore.Blazor.Client.Models;

namespace Explore.Blazor.Client.Contracts.Services.Notifications;

public interface INotificationService
{
    Task<PaginatedResult<Clients.NotificationListDto>> GetNotificationsAsync(
        int pageNumber,
        int pageSize,
        bool? isRead = null,
        int? notificationScopeId = null,
        int? notificationReasonId = null,
        bool? isArchived = null,
        bool? isSnoozed = null);

    Task<Clients.NotificationDto?> GetNotificationByIdAsync(Guid id);

    Task<int> GetUnreadCountAsync(int? notificationScopeId = null);

    Task<bool> MarkAsReadAsync(Guid notificationId);

    Task<bool> MarkAllAsReadAsync();

    Task<bool> DeleteAsync(Guid notificationId);

    Task<bool> ArchiveAsync(Guid notificationId, bool archive = true);

    Task<bool> SnoozeAsync(Guid notificationId, DateTimeOffset snoozedUntil);

    Task<Clients.HalResourceOfNotificationPreferenceMatrixDto?> GetCurrentUserPreferenceMatrixAsync();

    Task<bool> SaveCurrentUserPreferenceMatrixAsync(IReadOnlyCollection<Clients.UpdateNotificationPreferenceCellDto> cells);

    Task<bool> SetCurrentUserPreferenceMuteAsync(bool isMuted);

    Task<Clients.WebPushPublicConfiguration?> GetWebPushConfigurationAsync();

    Task<string?> GetVapidPublicKeyAsync();

    Task<Clients.HalResourceOfWebPushSubscriptionDto?> GetCurrentWebPushSubscriptionAsync(string deviceIdentifier);

    Task<bool> SubscribeWebPushAsync(
        string deviceIdentifier,
        string endpoint,
        string p256Dh,
        string auth,
        DateTimeOffset? expirationTime);

    Task<bool> UnsubscribeWebPushAsync(Guid subscriptionId);

    Task<Clients.HalResourceOfNotificationPreferenceMatrixDto?> GetOrganizationPreferenceMatrixAsync(Guid organizationId);

    Task<bool> SaveOrganizationPreferenceMatrixAsync(Guid organizationId, IReadOnlyCollection<Clients.UpdateNotificationPreferenceCellDto> cells);

    Task<bool> SetOrganizationPreferenceMuteAsync(Guid organizationId, bool isMuted);

    Task<Clients.HalResourceOfNotificationPreferenceMatrixDto?> GetGroupPreferenceMatrixAsync(Guid groupId);

    Task<bool> SaveGroupPreferenceMatrixAsync(Guid groupId, IReadOnlyCollection<Clients.UpdateNotificationPreferenceCellDto> cells);

    Task<bool> SetGroupPreferenceMuteAsync(Guid groupId, bool isMuted);
}
