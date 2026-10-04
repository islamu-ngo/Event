using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
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

        // Soft deletion is not custody transfer. Resource staging has not acquired
        // a usage charge until activation or a finalized session proves it.
        var objects = _dbContext.StorageObjects
            .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete]).AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.Provider == provider
                && row.Provider != StorageProviders.LegacyExternal
                && !_dbContext.StorageObjectDeletionTombstones.Any(authority => authority.Id == row.Id)
                && (row.Purpose != StorageObjectPurposes.EventResource
                    || row.LifecycleState == StorageObjectLifecycleStates.Active
                    || row.LifecycleState == StorageObjectLifecycleStates.Quarantined
                    || _dbContext.StorageUploadSessions.IgnoreQueryFilters(new[] { QueryFilterNames.Tenant })
                        .Any(session => session.TenantId == tenantId && session.StorageObjectId == row.Id
                            && session.Status == StorageUploadSessionStates.Finalized)));
        var totals = await objects.GroupBy(_ => 1).Select(group => new
        {
            Used = group.Sum(row => row.LifecycleState == StorageObjectLifecycleStates.Quarantined ? 0L : row.Size),
            Quarantined = group.Sum(row => row.LifecycleState == StorageObjectLifecycleStates.Quarantined ? row.Size : 0L),
            Count = group.LongCount()
        }).SingleOrDefaultAsync(cancellationToken);
        long reserved = await _dbContext.StorageUploadSessions.IgnoreQueryFilters([QueryFilterNames.Tenant])
            .AsNoTracking().Where(session => session.TenantId == tenantId && session.Provider == provider
                && (session.Status == StorageUploadSessionStates.Reserved
                    || session.Status == StorageUploadSessionStates.Uploading))
            .SumAsync(session => (long?)session.ReservedBytes, cancellationToken) ?? 0;
        counter.Recalculate(totals?.Used ?? 0, reserved, totals?.Quarantined ?? 0, totals?.Count ?? 0, utcNow);
        return counter;
    }

    public async Task<IReadOnlyList<StorageUsageCounter>> GetAllTrackedForInstanceStorageRecalculationAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.StorageUsageCounters
            .IgnoreTenantFilter(TenantFilterBypassReasons.InstanceStorageAdministration)
            .ToListAsync(cancellationToken);
    }
}
