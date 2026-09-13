using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Notifications.Requests.Commands;

public sealed record DeleteNotificationCommand(Guid Id = default) : ICommand<bool>;
