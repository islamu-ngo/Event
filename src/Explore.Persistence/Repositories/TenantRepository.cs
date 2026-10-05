using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Interfaces;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class TenantRepository : GenericRepository<Tenant, Guid>, ITenantRepository
{
    private readonly ExploreDbContext _dbContext;

    public TenantRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public override async Task Delete(Tenant entity)
    {
        await new EfCoreUnitOfWork(_dbContext).ExecuteSerializableAsync(async cancellationToken =>
        {
            // Captures and retention take reservation before source/epoch locks.
            // Hold it before Tenant so neither can race retirement or invert that order.
            await EventDiscoveryDisclosureRepository.InTenantAsync(_dbContext, entity.Id, async () =>
            {
                await EventDiscoverySnapshotProviderOperations.BootstrapReservationAsync(
                    _dbContext, entity.Id, cancellationToken);
                await RelationalEntityRowFence.AcquireGlobalAsync<EventDiscoverySnapshotReservation>(
                    _dbContext, entity.Id, cancellationToken);
                return true;
            }, cancellationToken);
            _dbContext.DisclosureMutations.Enlist([entity.Id]);
            await _dbContext.DisclosureMutations.CaptureAsync(cancellationToken);
            // Unloaded series are physically removed by the database cascade, not tracked saves.
            Guid[] pictureIds = await _dbContext.EventSeries.AsNoTracking()
                .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                .Where(series => series.TenantId == entity.Id && series.FeaturedImageId.HasValue)
                .Select(series => series.FeaturedImageId!.Value).Distinct()
                .ToArrayAsync(cancellationToken);
            await new StorageObjectReferenceRepository(_dbContext).FenceAsync(pictureIds, cancellationToken);
            await EventDiscoveryDisclosureRepository.InTenantAsync(_dbContext, entity.Id, async () =>
            {
                // Tenant fences identity/epoch writers; reservation fences capture and
                // retention. These are owned ephemeral rows, not their shared sources.
                // Do not acquire an early terminal epoch: the true transaction owner
                // may still have source writes and other tenants to finalize.
                await DeleteDiscoveryAsync<EventDiscoveryAlias>();
                await DeleteDiscoveryAsync<EventDiscoveryIdentity>();
                await DeleteDiscoveryAsync<EventDiscoverySnapshotItem>();
                await DeleteDiscoveryAsync<EventDiscoverySnapshot>();
                await DeleteDiscoveryAsync<EventDiscoveryRevision>();
                await DeleteDiscoveryAsync<EventDiscoverySnapshotReservation>();
                return true;
            }, cancellationToken);
            foreach (var entry in _dbContext.ChangeTracker.Entries()
                         .Where(entry => entry.Entity is ITenantEntity owned && owned.TenantId == entity.Id
                             && entry.Entity is EventDiscoveryAlias or EventDiscoveryIdentity or EventDiscoveryRevision
                                 or EventDiscoverySnapshotItem or EventDiscoverySnapshot or EventDiscoverySnapshotReservation)
                         .ToArray())
                entry.State = EntityState.Detached;
            await base.Delete(entity);
            _dbContext.DisclosureMutations.RecordTenantDeletion(entity.Id);
            return true;

            Task<int> DeleteDiscoveryAsync<TEntity>() where TEntity : class, ITenantEntity =>
                _dbContext.Set<TEntity>().IgnoreAllFilters(TenantFilterBypassReasons.DiscoveryDisclosureMutation)
                    .Where(row => row.TenantId == entity.Id).ExecuteDeleteAsync(cancellationToken);
        }, CancellationToken.None);
    }

    public async Task<Tenant?> GetTenantBySlug(string slug)
    {
        return await _dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Slug == slug);
    }

    public async Task<IReadOnlyList<Tenant>> GetBySlugsAsNoTrackingAsync(
        IReadOnlyCollection<string> slugs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slugs);
        if (slugs.Count == 0)
        {
            return [];
        }

        string[] normalizedSlugs = slugs
            .Select(slug => slug?.Trim())
            .Where(slug => !string.IsNullOrEmpty(slug))
            .Distinct(StringComparer.Ordinal)
            .Cast<string>()
            .ToArray();
        if (normalizedSlugs.Length != slugs.Count)
        {
            throw new ArgumentException(
                "Tenant slug batches must contain unique non-empty values.",
                nameof(slugs));
        }

        return await _dbContext.Tenants
            .AsNoTracking()
            .Where(tenant => normalizedSlugs.Contains(tenant.Slug))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetActiveTenantCountAsync()
    {
        return await _dbContext.Tenants
            .AsNoTracking()
            .CountAsync(t => t.TenantStatus.IsActiveState);
    }

    public async Task<IReadOnlyList<Tenant>> GetActiveAsNoTrackingAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.TenantStatus.IsActiveState)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Tenant>>
        GetAllActiveForConfigurationManifestExportAsync(
            int maximumCount,
            CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCount);

        return await _dbContext.Tenants
            .IgnoreTenantFilter(
                TenantFilterBypassReasons.InstanceConfigurationManifestExport)
            .AsNoTracking()
            .Where(tenant => tenant.TenantStatus.IsActiveState)
            .OrderBy(tenant => tenant.Slug)
            .ThenBy(tenant => tenant.Id)
            .Take(maximumCount)
            .ToListAsync(cancellationToken);
    }

    public Task<Tenant?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken cancellationToken = default) =>
        _dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(tenant => tenant.Id == id, cancellationToken);

    public async Task<bool> TryTransitionStatusAsync(
        Guid id,
        int expectedStatusId,
        int newStatusId,
        DateTime updatedAt,
        Guid updatedBy,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ExecuteDisclosureMutationAsync(async token =>
        {
            _dbContext.DisclosureMutations.Enlist([]);
            var affectedRows = await _dbContext.Tenants
                .Where(tenant => tenant.Id == id && tenant.TenantStatusId == expectedStatusId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(tenant => tenant.TenantStatusId, newStatusId)
                    .SetProperty(tenant => tenant.UpdatedAt, updatedAt)
                    .SetProperty(tenant => tenant.UpdatedBy, updatedBy), token);

            if (affectedRows == 1)
                _dbContext.DisclosureMutations.Enlist([id]);
            return affectedRows == 1;
        }, cancellationToken);
    }
}
