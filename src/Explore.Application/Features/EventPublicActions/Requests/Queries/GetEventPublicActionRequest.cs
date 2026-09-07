using Explore.Application.DTOs.Event;
using MediatR;

namespace Explore.Application.Features.EventPublicActions.Requests.Queries;

public sealed record GetEventPublicActionRequest(Guid EventId, Guid ActionId) : IRequest<EventPublicActionDto?>;
