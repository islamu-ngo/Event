using System.Runtime.CompilerServices;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Notification;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Services;

public sealed class NotificationRefreshStreamService : INotificationRefreshStreamService
{
    public const string InitialReason = "initial";
    public const string UnreadCountChangedReason = "unread-count-changed";

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(15);

    private readonly ICurrentUserService _currentUserService;
    private readonly INotificationRepository _notificationRepository;
    private readonly ILogger<NotificationRefreshStreamService> _logger;

    public NotificationRefreshStreamService(
        ICurrentUserService currentUserService,
        INotificationRepository notificationRepository,
        ILogger<NotificationRefreshStreamService> logger)
    {
        _currentUserService = currentUserService;
        _notificationRepository = notificationRepository;
        _logger = logger;
    }

    public async IAsyncEnumerable<NotificationRefreshHintDto> StreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId is not { } userId)
        {
            _logger.LogDebug("Notification refresh stream requested without an authenticated user");
            yield break;
        }

        int? lastUnreadCount = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var unreadCount = await _notificationRepository.GetUnreadCount(userId);

            if (lastUnreadCount is null || unreadCount != lastUnreadCount.Value)
            {
                yield return CreateHint(
                    unreadCount,
                    lastUnreadCount is null ? InitialReason : UnreadCountChangedReason);

                lastUnreadCount = unreadCount;
            }

            if (!await DelayAsync(cancellationToken))
                yield break;
        }
    }

    private static NotificationRefreshHintDto CreateHint(int unreadCount, string reason)
    {
        return new NotificationRefreshHintDto
        {
            UnreadCount = unreadCount,
            HasUnread = unreadCount > 0,
            Reason = reason,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private static async Task<bool> DelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(RefreshInterval, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
