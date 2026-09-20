using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSessionTemplate;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventSessionTemplates.Requests.Queries;

public sealed record GetEventSessionTemplateListRequest(
    Guid EventTemplateId = default,
    int PageNumber = 1,
    int PageSize = PaginatedResult<EventSessionTemplateListDto>.DefaultPageSize)
    : IQuery<PaginatedResult<EventSessionTemplateListDto>>;
