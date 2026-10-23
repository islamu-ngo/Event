using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class TenantUserRepository : GenericRepository<TenantUser, Guid>, ITenantUserRepository
{
    private readonly ExploreDbContext _dbContext;
    private readonly PostgresNativeQueueEnrollment? _enrollment;

    public TenantUserRepository(ExploreDbContext dbContext,
        PostgresNativeQueueEnrollment? enrollment = null) : base(dbContext)
    {
        _dbContext = dbContext;
        _enrollment = enrollment;
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

    public async Task<bool> FenceActiveTenantUserAsync(
        Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        await RelationalEntityRowFence.AcquireGlobalAsync<User>(_dbContext, userId, cancellationToken);
        if (!await _dbContext.Users.AsNoTracking()
                .AnyAsync(user => user.Id == userId && !user.IsDeleted, cancellationToken))
            return false;
        var membershipId = await _dbContext.TenantUsers
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .Where(membership => membership.TenantId == tenantId && membership.UserId == userId)
            .Select(membership => membership.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (membershipId == Guid.Empty)
            return false;
        await RelationalEntityRowFence.AcquireAsync<TenantUser>(
            _dbContext, tenantId, membership => membership.Id, membershipId, cancellationToken);
        return await IsActiveTenantUserAsync(tenantId, userId, cancellationToken);
    }

    public async Task<bool> TryRemoveMembershipAsync(
        Guid tenantId,
        Guid userId,
        Guid removedBy,
        DateTime removedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var securityGate = new HumanSecuritySourceGate(_dbContext);
        await using var ownedGate = _dbContext.Database.CurrentTransaction is null
            && !HumanSecuritySourceGate.TryRequireCurrent(_dbContext)
                ? await securityGate.AcquireAsync(cancellationToken) : null;
        securityGate.RequireCurrent();
        bool removed = await _dbContext.ExecuteDisclosureMutationAsync(async token =>
        {
            securityGate.RequireCurrent();
            bool changed = await RemoveMembershipAsync(tenantId, userId, removedBy, removedAtUtc, token);
            securityGate.RequireCurrent();
            return changed;
        }, cancellationToken);
        securityGate.RequireCurrent();
        return removed;
    }

    private async Task<bool> RemoveMembershipAsync(
        Guid tenantId, Guid userId, Guid removedBy, DateTime removedAtUtc, CancellationToken cancellationToken)
    {
        HumanSecuritySourceGate.RequireCurrent(_dbContext);
        if (tenantId == Guid.Empty || userId == Guid.Empty || removedBy == Guid.Empty)
        {
            throw new ArgumentException("Tenant, user, and removal actor identifiers are required.");
        }

        if (_dbContext.TenantFilterTenantId != tenantId || _dbContext.IsTenantFilterBypassed)
        {
            throw new InvalidOperationException("Tenant membership removal requires the matching active tenant filter.");
        }

        await PostgresTenantMembershipMutationPermissions.RequireCurrentAsync(
            _dbContext, _enrollment, cancellationToken);
        var membershipId = await _dbContext.TenantUsers
            .AsNoTracking()
            .Where(membership => membership.TenantId == tenantId
                && membership.UserId == userId
                && membership.StatusId != (int)TenantUserStatusEnum.Removed
                && !membership.IsDeleted)
            .Select(membership => membership.Id)
            .SingleOrDefaultAsync(cancellationToken);
        HumanSecuritySourceGate.RequireCurrent(_dbContext);
        if (membershipId == Guid.Empty)
        {
            return false;
        }

        var removedAt = removedAtUtc.ToUniversalTime();
        _dbContext.DisclosureMutations.Enlist([tenantId]);
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
        HumanSecuritySourceGate.RequireCurrent(_dbContext);
        if (claimed == 0)
        {
            return false;
        }

        await _dbContext.TenantUserProfiles
            .Where(profile => profile.TenantId == tenantId && profile.TenantUserId == membershipId)
            .ExecuteDeleteAsync(cancellationToken);
        HumanSecuritySourceGate.RequireCurrent(_dbContext);

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

        HumanSecuritySourceGate.RequireCurrent(_dbContext);

        await PostgresTenantMembershipMutationPermissions.RequireCurrentAsync(
            _dbContext, _enrollment, cancellationToken);
        return true;
    }
}
