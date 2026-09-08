using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class AuditLogRepository : GenericRepository<AuditLog, Guid>, IAuditLogRepository
{
    private readonly ExploreDbContext _dbContext;

    public AuditLogRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<(IReadOnlyList<AuditLog> Items, int TotalCount)> GetTemplateSyncHistoryAsync(
        string entityType,
        string entityId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        IQueryable<AuditLog> query = _dbContext.Set<AuditLog>()
            .AsNoTracking()
            .Where(x => x.EntityType == entityType && x.EntityId == entityId && x.Action == "TemplateSyncApplied")
            .OrderByDescending(x => x.Timestamp);

        int totalCount = await query.CountAsync(cancellationToken);
        List<AuditLog> items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
