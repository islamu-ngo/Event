using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Notifications.Requests.Commands;

public sealed record MarkNotificationAsReadCommand(Guid Id = default) : IRequest<BaseCommandResponse<Guid>>;
