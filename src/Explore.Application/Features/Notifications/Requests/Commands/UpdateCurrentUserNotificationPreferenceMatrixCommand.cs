using Explore.Application.DTOs.Notification;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Notifications.Requests.Commands;

public sealed record UpdateCurrentUserNotificationPreferenceMatrixCommand : ICommand<BaseCommandResponse<Guid>>
{
    private IReadOnlyList<UpdateNotificationPreferenceCellDto>? _cells;

    public IReadOnlyList<UpdateNotificationPreferenceCellDto>? Cells
    {
        get => _cells;
        init => _cells = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}
