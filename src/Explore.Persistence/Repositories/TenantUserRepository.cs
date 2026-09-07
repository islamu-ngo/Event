using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class TenantUserRepository : GenericRepository<TenantUser, Guid>, ITenantUserRepository
{
    private readonly ExploreDbContext _dbContext;

    public TenantUserRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TenantUser?> GetByTenantAndUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.TenantUsers
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Include(x => x.Profile)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId, cancellationToken);
    }

    public async Task<TenantUser?> GetByTenantAndActorAsync(Guid tenantId, Guid actorId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.TenantUsers
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .Include(x => x.Profile)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ActorId == actorId, cancellationToken);
    }

    public async Task<bool> IsActiveTenantUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.TenantUsers
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId
                && x.UserId == userId
                && x.StatusId == (int)TenantUserStatusEnum.Active
                && !x.IsDeleted, cancellationToken);
    }

    public async Task<List<TenantUser>> GetActiveTenantsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.TenantUsers
            .IgnoreTenantFilter(TenantFilterBypassReasons.UserTenantMembershipEnumeration)
            .Include(x => x.Tenant)
                .ThenInclude(t => t.TenantStatus)
            .Where(x => x.UserId == userId
                && x.StatusId == (int)TenantUserStatusEnum.Active
                && !x.IsDeleted
                && x.Tenant.TenantStatusId == (int)TenantStatusEnum.Active)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryRemoveMembershipAsync(
        Guid tenantId,
        Guid userId,
        Guid removedBy,
        DateTime removedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty || removedBy == Guid.Empty)
        {
            throw new ArgumentException("Tenant, user, and removal actor identifiers are required.");
        }

        if (_dbContext.TenantFilterTenantId != tenantId || _dbContext.IsTenantFilterBypassed)
        {
            throw new InvalidOperationException("Tenant membership removal requires the matching active tenant filter.");
        }

        var membershipId = await _dbContext.TenantUsers
            .AsNoTracking()
            .Where(membership => membership.TenantId == tenantId
                && membership.UserId == userId
                && membership.StatusId != (int)TenantUserStatusEnum.Removed
                && !membership.IsDeleted)
            .Select(membership => membership.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (membershipId == Guid.Empty)
        {
            return false;
        }

        var removedAt = removedAtUtc.ToUniversalTime();
        var claimed = await _dbContext.TenantUsers
            .Where(membership => membership.Id == membershipId
                && membership.TenantId == tenantId
                && membership.UserId == userId
                && membership.StatusId != (int)TenantUserStatusEnum.Removed
                && !membership.IsDeleted)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(membership => membership.StatusId, (int)TenantUserStatusEnum.Removed)
                .SetProperty(membership => membership.RemovedAt, removedAt)
                .SetProperty(membership => membership.RemovedBy, removedBy)
                .SetProperty(membership => membership.IsDeleted, true)
                .SetProperty(membership => membership.DeletedAt, removedAt)
                .SetProperty(membership => membership.DeletedBy, removedBy)
                .SetProperty(membership => membership.UpdatedAt, removedAt)
                .SetProperty(membership => membership.UpdatedBy, removedBy),
                cancellationToken);
        if (claimed == 0)
        {
            return false;
        }

        await _dbContext.TenantUserProfiles
            .Where(profile => profile.TenantId == tenantId && profile.TenantUserId == membershipId)
            .ExecuteDeleteAsync(cancellationToken);

        await _dbContext.TenantUserRoleGrants
            .Where(grant => grant.TenantId == tenantId
                && grant.TenantUserId == membershipId
                && grant.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(grant => grant.RevokedAt, removedAt)
                .SetProperty(grant => grant.RevokedBy, removedBy)
                .SetProperty(grant => grant.UpdatedAt, removedAt)
                .SetProperty(grant => grant.UpdatedBy, removedBy),
                cancellationToken);

        return true;
    }
}
