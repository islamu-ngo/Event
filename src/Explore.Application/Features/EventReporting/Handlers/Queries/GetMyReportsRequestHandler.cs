using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Mappers;
using Explore.Application.Features.EventReporting.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventReporting.Handlers.Queries;

public sealed class GetMyReportsRequestHandler(
    IEventReportRepository eventReportRepository,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetMyReportsRequest, PaginatedResult<MyEventReportDto>>
{
    public async Task<PaginatedResult<MyEventReportDto>> Handle(
        GetMyReportsRequest request,
        CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<MyEventReportDto>.NormalizeParameters(
            request.PageNumber,
            request.PageSize);

        var tenantId = tenantContext.TenantId;
        var currentUserId = currentUserService.UserId;
        if (tenantId == Guid.Empty || currentUserId is null)
        {
            return PaginatedResult<MyEventReportDto>.Create([], 0, pageNumber, pageSize);
        }

        var (reports, totalCount) = await eventReportRepository.GetByReporterAsync(
            tenantId,
            currentUserId.Value,
            pageNumber,
            pageSize,
            cancellationToken);

        return PaginatedResult<MyEventReportDto>.Create(
            reports.Select(MyEventReportDtoMapper.Map).ToList(),
            totalCount,
            pageNumber,
            pageSize);
    }
}
