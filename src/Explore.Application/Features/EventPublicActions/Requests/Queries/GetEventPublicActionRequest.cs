using Explore.Application.DTOs.Event;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventPublicActions.Requests.Queries;

public sealed record GetEventPublicActionRequest(Guid EventId, Guid ActionId) : IQuery<EventPublicActionDto?>;
