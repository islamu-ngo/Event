namespace Explore.Persistence.Repositories;

using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

public class FooterLinkGroupRepository : GenericRepository<TenantFooterLinkGroup, Guid>, IFooterLinkGroupRepository
{
    private readonly ExploreDbContext _dbContext;
    private readonly IFooterOperationExecution? _execution;

    public FooterLinkGroupRepository(ExploreDbContext dbContext, IFooterOperationExecution? execution = null) : base(dbContext)
    {
        _dbContext = dbContext;
        _execution = execution;
    }

    public override async Task<TenantFooterLinkGroup> Create(TenantFooterLinkGroup entity)
    {
        await RequireWriteAsync(entity, FooterWriteKind.Create);
        return await base.Create(entity);
    }

    public override async Task Update(TenantFooterLinkGroup entity)
    {
        await RequireWriteAsync(entity, FooterWriteKind.Update);
        await base.Update(entity);
    }

    public override async Task Delete(TenantFooterLinkGroup entity)
    {
        await RequireWriteAsync(entity, FooterWriteKind.Delete);
        await base.Delete(entity);
    }

    private Task RequireWriteAsync(TenantFooterLinkGroup entity, FooterWriteKind kind) =>
        (_execution ?? throw new UnauthorizedAccessException("Footer writes require their actual operation producer."))
        .RequireWriteAsync(entity, kind);

    public async Task<TenantFooterLinkGroup?> GetForMutationAsync(Guid id, CancellationToken ct = default)
    {
        if (_dbContext.TenantFilterTenantId is not { } tenantId || tenantId == Guid.Empty
            || _dbContext.IsTenantFilterBypassed
            || _dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Footer mutation requires the exact tenant Serializable transaction.");
        var candidate = await _dbContext.TenantFooterLinkGroups.AsNoTracking()
            .SingleOrDefaultAsync(group => group.Id == id && group.TenantId == tenantId, ct);
        if (candidate is null)
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<TenantFooterLinkGroup>(_dbContext, id, ct);
        var current = await _dbContext.TenantFooterLinkGroups.AsNoTracking()
            .SingleOrDefaultAsync(group => group.Id == id && group.TenantId == tenantId, ct);
        var tracked = _dbContext.ChangeTracker.Entries<TenantFooterLinkGroup>()
            .SingleOrDefault(entry => entry.Entity.Id == id);
        if (tracked is not null && current is not null && tracked.Entity.TenantId != current.TenantId)
            throw new InvalidOperationException("Tracked footer ownership must match the persisted group.");
        if (tracked is not null)
            tracked.State = EntityState.Detached;
        return current;
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
