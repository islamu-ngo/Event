using Explore.Application.DTOs.Notification;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Notifications.Requests.Commands;

public sealed record UpdateCurrentUserNotificationPreferenceMatrixCommand : IRequest<BaseCommandResponse<Guid>>
{
    private IReadOnlyList<UpdateNotificationPreferenceCellDto>? _cells;

    public IReadOnlyList<UpdateNotificationPreferenceCellDto>? Cells
    {
        get => _cells;
        init => _cells = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}
