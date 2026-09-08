using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSessionCustomProperties.Requests.Queries;

public sealed record GetEventSessionCustomPropertyDefinitionListRequest : IRequest<PaginatedResult<EventSessionCustomPropertyDefinitionListDto>>
{
    public Guid EventSessionId { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = PaginatedResult<EventSessionCustomPropertyDefinitionListDto>.DefaultPageSize;
}
