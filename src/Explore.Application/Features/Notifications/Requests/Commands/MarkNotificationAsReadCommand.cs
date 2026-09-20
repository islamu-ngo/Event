using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Notifications.Requests.Commands;

public sealed record MarkNotificationAsReadCommand(Guid Id = default) : ICommand<BaseCommandResponse<Guid>>;
