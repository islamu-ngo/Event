using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Notifications.Requests.Commands;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Notifications.Handlers.Commands;

public class SnoozeNotificationCommandHandler : IRequestHandler<SnoozeNotificationCommand, BaseCommandResponse<Guid>>
{
    private readonly INotificationRepository _notificationRepository;
    private readonly ICurrentUserService _currentUserService;

    public SnoozeNotificationCommandHandler(
        INotificationRepository notificationRepository,
        ICurrentUserService currentUserService)
    {
        _notificationRepository = notificationRepository;
        _currentUserService = currentUserService;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(SnoozeNotificationCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (userId == null)
        {
            return BaseCommandResponse.Validation<Guid>(["User not authenticated."], "User not authenticated.");
        }

        var result = await _notificationRepository.SnoozeNotification(request.Id, userId.Value, request.SnoozedUntil);
        if (!result)
        {
            return BaseCommandResponse.Validation<Guid>(["Notification not found."], "Notification not found.");
        }

        return BaseCommandResponse.Success(
            request.Id,
            request.SnoozedUntil.HasValue
                ? $"Notification snoozed until {request.SnoozedUntil.Value:O}."
                : "Notification unsnoozed.");
    }
}
