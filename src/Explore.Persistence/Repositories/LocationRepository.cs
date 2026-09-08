using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class LocationRepository : GenericRepository<Location, Guid>, ILocationRepository
{
    private readonly ExploreDbContext _dbContext;

    public LocationRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Location> Create(Location location, CancellationToken cancellationToken)
    {
        await _dbContext.Locations.AddAsync(location, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return location;
    }

    public new async Task<Location?> GetById(Guid id)
    {
        return await _dbContext.Locations
            .Include(l => l.Pii)
            .FirstOrDefaultAsync(l => l.Id == id);
    }

    public async Task<Location?> GetById(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.Locations
            .Include(location => location.Pii)
            .FirstOrDefaultAsync(location => location.Id == id, cancellationToken);
    }

    public override async Task Update(Location entity)
    {
        try
        {
            await base.Update(entity);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "The location was modified by another request. Reload and retry.",
                nameof(Location),
                innerException: exception);
        }
    }

    public async Task Update(Location location, CancellationToken cancellationToken)
    {
        try
        {
            var entry = _dbContext.Entry(location);
            if (entry.State != EntityState.Detached)
            {
                entry.State = EntityState.Modified;
            }
            else
            {
                Location? trackedLocation = _dbContext.Locations.Local
                    .FirstOrDefault(tracked => tracked.Id == location.Id);
                if (trackedLocation is not null)
                {
                    _dbContext.Entry(trackedLocation).CurrentValues.SetValues(location);
                    _dbContext.Entry(trackedLocation).State = EntityState.Modified;
                }
                else
                {
                    entry.State = EntityState.Modified;
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "The location was modified by another request. Reload and retry.",
                nameof(Location),
                innerException: exception);
        }
    }

    public async Task<List<Location>> GetLocationsByTenant(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Locations
            .AsNoTracking()
            .Include(l => l.Pii)
            .Where(l => l.TenantId == tenantId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetExistingTenantLocationIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> candidateLocationIds,
        CancellationToken cancellationToken = default)
    {
        var boundedLocationIds = candidateLocationIds
            .Where(locationId => locationId != Guid.Empty)
            .Distinct()
            .ToArray();
        if (boundedLocationIds.Length == 0)
            return [];

        return await _dbContext.Locations
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(location => location.TenantId == tenantId && boundedLocationIds.Contains(location.Id))
            .Select(location => location.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Location>> GetOwnedPrivateHomesForGlobalErasureAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken = default)
    {
        if (ownerUserId == Guid.Empty)
        {
            throw new ArgumentException("Owner user id is required.", nameof(ownerUserId));
        }

        return await _dbContext.Locations
            .IgnoreTenantFilter(TenantFilterBypassReasons.UserPrivacyErasure)
            .Where(location => location.OwnerUserId == ownerUserId
                && location.LocationKindId == (int)LocationKindEnum.PrivateHome)
            .OrderBy(location => location.TenantId)
            .ThenBy(location => location.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<(List<Location> Items, int TotalCount)> GetLocationsWithDetailsPaged(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Locations
            .AsNoTracking()
            .Include(l => l.Pii)
            .OrderBy(l => l.FullName);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<int> ForgetPiiAsync(Guid locationId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.LocationPii
            .Where(p => p.LocationId == locationId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
