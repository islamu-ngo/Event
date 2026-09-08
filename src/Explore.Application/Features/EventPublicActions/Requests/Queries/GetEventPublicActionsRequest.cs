using Explore.Application.DTOs.Event;
using MediatR;

namespace Explore.Application.Features.EventPublicActions.Requests.Queries;

public sealed record GetEventPublicActionsRequest(Guid EventId) : IRequest<IReadOnlyList<EventPublicActionDto>>;
