using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Constants;
using Explore.Blazor.Client.Contracts.Services.Notifications;
using Explore.Blazor.Client.Models;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

public class NotificationService : INotificationService
{
    private readonly INotificationClient _apiClient;
    private readonly IGroupClient _groupClient;
    private readonly IOrganizationClient _organizationClient;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        INotificationClient apiClient,
        IGroupClient groupClient,
        IOrganizationClient organizationClient,
        ILogger<NotificationService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _groupClient = groupClient ?? throw new ArgumentNullException(nameof(groupClient));
        _organizationClient = organizationClient ?? throw new ArgumentNullException(nameof(organizationClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PaginatedResult<NotificationListDto>> GetNotificationsAsync(
        int pageNumber,
        int pageSize,
        bool? isRead = null,
        int? notificationScopeId = null,
        int? notificationReasonId = null,
        bool? isArchived = null,
        bool? isSnoozed = null)
    {
        try
        {
            var response = await _apiClient.GetNotificationsAsync(
                pageNumber: pageNumber,
                pageSize: pageSize,
                isRead: isRead,
                notificationTypeId: null,
                notificationScopeId: notificationScopeId,
                notificationReasonId: notificationReasonId,
                isArchived: isArchived,
                isSnoozed: isSnoozed);

            return new PaginatedResult<NotificationListDto>
            {
                Items = response.Items?.ToList() ?? [],
                PageNumber = response.PageNumber ?? pageNumber,
                PageSize = response.PageSize ?? pageSize,
                TotalCount = response.TotalCount ?? 0
            };
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] API error fetching notifications: {StatusCode}", ex.StatusCode);
            return PaginatedResult<NotificationListDto>.Empty(pageNumber, pageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] Error fetching notifications");
            return PaginatedResult<NotificationListDto>.Empty(pageNumber, pageSize);
        }
    }

    public async Task<NotificationDto?> GetNotificationByIdAsync(Guid id)
    {
        try
        {
            return await _apiClient.GetNotificationByIdAsync(id);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("[NOTIFICATION SERVICE] Notification not found: {Id}", id);
            return null;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] API error fetching notification {Id}: {StatusCode}", id, ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] Error fetching notification {Id}", id);
            return null;
        }
    }

    public async Task<int> GetUnreadCountAsync(int? notificationScopeId = null)
    {
        try
        {
            var response = await _apiClient.GetUnreadNotificationCountAsync(notificationScopeId);
            return response.UnreadCount ?? 0;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] API error fetching unread count: {StatusCode}", ex.StatusCode);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] Error fetching unread count");
            return 0;
        }
    }

    public async Task<bool> MarkAsReadAsync(Guid notificationId)
    {
        try
        {
            var response = await _apiClient.MarkNotificationAsReadAsync(notificationId);
            return response.Success;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] API error marking notification as read: {StatusCode}", ex.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] Error marking notification as read");
            return false;
        }
    }

    public async Task<bool> MarkAllAsReadAsync()
    {
        try
        {
            var response = await _apiClient.MarkAllNotificationsAsReadAsync();
            return response.Success;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] API error marking all as read: {StatusCode}", ex.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] Error marking all as read");
            return false;
        }
    }

    public async Task<bool> DeleteAsync(Guid notificationId)
    {
        try
        {
            await _apiClient.DeleteNotificationAsync(notificationId);
            return true;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] API error deleting notification: {StatusCode}", ex.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] Error deleting notification");
            return false;
        }
    }

    public async Task<bool> ArchiveAsync(Guid notificationId, bool archive = true)
    {
        try
        {
            var response = await _apiClient.ArchiveNotificationAsync(notificationId, archive);
            return response.Success;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] API error archiving notification: {StatusCode}", ex.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] Error archiving notification");
            return false;
        }
    }

    public async Task<bool> SnoozeAsync(Guid notificationId, DateTimeOffset snoozedUntil)
    {
        try
        {
            var response = await _apiClient.SnoozeNotificationAsync(notificationId, snoozedUntil);
            return response.Success;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] API error snoozing notification: {StatusCode}", ex.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] Error snoozing notification");
            return false;
        }
    }

    public async Task<HalResourceOfNotificationPreferenceMatrixDto?> GetCurrentUserPreferenceMatrixAsync()
    {
        try
        {
            return await _apiClient.GetCurrentUserNotificationPreferencesAsync();
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] API error fetching notification preference matrix: {StatusCode}", ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] Error fetching notification preference matrix");
            return null;
        }
    }

    public async Task<bool> SaveCurrentUserPreferenceMatrixAsync(IReadOnlyCollection<UpdateNotificationPreferenceCellDto> cells)
    {
        if (cells.Count == 0)
            return true;

        try
        {
            var response = await _apiClient.UpdateCurrentUserNotificationPreferencesAsync(new UpdateNotificationPreferenceMatrixDto
            {
                Cells = cells.ToList()
            });

            return response.Success;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] API error saving notification preference matrix: {StatusCode}", ex.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] Error saving notification preference matrix");
            return false;
        }
    }

    public async Task<bool> SetCurrentUserPreferenceMuteAsync(bool isMuted)
    {
        try
        {
            var response = await _apiClient.SetCurrentUserNotificationPreferenceMuteAsync(new SetNotificationPreferenceMuteDto
            {
                IsMuted = isMuted
            });

            return response.Success;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] API error setting notification mute state: {StatusCode}", ex.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] Error setting notification mute state");
            return false;
        }
    }

    public async Task<WebPushPublicConfiguration?> GetWebPushConfigurationAsync()
    {
        try
        {
            return await _apiClient.GetWebPushConfigurationAsync();
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] API error fetching Web Push configuration: {StatusCode}", ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] Error fetching Web Push configuration");
            return null;
        }
    }

    public async Task<string?> GetVapidPublicKeyAsync()
    {
        try
        {
            return (await _apiClient.GetVapidPublicKeyAsync()).Trim();
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] API error fetching VAPID public key: {StatusCode}", ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] Error fetching VAPID public key");
            return null;
        }
    }

    public async Task<HalResourceOfWebPushSubscriptionDto?> GetCurrentWebPushSubscriptionAsync(string deviceIdentifier)
    {
        try
        {
            return await _apiClient.GetCurrentUserWebPushSubscriptionAsync(deviceIdentifier);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] API error fetching Web Push subscription: {StatusCode}", ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] Error fetching Web Push subscription");
            return null;
        }
    }

    public async Task<bool> SubscribeWebPushAsync(
        string deviceIdentifier,
        string endpoint,
        string p256Dh,
        string auth,
        DateTimeOffset? expirationTime)
    {
        try
        {
            var response = await _apiClient.SubscribeCurrentUserWebPushSubscriptionAsync(new SubscribeCurrentUserWebPushSubscriptionCommand
            {
                DeviceIdentifier = deviceIdentifier,
                Endpoint = endpoint,
                P256Dh = p256Dh,
                Auth = auth,
                ExpirationTime = expirationTime
            });

            return response.Success;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] API error subscribing Web Push device: {StatusCode}", ex.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] Error subscribing Web Push device");
            return false;
        }
    }

    public async Task<bool> UnsubscribeWebPushAsync(Guid subscriptionId)
    {
        try
        {
            var response = await _apiClient.UnsubscribeCurrentUserWebPushSubscriptionAsync(subscriptionId);
            return response.Success;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] API error unsubscribing Web Push device: {StatusCode}", ex.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] Error unsubscribing Web Push device");
            return false;
        }
    }

    public async Task<HalResourceOfNotificationPreferenceMatrixDto?> GetOrganizationPreferenceMatrixAsync(Guid organizationId)
    {
        try
        {
            return await _organizationClient.GetOrganizationNotificationPreferencesAsync(organizationId);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] API error fetching organization notification preferences: {StatusCode}", ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] Error fetching organization notification preferences");
            return null;
        }
    }

    public async Task<bool> SaveOrganizationPreferenceMatrixAsync(Guid organizationId, IReadOnlyCollection<UpdateNotificationPreferenceCellDto> cells)
    {
        if (cells.Count == 0)
            return true;

        try
        {
            var response = await _organizationClient.UpdateOrganizationNotificationPreferencesAsync(organizationId, new UpdateNotificationPreferenceMatrixDto
            {
                Cells = cells.ToList()
            });

            return response.Success;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] API error saving organization notification preferences: {StatusCode}", ex.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] Error saving organization notification preferences");
            return false;
        }
    }

    public async Task<bool> SetOrganizationPreferenceMuteAsync(Guid organizationId, bool isMuted)
    {
        try
        {
            var response = await _organizationClient.SetOrganizationNotificationPreferenceMuteAsync(organizationId, new SetNotificationPreferenceMuteDto
            {
                IsMuted = isMuted
            });

            return response.Success;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] API error setting organization notification mute state: {StatusCode}", ex.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] Error setting organization notification mute state");
            return false;
        }
    }

    public async Task<HalResourceOfNotificationPreferenceMatrixDto?> GetGroupPreferenceMatrixAsync(Guid groupId)
    {
        try
        {
            return await _groupClient.GetGroupNotificationPreferencesAsync(groupId);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] API error fetching group notification preferences: {StatusCode}", ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] Error fetching group notification preferences");
            return null;
        }
    }

    public async Task<bool> SaveGroupPreferenceMatrixAsync(Guid groupId, IReadOnlyCollection<UpdateNotificationPreferenceCellDto> cells)
    {
        if (cells.Count == 0)
            return true;

        try
        {
            var response = await _groupClient.UpdateGroupNotificationPreferencesAsync(groupId, new UpdateNotificationPreferenceMatrixDto
            {
                Cells = cells.ToList()
            });

            return response.Success;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] API error saving group notification preferences: {StatusCode}", ex.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] Error saving group notification preferences");
            return false;
        }
    }

    public async Task<bool> SetGroupPreferenceMuteAsync(Guid groupId, bool isMuted)
    {
        try
        {
            var response = await _groupClient.SetGroupNotificationPreferenceMuteAsync(groupId, new SetNotificationPreferenceMuteDto
            {
                IsMuted = isMuted
            });

            return response.Success;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] API error setting group notification mute state: {StatusCode}", ex.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NOTIFICATION SERVICE] Error setting group notification mute state");
            return false;
        }
    }
}
