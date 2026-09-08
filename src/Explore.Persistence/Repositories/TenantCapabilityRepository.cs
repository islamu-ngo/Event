// data access for tenant module capabilities and governance.

using Explore.Application.Contracts.Persistence;
using Explore.Domain.Modules;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class TenantCapabilityRepository : GenericRepository<TenantCapability, Guid>, ITenantCapabilityRepository
{
    private readonly ExploreDbContext _dbContext;

    public TenantCapabilityRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<TenantCapability>> GetByTenantId(Guid tenantId)
    {
        return await _dbContext.TenantCapabilities
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantCapabilityResolution)
            .AsNoTracking()
            .Include(c => c.Module)
            .Where(c => c.TenantId == tenantId)
            .ToListAsync();
    }

    public async Task<List<TenantCapability>> GetEnabledByTenantId(Guid tenantId)
    {
        return await _dbContext.TenantCapabilities
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantCapabilityResolution)
            .AsNoTracking()
            .Include(c => c.Module)
            .Where(c => c.TenantId == tenantId && c.IsEnabled && c.Module != null && c.Module.IsActive)
            .OrderBy(c => c.Module!.DisplayOrder)
            .ToListAsync();
    }

    public async Task<bool> IsModuleEnabled(Guid tenantId, string moduleKey)
    {
        return await _dbContext.TenantCapabilities
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantCapabilityResolution)
            .AsNoTracking()
            .Include(c => c.Module)
            .AnyAsync(c => c.TenantId == tenantId
                && c.IsEnabled
                && c.Module != null
                && c.Module.ModuleKey == moduleKey
                && c.Module.IsActive);
    }

    public async Task<TenantCapability?> GetByTenantAndModuleKey(
        Guid tenantId,
        string moduleKey,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.TenantCapabilities
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantCapabilityResolution)
            .Include(c => c.Module)
            .FirstOrDefaultAsync(
                c => c.TenantId == tenantId && c.Module != null && c.Module.ModuleKey == moduleKey,
                cancellationToken);
    }

    public async Task<TenantCapability> Create(
        TenantCapability entity,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.TenantCapabilities.AddAsync(entity, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task Update(TenantCapability entity, CancellationToken cancellationToken = default)
    {
        var trackedEntity = _dbContext.TenantCapabilities.Local
            .FirstOrDefault(candidate => candidate.Id == entity.Id);

        if (trackedEntity is not null && !ReferenceEquals(trackedEntity, entity))
        {
            _dbContext.Entry(trackedEntity).CurrentValues.SetValues(entity);
            _dbContext.Entry(trackedEntity).State = EntityState.Modified;
        }
        else
        {
            _dbContext.Entry(entity).State = EntityState.Modified;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
