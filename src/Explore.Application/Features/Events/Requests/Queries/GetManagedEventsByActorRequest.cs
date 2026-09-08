using Explore.Application.DTOs.Event;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Events.Requests.Queries;

public sealed record GetManagedEventsByActorRequest : IRequest<PaginatedResult<EventListDto>>
{
    public Guid ActorId { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
