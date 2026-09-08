using Explore.Application.Specifications.EventReports;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Persistence;

public interface IEventReportRepository : IGenericRepository<EventReport, Guid>
{
    Task<EventReport?> GetByIdAsync(
        Guid tenantId,
        Guid reportId,
        CancellationToken cancellationToken);

    Task<EventReport?> GetByIdForUpdateAsync(
        Guid tenantId,
        Guid reportId,
        CancellationToken cancellationToken);

    Task PersistDecisionCaptureAsync(
        EventReport report,
        EventReportDecision decision,
        CancellationToken cancellationToken);

    Task<EventReport?> GetByIdWithEvidenceAsync(
        Guid tenantId,
        Guid reportId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EventReport>> GetByEventAsync(
        Guid tenantId,
        Guid eventId,
        int limit,
        CancellationToken cancellationToken);

    Task<(List<EventReport> Items, int TotalCount)> GetByReporterAsync(
        Guid tenantId,
        Guid reporterUserId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken);

    Task<(List<EventReport> Items, int TotalCount)> GetReportQueueAsync(
        Guid tenantId,
        int pageNumber,
        int pageSize,
        EventReportQuerySpecification specification,
        CancellationToken cancellationToken);

    Task<bool> ExistsByReporterAndEventAsync(
        Guid tenantId,
        Guid eventId,
        Guid? reporterUserId,
        Guid? reporterActorId,
        string? reporterIpHash,
        string? reporterUserAgentHash,
        string reasonCode,
        string? subcategoryCode,
        DateTime createdAfterUtc,
        CancellationToken cancellationToken);

    Task<int> CountByReporterSinceAsync(
        Guid tenantId,
        Guid? reporterUserId,
        Guid? reporterActorId,
        string? reporterIpHash,
        string? reporterUserAgentHash,
        DateTime createdAfterUtc,
        CancellationToken cancellationToken);

    Task<int> CountByReporterAndEventSinceAsync(
        Guid tenantId,
        Guid eventId,
        Guid? reporterUserId,
        Guid? reporterActorId,
        string? reporterIpHash,
        string? reporterUserAgentHash,
        DateTime createdAfterUtc,
        CancellationToken cancellationToken);

    Task<int> CountByEventSinceAsync(
        Guid tenantId,
        Guid eventId,
        DateTime createdAfterUtc,
        CancellationToken cancellationToken);

    Task<int> CountByTenantAndStatusesAsync(
        Guid tenantId,
        IReadOnlyCollection<EventReportStatus> statuses,
        CancellationToken cancellationToken);

    Task<int> CountCasesByTenantAndStatusesAsync(
        Guid tenantId,
        IReadOnlyCollection<EventReportCaseStatus> statuses,
        CancellationToken cancellationToken);

    Task<int> CountExternalLinksByTenantAndSyncStateAsync(
        Guid tenantId,
        EventReportSyncState syncState,
        CancellationToken cancellationToken);

    Task<int> CountExternalLinksByTenantAndSyncStateBeforeAsync(
        Guid tenantId,
        EventReportSyncState syncState,
        DateTime olderThanUtc,
        CancellationToken cancellationToken);

    Task<int> CountExternalLinksBySyncStateAsync(
        EventReportSyncState syncState,
        CancellationToken cancellationToken);

    Task<int> CountExternalLinksBySyncStateBeforeAsync(
        EventReportSyncState syncState,
        DateTime olderThanUtc,
        CancellationToken cancellationToken);
}
