using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Notifications.Requests.Commands;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Notifications.Handlers.Commands;

public class DeleteNotificationCommandHandler : ICommandHandler<DeleteNotificationCommand, bool>
{
    private readonly INotificationRepository _notificationRepository;
    private readonly ICurrentUserService _currentUserService;

    public DeleteNotificationCommandHandler(
        INotificationRepository notificationRepository,
        ICurrentUserService currentUserService)
    {
        _notificationRepository = notificationRepository;
        _currentUserService = currentUserService;
    }

    public async Task<bool> ExecuteAsync(DeleteNotificationCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (userId == null)
            return false;

        var notification = await _notificationRepository.GetByIdForUser(request.Id, userId.Value);
        if (notification == null)
            return false;

        await _notificationRepository.Delete(notification);
        return true;
    }
}
