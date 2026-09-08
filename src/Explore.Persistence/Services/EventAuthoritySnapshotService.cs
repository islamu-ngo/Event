using Explore.Application.Contracts.Services;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public class EventAuthoritySnapshotService : IEventAuthoritySnapshotService
{
    private readonly ExploreDbContext _dbContext;

    public EventAuthoritySnapshotService(ExploreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EventAuthoritySnapshot> GetForUserAndEventsAsync(
        Guid tenantId,
        Guid userId,
        IReadOnlyCollection<Guid> eventIds,
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
        var utcNow = DateTime.UtcNow;

        var assignments = await _dbContext.EventRoleAssignments
            .AsNoTracking()
            .Where(a =>
                a.TenantId == tenantId &&
                a.UserId == userId &&
                distinctEventIds.Contains(a.EventId) &&
                a.Status == EventRoleAssignmentStatus.Active &&
                a.StartsAtUtc <= utcNow &&
                (a.ExpiresAtUtc == null || a.ExpiresAtUtc > utcNow))
            .Select(a => new AssignmentAuthorityRow(a.EventId, a.Role.MasterCode, a.RoleId))
            .ToListAsync(cancellationToken);

        var roleIds = assignments
            .Select(a => a.RoleId)
            .Distinct()
            .ToArray();

        var permissionCodesByRoleId = roleIds.Length == 0
            ? new Dictionary<int, HashSet<string>>()
            : await _dbContext.RolePermissions
                .AsNoTracking()
                .Where(rp => roleIds.Contains(rp.RoleId) && rp.Permission.IsActive)
                .GroupBy(rp => rp.RoleId)
                .Select(group => new
                {
                    RoleId = group.Key,
                    PermissionCodes = group.Select(rp => rp.Permission.MasterCode).Distinct().ToArray()
                })
                .ToDictionaryAsync(
                    item => item.RoleId,
                    item => item.PermissionCodes.ToHashSet(StringComparer.Ordinal),
                    cancellationToken);

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
