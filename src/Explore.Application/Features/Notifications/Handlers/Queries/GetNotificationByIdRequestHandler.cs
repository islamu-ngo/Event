using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Notification;
using Explore.Application.Features.Notifications.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Notifications.Handlers.Queries;

public class GetNotificationByIdRequestHandler : IRequestHandler<GetNotificationByIdRequest, NotificationDto?>
{
    private readonly INotificationRepository _notificationRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMapper _mapper;

    public GetNotificationByIdRequestHandler(
        INotificationRepository notificationRepository,
        ICurrentUserService currentUserService,
        IMapper mapper)
    {
        _notificationRepository = notificationRepository;
        _currentUserService = currentUserService;
        _mapper = mapper;
    }

    public async Task<NotificationDto?> Handle(GetNotificationByIdRequest request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (userId == null)
            return null;

        var notification = await _notificationRepository.GetByIdForUser(request.Id, userId.Value);
        if (notification == null)
            return null;

        return _mapper.Map<NotificationDto>(notification);
    }
}
