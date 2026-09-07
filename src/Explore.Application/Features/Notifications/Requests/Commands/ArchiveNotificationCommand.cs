using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Notifications.Requests.Commands;

public sealed record ArchiveNotificationCommand(Guid Id = default, bool Archive = true) : IRequest<BaseCommandResponse<Guid>>;
