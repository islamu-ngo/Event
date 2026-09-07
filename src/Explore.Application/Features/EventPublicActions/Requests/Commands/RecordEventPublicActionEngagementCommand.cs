using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.EventPublicActions.Requests.Commands;

public sealed record RecordEventPublicActionEngagementCommand(
    EventPublicActionKindEnum ActionKind,
    string? Surface) : IRequest<Unit>;
