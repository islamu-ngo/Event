using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
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

    public async Task RecalculateForInstanceStorageAdministrationAsync(
        Guid tenantId, string provider, DateTime utcNow, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || _dbContext.TenantFilterTenantId != tenantId)
            throw new InvalidOperationException("Instance accounting repair requires an exact tenant lease.");
        await RecalculateScopeAsync(tenantId, provider, utcNow, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<StorageUsageCounter> RecalculateScopeAsync(
        Guid tenantId, string provider, DateTime utcNow, CancellationToken cancellationToken)
    {
        if (_dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Storage usage projection requires the caller's transaction.");
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Storage usage projection time must be UTC.", nameof(utcNow));

        var counter = await _dbContext.StorageUsageCounters.IgnoreQueryFilters([QueryFilterNames.Tenant])
            .SingleOrDefaultAsync(row => row.TenantId == tenantId && row.Provider == provider, cancellationToken);
        if (counter is null)
        {
            counter = new StorageUsageCounter { TenantId = tenantId, Provider = provider };
            _dbContext.Add(counter);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        var stampProperty = _dbContext.Entry(counter).Property(row => row.ConcurrencyStamp);
        Guid expected = stampProperty.OriginalValue;
        Guid stamp = Guid.CreateVersion7();
        int changed = await _dbContext.StorageUsageCounters.IgnoreQueryFilters([QueryFilterNames.Tenant])
            .Where(row => row.Id == counter.Id && row.TenantId == tenantId && row.Provider == provider
                && row.ConcurrencyStamp == expected)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConcurrencyStamp, stamp), cancellationToken);
        if (changed != 1)
            throw new ConcurrencyConflictException(ConcurrencyConflictException.ConcurrentUpdate,
                "Storage usage authority changed concurrently.");
        stampProperty.CurrentValue = stamp;
        stampProperty.OriginalValue = stamp;
        stampProperty.IsModified = false;

        var totals = await StorageScopeTotalsQuery.ReadAsync(_dbContext, tenantId, provider, cancellationToken);
        counter.Recalculate(totals.Used, totals.Reserved, totals.Quarantined, totals.Count, utcNow);
        return counter;
    }

}
