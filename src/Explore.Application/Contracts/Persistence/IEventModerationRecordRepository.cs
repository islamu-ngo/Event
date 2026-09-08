using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventModerationRecordRepository : IGenericRepository<EventModerationRecord, Guid>
{
    Task<EventModerationRecord?> GetByIdAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EventModerationRecord>> GetByEventAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken);

    Task<EventModerationRecord?> GetLatestByEventAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken);

    Task<EventModerationRecord?> GetBySourceReportDecisionAsync(
        Guid tenantId,
        Guid reportId,
        Guid decisionId,
        CancellationToken cancellationToken);
}
