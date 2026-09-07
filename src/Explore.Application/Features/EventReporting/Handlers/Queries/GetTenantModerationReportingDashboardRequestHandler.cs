using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Requests.Queries;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace Explore.Application.Features.EventReporting.Handlers.Queries;

public sealed class GetTenantModerationReportingDashboardRequestHandler(
    IEventReportRepository eventReportRepository,
    ITenantContext tenantContext,
    IConfiguration configuration)
    : IRequestHandler<GetTenantModerationReportingDashboardRequest, TenantModerationReportingDashboardDto>
{
    private const int DefaultStuckSyncMinutes = 120;

    public async Task<TenantModerationReportingDashboardDto> Handle(
        GetTenantModerationReportingDashboardRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId == Guid.Empty ? request.TenantId : tenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            return new TenantModerationReportingDashboardDto { GeneratedAtUtc = DateTime.UtcNow };
        }

        var now = DateTime.UtcNow;
        var stuckBeforeUtc = now.AddMinutes(-ReadInt("Reporting:Health:StuckProviderSyncMinutes", DefaultStuckSyncMinutes));

        var submittedReports = await eventReportRepository.CountByTenantAndStatusesAsync(
            tenantId,
            [EventReportStatus.Submitted],
            cancellationToken);
        var inReviewReports = await eventReportRepository.CountByTenantAndStatusesAsync(
            tenantId,
            [EventReportStatus.Triaged, EventReportStatus.UnderReview, EventReportStatus.Escalated],
            cancellationToken);
        var closedReports = await eventReportRepository.CountByTenantAndStatusesAsync(
            tenantId,
            [EventReportStatus.Actioned, EventReportStatus.Dismissed, EventReportStatus.Duplicate, EventReportStatus.Closed],
            cancellationToken);

        var openCases = await eventReportRepository.CountCasesByTenantAndStatusesAsync(
            tenantId,
            [EventReportCaseStatus.Open],
            cancellationToken);
        var assignedCases = await eventReportRepository.CountCasesByTenantAndStatusesAsync(
            tenantId,
            [EventReportCaseStatus.Assigned],
            cancellationToken);
        var waitingExternalCases = await eventReportRepository.CountCasesByTenantAndStatusesAsync(
            tenantId,
            [EventReportCaseStatus.WaitingExternal],
            cancellationToken);
        var waitingReporterCases = await eventReportRepository.CountCasesByTenantAndStatusesAsync(
            tenantId,
            [EventReportCaseStatus.WaitingReporter],
            cancellationToken);
        var decisionReadyCases = await eventReportRepository.CountCasesByTenantAndStatusesAsync(
            tenantId,
            [EventReportCaseStatus.DecisionReady],
            cancellationToken);

        var pendingSyncs = await eventReportRepository.CountExternalLinksByTenantAndSyncStateAsync(
            tenantId,
            EventReportSyncState.Pending,
            cancellationToken);
        var stuckPendingSyncs = await eventReportRepository.CountExternalLinksByTenantAndSyncStateBeforeAsync(
            tenantId,
            EventReportSyncState.Pending,
            stuckBeforeUtc,
            cancellationToken);
        var failedSyncs = await eventReportRepository.CountExternalLinksByTenantAndSyncStateAsync(
            tenantId,
            EventReportSyncState.Failed,
            cancellationToken);
        var disabledSyncs = await eventReportRepository.CountExternalLinksByTenantAndSyncStateAsync(
            tenantId,
            EventReportSyncState.Disabled,
            cancellationToken);
        var ignoredSyncs = await eventReportRepository.CountExternalLinksByTenantAndSyncStateAsync(
            tenantId,
            EventReportSyncState.Ignored,
            cancellationToken);

        return new TenantModerationReportingDashboardDto
        {
            TenantId = tenantId,
            GeneratedAtUtc = now,
            QueueHealth = new TenantModerationReportQueueHealthDto
            {
                SubmittedReports = submittedReports,
                InReviewReports = inReviewReports,
                ClosedReports = closedReports,
                OpenCases = openCases,
                AssignedCases = assignedCases,
                WaitingExternalCases = waitingExternalCases,
                WaitingReporterCases = waitingReporterCases,
                DecisionReadyCases = decisionReadyCases
            },
            ProviderSyncHealth = new TenantModerationProviderSyncHealthDto
            {
                PendingSyncs = pendingSyncs,
                StuckPendingSyncs = stuckPendingSyncs,
                FailedSyncs = failedSyncs,
                DisabledSyncs = disabledSyncs,
                IgnoredSyncs = ignoredSyncs
            }
        };
    }

    private int ReadInt(string key, int fallback)
    {
        var value = configuration[key];
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }
}
