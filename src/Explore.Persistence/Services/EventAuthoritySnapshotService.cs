using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public class EventAuthoritySnapshotService : IEventAuthoritySnapshotService
{
    private readonly ExploreDbContext _dbContext;
    private readonly Func<IPrivateEventManagementReader>? _privateManagement;

    public EventAuthoritySnapshotService(ExploreDbContext dbContext, Func<IPrivateEventManagementReader>? privateManagement = null)
    {
        _dbContext = dbContext;
        _privateManagement = privateManagement;
    }

    public async Task<EventAuthoritySnapshot> GetCommitBoundForUserAndEventsAsync(
        Guid tenantId, Guid userId, IReadOnlyCollection<Guid> eventIds,
        DateTime evaluationTimeUtc, CancellationToken cancellationToken)
        => (await GetCommitBoundForUsersAndEventsAsync(
            tenantId, [userId], eventIds, evaluationTimeUtc, cancellationToken))[userId];

    internal async Task<IReadOnlyDictionary<Guid, EventAuthoritySnapshot>> GetCommitBoundForUsersAndEventsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<Guid> eventIds,
        DateTime evaluationTimeUtc, CancellationToken cancellationToken,
        EventAuthorityProviderOperations.AuthorityPlan? acquiredPlan = null)
    {
        var humans = userIds.Distinct().OrderBy(id => id.ToString("N"), StringComparer.Ordinal).ToArray();
        var ids = eventIds.Distinct().OrderBy(id => id.ToString("N"), StringComparer.Ordinal).ToArray();
        var plan = acquiredPlan ?? await EventAuthorityProviderOperations.ReadPlanAsync(
            _dbContext, tenantId, humans, ids, cancellationToken);
        var parents = plan.Parents;
        if (acquiredPlan is null)
        {
            var roleIds = plan.Assignments.Select(row => row.RoleId).Distinct().Order().ToArray();
            foreach (int roleId in roleIds)
                await RelationalEntityRowFence.AcquireLookupAsync<Role>(_dbContext, [roleId], cancellationToken);
            await AcquirePermissionSourcesAsync(_dbContext, roleIds, cancellationToken);
            var actors = parents.Select(parent => parent.ActorId)
                .Concat(parents.Where(parent => parent.OrganizerActorId.HasValue)
                    .Select(parent => parent.OrganizerActorId!.Value))
                .Distinct().OrderBy(id => id.ToString("N"), StringComparer.Ordinal);
            foreach (var actorId in actors)
                await RelationalEntityRowFence.AcquireGlobalAsync<Actor>(_dbContext, actorId, cancellationToken);
        }
        else
        {
            // The parent acquisition already holds these early anchors. Never replan or add
            // Role/Actor locks behind its Tenant, epoch or native-resource fences.
            var currentParents = await EventAuthorityProviderOperations.ReadOwnershipAsync(_dbContext, tenantId, ids, cancellationToken);
            var currentAssignments = await EventAuthorityProviderOperations.ReadAssignmentsAsync(_dbContext, tenantId, humans, ids, cancellationToken);
            if (parents.Any(row => !ids.Contains(row.Id))
                || plan.Assignments.Any(row => !ids.Contains(row.EventId) || !humans.Contains(row.UserId))
                || !parents.OrderBy(row => row.Id).SequenceEqual(currentParents.OrderBy(row => row.Id))
                || !plan.Assignments.OrderBy(row => row.Id).SequenceEqual(currentAssignments.OrderBy(row => row.Id)))
                throw new DbUpdateConcurrencyException("Borrowed Event authority sources changed before native acquisition.");
        }
        foreach (var id in ids)
            await RelationalEntityRowFence.AcquireAsync<Explore.Domain.Event>(
                _dbContext, tenantId, row => row.Id, id, cancellationToken);
        var heldParents = await EventAuthorityProviderOperations.ReadOwnershipAsync(
            _dbContext, tenantId, ids, cancellationToken);
        // Never add a newly discovered Actor behind an Event fence. Dirty, missing
        // or changed ownership invalidates this entire attempt before authority is
        // read; a new attempt must plan and acquire its complete sorted set again.
        if (!parents.OrderBy(parent => parent.Id).SequenceEqual(heldParents.OrderBy(parent => parent.Id)))
            throw new DbUpdateConcurrencyException("Event authority ownership changed before its source fence.");
        var assignments = await EventAuthorityProviderOperations.ReadAssignmentsAsync(
            _dbContext, tenantId, humans, ids, cancellationToken);
        if (!plan.Assignments.OrderBy(row => row.Id).SequenceEqual(assignments.OrderBy(row => row.Id)))
            throw new DbUpdateConcurrencyException("Event authority role sources changed before their source fence.");
        foreach (var assignment in assignments.OrderBy(row => row.Id.ToString("N"), StringComparer.Ordinal))
            await RelationalEntityRowFence.AcquireAsync<EventRoleAssignment>(
                _dbContext, tenantId, row => row.Id, assignment.Id, cancellationToken);
        var heldAssignments = await EventAuthorityProviderOperations.ReadAssignmentsAsync(
            _dbContext, tenantId, humans, ids, cancellationToken);
        // A newly discovered role needs a new whole attempt, never a late Role lock behind Event.
        if (!plan.Assignments.OrderBy(row => row.Id).SequenceEqual(heldAssignments.OrderBy(row => row.Id)))
            throw new DbUpdateConcurrencyException("Event authority role sources changed under their source fence.");
        var snapshots = new Dictionary<Guid, EventAuthoritySnapshot>();
        foreach (var human in humans)
            snapshots.Add(human, await GetForUserAndEventsAsync(
                tenantId, human, ids, evaluationTimeUtc, cancellationToken));
        return snapshots;
    }

    internal static async Task AcquireHumanSourcesAsync(ExploreDbContext context, Guid tenantId,
        Guid[] humans, int[] eventRoles, Guid? emailSubjectId, CancellationToken token)
    {
        foreach (var id in humans) await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, id, token);
        if (emailSubjectId is { } subjectId) await RelationalEntityRowFence.AcquireGlobalAsync<UserPii>(context, subjectId, token);
        foreach (var id in await context.UserExternalLogins.AsNoTracking().Where(row => humans.Contains(row.UserId)).OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token))
            await RelationalEntityRowFence.AcquireGlobalAsync<UserExternalLogin>(context, id, token);
        var platform = await context.PlatformUserRoles.AsNoTracking().Where(row => humans.Contains(row.UserId)).OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToArrayAsync(token);
        foreach (var row in platform) await RelationalEntityRowFence.AcquireGlobalAsync<PlatformUserRole>(context, row.Id, token);
        var members = await context.TenantUsers.AsNoTracking().Where(row => row.TenantId == tenantId && humans.Contains(row.UserId)).OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
        foreach (var id in members) await RelationalEntityRowFence.AcquireAsync<TenantUser>(context, tenantId, row => row.Id, id, token);
        var grants = await context.TenantUserRoleGrants.AsNoTracking().Where(row => row.TenantId == tenantId && members.Contains(row.TenantUserId))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToArrayAsync(token);
        foreach (var row in grants) await RelationalEntityRowFence.AcquireAsync<TenantUserRoleGrant>(context, tenantId, row => row.Id, row.Id, token);
        var roles = platform.Select(row => row.RoleId).Concat(grants.Select(row => row.RoleId)).Concat(eventRoles).Distinct().Order().ToArray();
        foreach (int id in roles) await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [id], token);
        await AcquirePermissionSourcesAsync(context, roles, token);
    }

    internal static async Task AcquirePermissionSourcesAsync(
        ExploreDbContext context, int[] heldRoleIds, CancellationToken token)
    {
        var pairs = await context.RolePermissions.AsNoTracking().Where(row => heldRoleIds.Contains(row.RoleId))
            .Select(row => new { row.RoleId, row.PermissionId }).ToArrayAsync(token);
        foreach (var id in pairs.Select(row => row.PermissionId).Distinct().Order())
            await RelationalEntityRowFence.AcquireLookupAsync<Permission>(context, [id], token);
        foreach (var pair in pairs.OrderBy(row => row.RoleId).ThenBy(row => row.PermissionId))
            await RelationalEntityRowFence.AcquireLookupAsync<RolePermission>(context, [pair.RoleId, pair.PermissionId], token);
    }

    public async Task<EventAuthoritySnapshot> GetForUserAndEventsAsync(
        Guid tenantId,
        Guid userId,
        IReadOnlyCollection<Guid> eventIds,
        DateTime evaluationTimeUtc,
        CancellationToken cancellationToken)
    {
        if (eventIds.Count == 0)
        {
            return new EventAuthoritySnapshot(
                tenantId,
                userId,
                new Dictionary<Guid, EventAuthorityForUser>());
        }

        var distinctEventIds = eventIds.Distinct().ToArray();

        if (_privateManagement is not null && _dbContext.Database.CurrentTransaction is null
            && PostgresPrivateEventManagementContract.For(_dbContext) is { } native)
            return await native.ReadAuthorityAsync(
                _privateManagement, tenantId, userId, distinctEventIds, cancellationToken);

        var assignments = await _dbContext.EventRoleAssignments
            .AsNoTracking()
            .Where(a =>
                a.TenantId == tenantId &&
                a.UserId == userId &&
                distinctEventIds.Contains(a.EventId) &&
                a.Status == EventRoleAssignmentStatus.Active &&
                a.StartsAtUtc <= evaluationTimeUtc &&
                (a.ExpiresAtUtc == null || a.ExpiresAtUtc > evaluationTimeUtc))
            .Select(a => new AssignmentAuthorityRow(a.EventId, a.Role.MasterCode, a.RoleId))
            .ToListAsync(cancellationToken);

        var roleIds = assignments
            .Select(a => a.RoleId)
            .Distinct()
            .ToArray();

        var permissionCodesByRoleId = new Dictionary<int, HashSet<string>>();
        if (roleIds.Length > 0)
        {
            var permissions = await _dbContext.RolePermissions
                .AsNoTracking()
                .Where(rp => roleIds.Contains(rp.RoleId) && rp.Permission.IsActive)
                .Select(rp => new { rp.RoleId, PermissionCode = rp.Permission.MasterCode })
                .ToListAsync(cancellationToken);

            permissionCodesByRoleId = permissions
                .GroupBy(permission => permission.RoleId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(permission => permission.PermissionCode).ToHashSet(StringComparer.Ordinal));
        }

        var events = distinctEventIds.ToDictionary(
            eventId => eventId,
            eventId => BuildAuthority(assignments.Where(a => a.EventId == eventId), permissionCodesByRoleId));

        return new EventAuthoritySnapshot(tenantId, userId, events);
    }

    private static EventAuthorityForUser BuildAuthority(
        IEnumerable<AssignmentAuthorityRow> assignments,
        IReadOnlyDictionary<int, HashSet<string>> permissionCodesByRoleId)
    {
        var roleCodes = new HashSet<string>(StringComparer.Ordinal);
        var permissionCodes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var assignment in assignments)
        {
            roleCodes.Add(assignment.RoleCode);

            if (!permissionCodesByRoleId.TryGetValue(assignment.RoleId, out var rolePermissionCodes))
            {
                continue;
            }

            permissionCodes.UnionWith(rolePermissionCodes);
        }

        var roleIds = assignments.Select(assignment => assignment.RoleId).ToHashSet();

        return new EventAuthorityForUser(
            roleCodes,
            permissionCodes,
            roleIds.Contains((int)RoleEnum.EventOwner),
            roleIds.Contains((int)RoleEnum.EventManager) || permissionCodes.Contains(PermissionCodes.EventManageTeam));
    }

    private sealed record AssignmentAuthorityRow(Guid EventId, string RoleCode, int RoleId);
}
