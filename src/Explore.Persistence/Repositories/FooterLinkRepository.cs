namespace Explore.Persistence.Repositories;

using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

public class FooterLinkRepository : GenericRepository<TenantFooterLink, Guid>, IFooterLinkRepository
{
    private readonly ExploreDbContext _dbContext;
    private readonly IFooterOperationExecution? _execution;

    public FooterLinkRepository(ExploreDbContext dbContext, IFooterOperationExecution? execution = null) : base(dbContext)
    {
        _dbContext = dbContext;
        _execution = execution;
    }

    public override async Task<TenantFooterLink> Create(TenantFooterLink entity)
    {
        await RequireWriteAsync(entity, FooterWriteKind.Create);
        return await base.Create(entity);
    }

    public override async Task Update(TenantFooterLink entity)
    {
        await RequireWriteAsync(entity, FooterWriteKind.Update);
        await base.Update(entity);
    }

    public override async Task Delete(TenantFooterLink entity)
    {
        await RequireWriteAsync(entity, FooterWriteKind.Delete);
        await base.Delete(entity);
    }

    private Task RequireWriteAsync(TenantFooterLink entity, FooterWriteKind kind) =>
        (_execution ?? throw new UnauthorizedAccessException("Footer writes require their actual operation producer."))
        .RequireWriteAsync(entity, kind);

    public Task<TenantFooterLink?> GetByIdForTenantAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default) =>
        _dbContext.TenantFooterLinks
            .Include(link => link.Group)
            .FirstOrDefaultAsync(
                link => link.Id == id && link.Group != null && link.Group.TenantId == tenantId,
                ct);

    public async Task<List<TenantFooterLink>> GetByGroupIdAsync(
        Guid groupId, CancellationToken ct = default)
    {
        return await _dbContext.TenantFooterLinks
            .AsNoTracking()
            .Where(l => l.FooterLinkGroupId == groupId && l.IsActive)
            .OrderBy(l => l.Order)
            .ToListAsync(ct);
    }

    public async Task<int> GetMaxOrderInGroupAsync(
        Guid groupId, CancellationToken ct = default)
    {
        var maxOrder = await _dbContext.TenantFooterLinks
            .Where(l => l.FooterLinkGroupId == groupId)
            .MaxAsync(l => (int?)l.Order, ct);

        return maxOrder ?? 0;
    }

    public async Task<TenantFooterLink?> GetForMutationAsync(Guid id, Guid groupId, CancellationToken ct = default)
    {
        if (_dbContext.TenantFilterTenantId is not { } tenantId || tenantId == Guid.Empty
            || _dbContext.IsTenantFilterBypassed
            || _dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Footer link mutation requires the exact tenant Serializable transaction.");
        var planned = await _dbContext.TenantFooterLinks.AsNoTracking()
            .Where(link => link.Id == id && link.FooterLinkGroupId == groupId && link.Group != null && link.Group.TenantId == tenantId)
            .Select(link => new { link.FooterLinkGroupId }).SingleOrDefaultAsync(ct);
        if (planned is null || await new FooterLinkGroupRepository(_dbContext)
                .GetForMutationAsync(planned.FooterLinkGroupId, ct) is null)
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<TenantFooterLink>(_dbContext, id, ct);
        var current = await _dbContext.TenantFooterLinks.AsNoTracking()
            .SingleOrDefaultAsync(link => link.Id == id && link.FooterLinkGroupId == planned.FooterLinkGroupId
                && link.Group != null && link.Group.TenantId == tenantId, ct);
        var tracked = _dbContext.ChangeTracker.Entries<TenantFooterLink>().SingleOrDefault(entry => entry.Entity.Id == id);
        if (tracked is not null && current is not null && tracked.Entity.FooterLinkGroupId != current.FooterLinkGroupId)
            throw new InvalidOperationException("Tracked footer link ownership must match its persisted group.");
        if (tracked is not null)
            tracked.State = EntityState.Detached;
        return current;
    }

    public async Task FenceByGroupIdAsync(Guid groupId, CancellationToken ct = default)
    {
        if (_dbContext.TenantFilterTenantId is not { } tenantId || tenantId == Guid.Empty
            || _dbContext.IsTenantFilterBypassed
            || _dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Footer link fences require the exact tenant Serializable transaction.");
        await _dbContext.TenantFooterLinks
            .Where(link => link.FooterLinkGroupId == groupId && link.Group != null && link.Group.TenantId == tenantId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(link => link.Id, link => link.Id), ct);
    }

    public async Task DeleteByGroupIdAsync(
        Guid groupId, CancellationToken ct = default)
    {
        if (_dbContext.TenantFilterTenantId is not { } tenantId || tenantId == Guid.Empty
            || _dbContext.IsTenantFilterBypassed
            || _dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Footer deletion requires the exact tenant Serializable transaction.");
        await (_execution ?? throw new UnauthorizedAccessException("Footer deletion requires its actual operation producer."))
            .RequireGroupDeletionAsync(groupId, ct);
        var owned = await _dbContext.TenantFooterLinkGroups.AsNoTracking()
            .AnyAsync(group => group.Id == groupId && group.TenantId == tenantId, ct);
        if (!owned)
            throw new InvalidOperationException("Footer deletion requires its persisted tenant-owned group.");
        await RelationalEntityRowFence.AcquireGlobalAsync<TenantFooterLinkGroup>(_dbContext, groupId, ct);
        var tracked = _dbContext.ChangeTracker.Entries<TenantFooterLink>().ToArray();
        var trackedIds = tracked.Select(entry => entry.Entity.Id).ToArray();
        Guid[] deletedTrackedIds = [];
        if (trackedIds.Length > 0)
        {
            deletedTrackedIds = await _dbContext.TenantFooterLinks.AsNoTracking()
                .Where(link => link.FooterLinkGroupId == groupId && link.Group != null
                    && link.Group.TenantId == tenantId && trackedIds.Contains(link.Id))
                .Select(link => link.Id).ToArrayAsync(ct);
            if (tracked.Any(entry => deletedTrackedIds.Contains(entry.Entity.Id)
                    && entry.Entity.FooterLinkGroupId != groupId))
                throw new InvalidOperationException("Tracked footer link ownership must match its persisted group.");
        }
        await _dbContext.TenantFooterLinks
            .Where(l => l.FooterLinkGroupId == groupId && l.Group != null && l.Group.TenantId == tenantId)
            .ExecuteDeleteAsync(ct);
        // Bulk deletion bypasses tracking. Do not let the subsequent parent cascade
        // issue another delete for these rows, or discard unrelated tracked links.
        foreach (var entry in tracked.Where(entry => deletedTrackedIds.Contains(entry.Entity.Id)))
            entry.State = EntityState.Detached;
    }
}
