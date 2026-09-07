using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Mappers;
using Explore.Application.Features.EventReporting.Requests.Queries;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventReporting.Handlers.Queries;

public sealed class GetMyReportRequestHandler(
    IEventReportRepository eventReportRepository,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService,
    HybridCache cache)
    : IRequestHandler<GetMyReportRequest, MyEventReportDto?>
{
    public async Task<MyEventReportDto?> Handle(
        GetMyReportRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId;
        var currentUserId = currentUserService.UserId;
        if (request.ReportId == Guid.Empty || tenantId == Guid.Empty || currentUserId is null)
        {
            return null;
        }

        var cacheKey = $"event-reporting:my-report:{tenantId:N}:{currentUserId.Value:N}:{request.ReportId:N}";
        return await cache.GetOrCreateAsync(
            cacheKey,
            async token =>
            {
                var report = await eventReportRepository.GetByIdAsync(
                    tenantId,
                    request.ReportId,
                    token);

                return report is null || report.ReporterUserId != currentUserId
                    ? null
                    : MyEventReportDtoMapper.Map(report);
            },
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromSeconds(30),
                LocalCacheExpiration = TimeSpan.FromSeconds(10)
            },
            cancellationToken: cancellationToken);
    }
}
