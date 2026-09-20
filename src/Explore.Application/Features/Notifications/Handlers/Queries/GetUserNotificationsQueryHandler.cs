using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Notification;
using Explore.Application.Features.Notifications.Requests.Queries;
using Explore.Application.Mappings;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Notifications.Handlers.Queries;

public class GetUserNotificationsQueryHandler : IQueryHandler<GetUserNotificationsQuery, PaginatedResult<NotificationListDto>>
{
    private readonly INotificationRepository _notificationRepository;
    private readonly ICurrentUserService _currentUserService;

    public GetUserNotificationsQueryHandler(
        INotificationRepository notificationRepository,
        ICurrentUserService currentUserService)
    {
        _notificationRepository = notificationRepository;
        _currentUserService = currentUserService;
    }

    public async Task<PaginatedResult<NotificationListDto>> QueryAsync(GetUserNotificationsQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (userId == null)
            return PaginatedResult<NotificationListDto>.Create([], 0, request.PageNumber, request.PageSize);

        var (pageNumber, pageSize) = PaginatedResult<NotificationListDto>.NormalizeParameters(request.PageNumber, request.PageSize);

        var (items, totalCount) = await _notificationRepository.GetUserNotificationsPaged(
            userId.Value, pageNumber, pageSize, request.IsRead, request.NotificationTypeId,
            request.NotificationScopeId, request.NotificationReasonId, request.IsArchived, request.IsSnoozed);

        var dtos = items.Select(NotificationMapper.ToListItem).ToList();

        return PaginatedResult<NotificationListDto>.Create(dtos, totalCount, pageNumber, pageSize);
    }
}
