using MediatR;

namespace Explore.Application.Features.Notifications.Requests.Commands;

public sealed record DeleteNotificationCommand(Guid Id = default) : IRequest<bool>;
