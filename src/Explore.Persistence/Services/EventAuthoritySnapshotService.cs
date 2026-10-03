using Explore.Application.Contracts.Services;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public class EventAuthoritySnapshotService : IEventAuthoritySnapshotService
{
    private readonly ExploreDbContext _dbContext;

    public EventAuthoritySnapshotService(ExploreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EventAuthoritySnapshot> GetCommitBoundForUserAndEventsAsync(
        Guid tenantId, Guid userId, IReadOnlyCollection<Guid> eventIds,
        DateTime evaluationTimeUtc, CancellationToken cancellationToken)
    {
        var ids = eventIds.Distinct().ToArray();
        var parents = await _dbContext.Events.AsNoTracking()
            .Where(parent => parent.TenantId == tenantId && ids.Contains(parent.Id))
            .Select(parent => new { parent.Id, parent.ActorId, parent.OrganizerActorId })
            .ToListAsync(cancellationToken);
        var actors = parents.Select(parent => parent.ActorId)
            .Concat(parents.Where(parent => parent.OrganizerActorId.HasValue)
                .Select(parent => parent.OrganizerActorId!.Value))
            .Distinct().OrderBy(id => id.ToString("N"), StringComparer.Ordinal);
        foreach (var actorId in actors)
            await RelationalEntityRowFence.AcquireGlobalAsync<Actor>(_dbContext, actorId, cancellationToken);
        foreach (var parent in parents.OrderBy(parent => parent.Id.ToString("N"), StringComparer.Ordinal))
            await RelationalEntityRowFence.AcquireAsync<Explore.Domain.Event>(
                _dbContext, tenantId, row => row.Id, parent.Id, cancellationToken);
        var assignments = await _dbContext.EventRoleAssignments.AsNoTracking()
            .Where(assignment => assignment.TenantId == tenantId && assignment.UserId == userId
                && ids.Contains(assignment.EventId))
            .Select(assignment => new { assignment.Id, assignment.RoleId })
            .ToListAsync(cancellationToken);
        foreach (var assignment in assignments.OrderBy(row => row.Id.ToString("N"), StringComparer.Ordinal))
            await RelationalEntityRowFence.AcquireAsync<EventRoleAssignment>(
                _dbContext, tenantId, row => row.Id, assignment.Id, cancellationToken);
        var roleIds = assignments.Select(row => row.RoleId).Distinct().Order().ToArray();
        foreach (int roleId in roleIds)
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(_dbContext, [roleId], cancellationToken);
        var pairs = await _dbContext.RolePermissions.AsNoTracking()
            .Where(pair => roleIds.Contains(pair.RoleId))
            .Select(pair => new { pair.RoleId, pair.PermissionId })
            .ToListAsync(cancellationToken);
        foreach (int permissionId in pairs.Select(pair => pair.PermissionId).Distinct().Order())
            await RelationalEntityRowFence.AcquireLookupAsync<Permission>(
                _dbContext, [permissionId], cancellationToken);
        foreach (var pair in pairs.OrderBy(pair => pair.RoleId).ThenBy(pair => pair.PermissionId))
            await RelationalEntityRowFence.AcquireLookupAsync<RolePermission>(
                _dbContext, [pair.RoleId, pair.PermissionId], cancellationToken);
        return await GetForUserAndEventsAsync(
            tenantId, userId, ids, evaluationTimeUtc, cancellationToken);
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
