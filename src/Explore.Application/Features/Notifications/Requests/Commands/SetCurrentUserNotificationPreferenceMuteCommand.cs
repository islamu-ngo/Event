using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Notifications.Requests.Commands;

public sealed record SetCurrentUserNotificationPreferenceMuteCommand(bool IsMuted = default) : ICommand<BaseCommandResponse<Guid>>;
