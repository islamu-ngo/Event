using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Event;
using Explore.Application.Responses;

namespace Explore.Application.Features.Events.Requests.Queries;

public sealed record GetManagedEventsByActorRequest : IQuery<PaginatedResult<EventListDto>>
{
    public Guid ActorId { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
