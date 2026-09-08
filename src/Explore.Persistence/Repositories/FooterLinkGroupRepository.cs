namespace Explore.Persistence.Repositories;

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

public class FooterLinkGroupRepository : GenericRepository<TenantFooterLinkGroup, Guid>, IFooterLinkGroupRepository
{
    private readonly ExploreDbContext _dbContext;

    public FooterLinkGroupRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<TenantFooterLinkGroup>> GetResolvedGroupsForTenantAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        // Try tenant-owned groups first
        var tenantGroups = await _dbContext.TenantFooterLinkGroups
            .AsNoTracking()
            .Where(g => g.TenantId == tenantId && g.IsActive)
            .Include(g => g.Links.Where(l => l.IsActive).OrderBy(l => l.Order))
            .OrderBy(g => g.Order)
            .ToListAsync(ct);

        if (tenantGroups.Count > 0)
            return tenantGroups;

        // Fall back to instance-default groups (TenantId == null)
        return await _dbContext.TenantFooterLinkGroups
            .AsNoTracking()
            .Where(g => g.TenantId == null && g.IsActive)
            .Include(g => g.Links.Where(l => l.IsActive).OrderBy(l => l.Order))
            .OrderBy(g => g.Order)
            .ToListAsync(ct);
    }

    public async Task<List<TenantFooterLinkGroup>> GetByTenantIdAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        return await _dbContext.TenantFooterLinkGroups
            .AsNoTracking()
            .Where(g => g.TenantId == tenantId)
            .OrderBy(g => g.Order)
            .ToListAsync(ct);
    }

    public async Task<List<TenantFooterLinkGroup>> GetInstanceDefaultGroupsAsync(
        CancellationToken ct = default)
    {
        return await _dbContext.TenantFooterLinkGroups
            .AsNoTracking()
            .Where(g => g.TenantId == null)
            .Include(g => g.Links.OrderBy(l => l.Order))
            .OrderBy(g => g.Order)
            .ToListAsync(ct);
    }

    public async Task<TenantFooterLinkGroup?> GetWithLinksAsync(
        Guid id, CancellationToken ct = default)
    {
        return await _dbContext.TenantFooterLinkGroups
            .Include(g => g.Links.OrderBy(l => l.Order))
            .FirstOrDefaultAsync(g => g.Id == id, ct);
    }

    public async Task<int> GetMaxOrderAsync(Guid? tenantId, CancellationToken ct = default)
    {
        var maxOrder = await _dbContext.TenantFooterLinkGroups
            .Where(g => g.TenantId == tenantId)
            .MaxAsync(g => (int?)g.Order, ct);

        return maxOrder ?? 0;
    }
}
