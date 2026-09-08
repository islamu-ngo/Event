using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventCustomProperties.Requests.Queries;

public sealed record GetEventCustomPropertyDefinitionListRequest : IRequest<PaginatedResult<EventCustomPropertyDefinitionListDto>>
{
    public Guid EventId { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = PaginatedResult<EventCustomPropertyDefinitionListDto>.DefaultPageSize;
}
