using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Notifications.Requests.Commands;

public sealed record ArchiveNotificationCommand(Guid Id = default, bool Archive = true) : ICommand<BaseCommandResponse<Guid>>;
