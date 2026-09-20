using Explore.Domain.Enums;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventPublicActions.Requests.Commands;

public sealed record RecordEventPublicActionEngagementCommand(
    EventPublicActionKindEnum ActionKind,
    string? Surface) : ICommand;
