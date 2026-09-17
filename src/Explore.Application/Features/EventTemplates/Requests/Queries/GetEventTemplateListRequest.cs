using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventTemplate;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventTemplates.Requests.Queries;

public sealed record GetEventTemplateListRequest : IQuery<PaginatedResult<EventTemplateListDto>>
{
    public int? EventTypeId { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = PaginatedResult<EventTemplateListDto>.DefaultPageSize;
}
