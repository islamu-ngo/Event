using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface INotificationRepository : IGenericRepository<Notification, Guid>
{
    Task<bool> ExistsByDeduplicationKeyAsync(Guid tenantId, Guid userId, string deduplicationKey, CancellationToken cancellationToken = default);

    Task<Notification?> GetByDeduplicationKeyAsync(Guid tenantId, Guid userId, string deduplicationKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets paginated notifications for a specific user with optional filtering.
    /// </summary>
    Task<(List<Notification> Items, int TotalCount)> GetUserNotificationsPaged(
        Guid userId, int pageNumber, int pageSize, bool? isRead = null, int? notificationTypeId = null,
        int? notificationScopeId = null, int? notificationReasonId = null, bool? isArchived = null, bool? isSnoozed = null);

    /// <summary>
    /// Gets the count of unread notifications for a specific user.
    /// Leverages partial index on is_read = false for performance.
    /// </summary>
    Task<int> GetUnreadCount(Guid userId, int? notificationScopeId = null);

    /// <summary>
    /// Marks a single notification as read for the specified user.
    /// Returns true if the notification was found and belongs to the user.
    /// </summary>
    Task<bool> MarkAsRead(Guid notificationId, Guid userId);

    /// <summary>
    /// Marks all unread notifications as read for the specified user.
    /// Uses ExecuteUpdateAsync for bulk efficiency.
    /// Uses a timestamp cutoff to prevent marking newly arrived notifications.
    /// Returns the number of notifications marked as read.
    /// </summary>
    Task<int> MarkAllAsRead(Guid userId, DateTime? cutoff = null);

    /// <summary>
    /// Gets a single notification by ID, scoped to the specified user.
    /// Returns null if not found or doesn't belong to the user.
    /// </summary>
    Task<Notification?> GetByIdForUser(Guid notificationId, Guid userId);

    /// <summary>
    /// Archives or unarchives a notification for the specified user.
    /// Returns true if the notification was found and belongs to the user.
    /// </summary>
    Task<bool> ArchiveNotification(Guid notificationId, Guid userId, bool archive);

    /// <summary>
    /// Snoozes a notification until the specified time for the specified user.
    /// Pass null snoozedUntil to unsnooze.
    /// Returns true if the notification was found and belongs to the user.
    /// </summary>
    Task<bool> SnoozeNotification(Guid notificationId, Guid userId, DateTime? snoozedUntil);
}
