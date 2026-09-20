using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Notification;
using Explore.Application.Features.Notifications.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Notifications.Handlers.Queries;

public class GetUnreadCountQueryHandler : IQueryHandler<GetUnreadCountQuery, UnreadCountDto>
{
    private readonly INotificationRepository _notificationRepository;
    private readonly ICurrentUserService _currentUserService;

    public GetUnreadCountQueryHandler(
        INotificationRepository notificationRepository,
        ICurrentUserService currentUserService)
    {
        _notificationRepository = notificationRepository;
        _currentUserService = currentUserService;
    }

    public async Task<UnreadCountDto> QueryAsync(GetUnreadCountQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (userId == null)
            return new UnreadCountDto { UnreadCount = 0 };

        var count = await _notificationRepository.GetUnreadCount(userId.Value, request.NotificationScopeId);
        return new UnreadCountDto { UnreadCount = count };
    }
}
