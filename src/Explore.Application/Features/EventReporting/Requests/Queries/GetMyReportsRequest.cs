using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventReporting.Requests.Queries;

public sealed record GetMyReportsRequest : IQuery<PaginatedResult<MyEventReportDto>>
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = PaginatedResult<MyEventReportDto>.DefaultPageSize;
}
