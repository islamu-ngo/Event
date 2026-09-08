using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class GroupRepository : GenericRepository<Group, Guid>, IGroupRepository
{
    private readonly ExploreDbContext _dbContext;

    public GroupRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public new async Task<Group?> GetById(Guid id)
    {
        return await _dbContext.Groups
            .Include(g => g.TenantParticipations)
                .ThenInclude(p => p.ApprovalStatus)
            .FirstOrDefaultAsync(g => g.Id == id);
    }

    public async Task<List<Group>> GetGroupsWithDetails()
    {
        return await _dbContext.Groups
            .AsNoTracking()
            .AsSplitQuery()
            .Include(g => g.Actor)
                .ThenInclude(a => a!.Pii)
            .Include(g => g.Actor)
                .ThenInclude(a => a!.AtprotoIdentities)
            .Include(g => g.TenantParticipations)
                .ThenInclude(p => p.ApprovalStatus)
            .Include(g => g.TenantParticipations)
                .ThenInclude(p => p.Tenant)
            .ToListAsync();
    }

    public async Task<Group?> GetGroupWithDetails(Guid id)
    {
        return await _dbContext.Groups
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Include(g => g.Actor)
                .ThenInclude(a => a!.Pii)
            .Include(g => g.Actor)
                .ThenInclude(a => a!.AtprotoIdentities)
            .Include(g => g.TenantParticipations)
                .ThenInclude(p => p.ApprovalStatus)
            .Include(g => g.TenantParticipations)
                .ThenInclude(p => p.Tenant)
            .Include(g => g.TenantParticipations)
                .ThenInclude(p => p.Members)
                .ThenInclude(m => m.User)
            .Include(g => g.TenantParticipations)
                .ThenInclude(p => p.Members)
                .ThenInclude(m => m.User)
                .ThenInclude(u => u!.Pii)
            .Include(g => g.TenantParticipations)
                .ThenInclude(p => p.Members)
                .ThenInclude(m => m.Role)
            .FirstOrDefaultAsync(g => g.Id == id);
    }

    public async Task<List<Group>> GetMyGroups(Guid userId)
    {
        return await _dbContext.Groups
            .AsNoTracking()
            .AsSplitQuery()
            .Include(g => g.Actor)
                .ThenInclude(a => a!.Pii)
            .Include(g => g.Actor)
                .ThenInclude(a => a!.AtprotoIdentities)
            .Include(g => g.TenantParticipations)
                .ThenInclude(p => p.ApprovalStatus)
            .Include(g => g.TenantParticipations)
                .ThenInclude(p => p.Members)
                    .ThenInclude(m => m.Role)
            .Where(g => g.TenantParticipations.Any(p => p.Members.Any(m => m.UserId == userId)))
            .ToListAsync();
    }

    public async Task<(List<Group> Items, int TotalCount)> GetGroupsWithDetailsPaged(int pageNumber, int pageSize)
    {
        var query = _dbContext.Groups
            .AsNoTracking()
            .AsSplitQuery()
            .Include(g => g.Actor)
                .ThenInclude(a => a!.Pii)
            .Include(g => g.Actor)
                .ThenInclude(a => a!.AtprotoIdentities)
            .Include(g => g.TenantParticipations)
                .ThenInclude(p => p.ApprovalStatus)
            .Include(g => g.TenantParticipations)
                .ThenInclude(p => p.Tenant)
            .OrderBy(g => g.FullName);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<(List<Group> Items, int TotalCount)> GetMyGroupsPaged(Guid userId, int pageNumber, int pageSize)
    {
        var query = _dbContext.Groups
            .AsNoTracking()
            .AsSplitQuery()
            .Include(g => g.Actor)
                .ThenInclude(a => a!.Pii)
            .Include(g => g.Actor)
                .ThenInclude(a => a!.AtprotoIdentities)
            .Include(g => g.TenantParticipations)
                .ThenInclude(p => p.ApprovalStatus)
            .Include(g => g.TenantParticipations)
                .ThenInclude(p => p.Members)
                    .ThenInclude(m => m.Role)
            .Where(g => g.TenantParticipations.Any(p => p.Members.Any(m => m.UserId == userId)))
            .OrderBy(g => g.FullName);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<bool> OrganizationExistsInTenant(Guid organizationId, Guid tenantId, CancellationToken cancellationToken)
    {
        return await _dbContext.OrganizationTenants
            .AsNoTracking()
            .AnyAsync(p => p.OrganizationId == organizationId && p.TenantId == tenantId, cancellationToken);
    }

    public async Task<bool> GroupExistsInTenant(Guid groupId, Guid tenantId, CancellationToken cancellationToken)
    {
        return await _dbContext.GroupTenants
            .AsNoTracking()
            .AnyAsync(p => p.GroupId == groupId && p.TenantId == tenantId, cancellationToken);
    }

    public async Task<bool> WouldCreateHierarchyCycle(Guid groupId, Guid parentGroupId, Guid tenantId, CancellationToken cancellationToken)
    {
        var current = await FindByGroupId(parentGroupId, tenantId, cancellationToken);
        var visited = new HashSet<Guid>();

        for (var depth = 0; current is not null && depth <= GroupHierarchyRules.MaxDepth; depth++)
        {
            if (current.GroupId == groupId)
            {
                return true;
            }

            if (!visited.Add(current.Id) || current.ParentGroupTenantId is not { } parentId)
            {
                return false;
            }

            current = await FindById(parentId, tenantId, cancellationToken);
        }

        return false;
    }

    public async Task<bool> WouldExceedHierarchyDepth(Guid? parentGroupId, Guid tenantId, int maxDepth, CancellationToken cancellationToken)
    {
        if (!parentGroupId.HasValue)
        {
            return false;
        }

        return await GetAncestorDepth(parentGroupId.Value, tenantId, maxDepth, cancellationToken) >= maxDepth;
    }

    public async Task<bool> WouldExceedHierarchyDepthForMove(Guid groupId, Guid? parentGroupId, Guid tenantId, int maxDepth, CancellationToken cancellationToken)
    {
        var parentDepth = parentGroupId.HasValue
            ? await GetAncestorDepth(parentGroupId.Value, tenantId, maxDepth + 1, cancellationToken)
            : 0;
        var subtreeDepth = await GetSubtreeDepth(groupId, tenantId, maxDepth + 1, cancellationToken);

        return parentDepth + subtreeDepth > maxDepth;
    }

    // ponytail: the domain caps hierarchy depth at eight; switch to provider-specific recursive SQL only if that ceiling grows measurably.
    private async Task<int> GetAncestorDepth(Guid groupId, Guid tenantId, int depthLimit, CancellationToken cancellationToken)
    {
        var current = await FindByGroupId(groupId, tenantId, cancellationToken);
        var visited = new HashSet<Guid>();
        var depth = 0;

        while (current is not null && depth < depthLimit)
        {
            depth++;
            if (!visited.Add(current.Id))
            {
                return depthLimit;
            }

            if (current.ParentGroupTenantId is not { } parentId)
            {
                return depth;
            }

            current = await FindById(parentId, tenantId, cancellationToken);
        }

        return depth;
    }

    private async Task<int> GetSubtreeDepth(Guid groupId, Guid tenantId, int depthLimit, CancellationToken cancellationToken)
    {
        var root = await FindByGroupId(groupId, tenantId, cancellationToken);
        if (root is null)
        {
            return 1;
        }

        var visited = new HashSet<Guid> { root.Id };
        Guid[] frontier = [root.Id];
        var depth = 1;

        while (frontier.Length > 0 && depth < depthLimit)
        {
            var children = await _dbContext.GroupTenants
                .AsNoTracking()
                .Where(participation =>
                    participation.TenantId == tenantId &&
                    participation.ParentGroupTenantId.HasValue &&
                    frontier.Contains(participation.ParentGroupTenantId.Value))
                .Select(participation => participation.Id)
                .ToArrayAsync(cancellationToken);

            if (children.Length == 0)
            {
                break;
            }

            if (children.Any(childId => !visited.Add(childId)))
            {
                return depthLimit;
            }

            frontier = children;
            depth++;
        }

        return depth;
    }

    private Task<HierarchyNode?> FindByGroupId(Guid groupId, Guid tenantId, CancellationToken cancellationToken)
    {
        return _dbContext.GroupTenants
            .AsNoTracking()
            .Where(participation => participation.TenantId == tenantId && participation.GroupId == groupId)
            .Select(participation => new HierarchyNode(
                participation.Id,
                participation.GroupId,
                participation.ParentGroupTenantId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private Task<HierarchyNode?> FindById(Guid id, Guid tenantId, CancellationToken cancellationToken)
    {
        return _dbContext.GroupTenants
            .AsNoTracking()
            .Where(participation => participation.TenantId == tenantId && participation.Id == id)
            .Select(participation => new HierarchyNode(
                participation.Id,
                participation.GroupId,
                participation.ParentGroupTenantId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private sealed record HierarchyNode(Guid Id, Guid GroupId, Guid? ParentGroupTenantId);

    public async Task<T> ExecuteWithHierarchyMutationLock<T>(Guid tenantId, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            try
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                await using IAsyncDisposable hierarchyLease = await RelationalNamedLock.AcquireTransactionAsync(
                    _dbContext,
                    $"group-hierarchy:{tenantId}",
                    cancellationToken);

                var result = await operation(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                _dbContext.ChangeTracker.Clear();
                throw;
            }
        });
    }
}
