using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class EventModerationRecordRepository : GenericRepository<EventModerationRecord, Guid>, IEventModerationRecordRepository
{
    private readonly ExploreDbContext _dbContext;

    public EventModerationRecordRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EventModerationRecord?> GetByIdAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken)
    {
        return await _dbContext.EventModerationRecords
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .Where(record => record.TenantId == tenantId && record.Id == id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EventModerationRecord>> GetByEventAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.EventModerationRecords
            .AsNoTracking()
            .Where(record => record.TenantId == tenantId && record.EventId == eventId)
            .OrderByDescending(record => record.CreatedAt)
            .ThenByDescending(record => record.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<EventModerationRecord?> GetLatestByEventAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.EventModerationRecords
            .AsNoTracking()
            .Where(record => record.TenantId == tenantId && record.EventId == eventId)
            .OrderByDescending(record => record.CreatedAt)
            .ThenByDescending(record => record.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<EventModerationRecord?> GetBySourceReportDecisionAsync(
        Guid tenantId,
        Guid reportId,
        Guid decisionId,
        CancellationToken cancellationToken)
    {
        return _dbContext.EventModerationRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(record =>
                record.TenantId == tenantId
                && record.SourceReportId == reportId
                && record.SourceReportDecisionId == decisionId,
                cancellationToken);
    }
}
