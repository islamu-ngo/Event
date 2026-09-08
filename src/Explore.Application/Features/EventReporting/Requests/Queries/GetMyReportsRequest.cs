using Explore.Application.DTOs.EventReporting;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventReporting.Requests.Queries;

public sealed record GetMyReportsRequest : IRequest<PaginatedResult<MyEventReportDto>>
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = PaginatedResult<MyEventReportDto>.DefaultPageSize;
}
