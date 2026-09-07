using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class StorageUsageCounterRepository : GenericRepository<StorageUsageCounter, Guid>, IStorageUsageCounterRepository
{
    private readonly ExploreDbContext _dbContext;

    public StorageUsageCounterRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<StorageUsageCounter?> GetByTenantAndProviderAsync(
        Guid tenantId,
        string provider,
        CancellationToken cancellationToken)
    {
        return await _dbContext.StorageUsageCounters
            .AsNoTracking()
            .SingleOrDefaultAsync(counter =>
                    counter.TenantId == tenantId &&
                    counter.Provider == provider,
                cancellationToken);
    }


    public async Task<IReadOnlyList<StorageUsageCounter>> GetByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.StorageUsageCounters
            .AsNoTracking()
            .Where(counter => counter.TenantId == tenantId)
            .ToListAsync(cancellationToken);
    }

    public async Task<StorageUsageCounter> GetOrCreateAsync(
        Guid tenantId,
        string provider,
        CancellationToken cancellationToken)
    {
        var counter = await _dbContext.StorageUsageCounters
            .SingleOrDefaultAsync(existing =>
                    existing.TenantId == tenantId &&
                    existing.Provider == provider,
                cancellationToken);

        if (counter is not null)
        {
            return counter;
        }

        counter = new StorageUsageCounter
        {
            TenantId = tenantId,
            Provider = provider
        };

        await _dbContext.StorageUsageCounters.AddAsync(counter, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return counter;
    }

    public async Task<IReadOnlyList<StorageUsageCounter>> GetAllForInstanceStorageReportAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.StorageUsageCounters
            .AsNoTracking()
            .IgnoreTenantFilter(TenantFilterBypassReasons.InstanceStorageAdministration)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StorageUsageCounter>> GetAllTrackedForInstanceStorageRecalculationAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.StorageUsageCounters
            .IgnoreTenantFilter(TenantFilterBypassReasons.InstanceStorageAdministration)
            .ToListAsync(cancellationToken);
    }
}
