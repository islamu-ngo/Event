using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Notification;
using Explore.Application.Features.Notifications.Requests.Queries;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Notifications.Handlers.Queries;

public class GetNotificationByIdQueryHandler : IQueryHandler<GetNotificationByIdQuery, NotificationDto?>
{
    private readonly INotificationRepository _notificationRepository;
    private readonly ICurrentUserService _currentUserService;

    public GetNotificationByIdQueryHandler(
        INotificationRepository notificationRepository,
        ICurrentUserService currentUserService)
    {
        _notificationRepository = notificationRepository;
        _currentUserService = currentUserService;
    }

    public async Task<NotificationDto?> QueryAsync(GetNotificationByIdQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (userId == null)
            return null;

        var notification = await _notificationRepository.GetByIdForUser(request.Id, userId.Value);
        if (notification == null)
            return null;

        return NotificationMapper.ToDetail(notification);
    }
}
