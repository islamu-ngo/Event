using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class TenantUserRoleGrantRepository : GenericRepository<TenantUserRoleGrant, Guid>, ITenantUserRoleGrantRepository
{
    private readonly ExploreDbContext _dbContext;

    public TenantUserRoleGrantRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TenantUserRoleGrant?> GetByTenantAndUser(Guid tenantId, Guid userId)
    {
        return await _dbContext.TenantUserRoleGrants
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Include(x => x.TenantUser)
            .Include(x => x.Role)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId
                && x.TenantUser.UserId == userId
                && x.RevokedAt == null);
    }

    public async Task<TenantUserRoleGrant?> GetByTenantUserAndRole(Guid tenantId, Guid tenantUserId, int roleId)
    {
        return await _dbContext.TenantUserRoleGrants
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Include(x => x.Role)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId
                && x.TenantUserId == tenantUserId
                && x.RoleId == roleId
                && x.RevokedAt == null);
    }

    public async Task<List<TenantUserRoleGrant>> GetByTenant(Guid tenantId)
    {
        return await _dbContext.TenantUserRoleGrants
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .Include(x => x.TenantUser)
                .ThenInclude(tu => tu.User)
                    .ThenInclude(u => u!.Pii)
            .Include(x => x.Role)
            .Where(x => x.TenantId == tenantId && x.RevokedAt == null)
            .ToListAsync();
    }

    public async Task<List<TenantUserRoleGrant>> GetByUserId(Guid userId)
    {
        return await _dbContext.TenantUserRoleGrants
            .IgnoreTenantFilter(TenantFilterBypassReasons.UserTenantMembershipEnumeration)
            .AsNoTracking()
            .Include(x => x.Tenant)
            .Include(x => x.TenantUser)
            .Include(x => x.Role)
            .Where(x => x.TenantUser.UserId == userId && x.RevokedAt == null)
            .ToListAsync();
    }

    public async Task<bool> HasActiveTenantUserRoleGrant(Guid tenantId, Guid userId)
    {
        return await _dbContext.TenantUserRoleGrants
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId
                && x.RevokedAt == null
                && x.TenantUser.UserId == userId
                && x.TenantUser.StatusId == (int)TenantUserStatusEnum.Active
                && !x.TenantUser.IsDeleted);
    }

    public async Task<bool> IsTenantAdmin(Guid tenantId, Guid userId)
    {
        return await _dbContext.TenantUserRoleGrants
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId
                && x.RevokedAt == null
                && x.TenantUser.UserId == userId
                && x.RoleId == (int)RoleEnum.TenantAdmin
                && x.TenantUser.StatusId == (int)TenantUserStatusEnum.Active
                && !x.TenantUser.IsDeleted);
    }

    public async Task<bool> IsTenantAdminInCurrentTenantAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (_dbContext.TenantFilterTenantId != tenantId || _dbContext.IsTenantFilterBypassed)
        {
            throw new InvalidOperationException("Tenant-admin revalidation requires the matching active tenant filter.");
        }

        return await _dbContext.TenantUserRoleGrants
            .AsNoTracking()
            .AnyAsync(grant => grant.TenantId == tenantId
                && grant.RevokedAt == null
                && grant.TenantUser.UserId == userId
                && grant.RoleId == (int)RoleEnum.TenantAdmin
                && grant.TenantUser.StatusId == (int)TenantUserStatusEnum.Active
                && !grant.TenantUser.IsDeleted,
                cancellationToken);
    }

    public async Task<TenantUserRoleGrant?> GetGrantWithDetails(Guid id)
    {
        return await _dbContext.TenantUserRoleGrants
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.TenantUser)
                .ThenInclude(tu => tu.User)
                    .ThenInclude(u => u!.Pii)
            .Include(x => x.Tenant)
            .Include(x => x.Role)
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<List<TenantUserRoleGrant>> GetGrantsWithDetails()
    {
        return await _dbContext.TenantUserRoleGrants
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.TenantUser)
                .ThenInclude(tu => tu.User)
                    .ThenInclude(u => u!.Pii)
            .Include(x => x.Tenant)
            .Include(x => x.Role)
            .ToListAsync();
    }
}
