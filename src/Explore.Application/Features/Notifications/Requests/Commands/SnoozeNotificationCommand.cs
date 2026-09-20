using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Notifications.Requests.Commands;

public sealed record SnoozeNotificationCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid Id { get; init; }
    public DateTime? SnoozedUntil { get; init; }
}
