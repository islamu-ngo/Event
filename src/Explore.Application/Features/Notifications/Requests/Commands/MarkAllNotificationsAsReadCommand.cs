using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Notifications.Requests.Commands;

public sealed record MarkAllNotificationsAsReadCommand : ICommand<BaseCommandResponse<Guid>>
{
}
