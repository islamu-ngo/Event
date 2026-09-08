using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Notifications.Requests.Commands;

public sealed record SetCurrentUserNotificationPreferenceMuteCommand(bool IsMuted = default) : IRequest<BaseCommandResponse<Guid>>;
