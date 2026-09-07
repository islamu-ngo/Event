namespace Explore.Persistence.Repositories;

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

public class FooterLinkRepository : GenericRepository<TenantFooterLink, Guid>, IFooterLinkRepository
{
    private readonly ExploreDbContext _dbContext;

    public FooterLinkRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

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

    public async Task DeleteByGroupIdAsync(
        Guid groupId, CancellationToken ct = default)
    {
        await _dbContext.TenantFooterLinks
            .Where(l => l.FooterLinkGroupId == groupId)
            .ExecuteDeleteAsync(ct);
    }
}
