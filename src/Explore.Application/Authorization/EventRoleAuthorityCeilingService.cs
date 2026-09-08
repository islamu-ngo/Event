using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;

namespace Explore.Application.Authorization;

public interface IEventRoleAuthorityCeilingService
{
    Task<IReadOnlyList<EventRolePreset>> GetAssignableRolePresetsAsync(
        Guid tenantId,
        Guid eventId,
        Guid assignerUserId,
        CancellationToken cancellationToken);

    Task<EventRoleAssignmentAuthorityResult> CanAssignRoleAsync(
        Guid tenantId,
        Guid eventId,
        Guid assignerUserId,
        int roleId,
        CancellationToken cancellationToken);
}

public sealed class EventRoleAuthorityCeilingService : IEventRoleAuthorityCeilingService
{
    private static readonly HashSet<string> NonDelegablePermissionCodes = new(StringComparer.Ordinal)
    {
        PermissionCodes.EventManageOwner,
        PermissionCodes.EventTransferOwnership,
        PermissionCodes.EventDelete,
        PermissionCodes.EventManageFinance,
        PermissionCodes.EventApprovePublish
    };

    private readonly IEventAuthoritySnapshotService _authoritySnapshotService;
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRegistryService _permissionRegistry;

    public EventRoleAuthorityCeilingService(
        IEventAuthoritySnapshotService authoritySnapshotService,
        IRoleRepository roleRepository,
        IPermissionRegistryService permissionRegistry)
    {
        _authoritySnapshotService = authoritySnapshotService;
        _roleRepository = roleRepository;
        _permissionRegistry = permissionRegistry;
    }

    public async Task<IReadOnlyList<EventRolePreset>> GetAssignableRolePresetsAsync(
        Guid tenantId,
        Guid eventId,
        Guid assignerUserId,
        CancellationToken cancellationToken)
    {
        var assignablePermissions = await GetAssignablePermissionCodesAsync(
            tenantId, eventId, assignerUserId, cancellationToken);

        if (assignablePermissions.Count == 0)
        {
            return Array.Empty<EventRolePreset>();
        }

        var eventRoles = await _roleRepository.GetByScopeAsync(RoleScopeEnum.Event);
        var firstReleaseRoles = eventRoles
            .Where(role => IsFirstReleaseEventRole(role.Id))
            .OrderBy(role => role.Id)
            .ToList();

        var presets = new List<EventRolePreset>();

        foreach (var role in firstReleaseRoles)
        {
            var rolePermissions = await _roleRepository.GetPermissionsForRoleAsync(role.Id);
            var rolePermissionCodes = rolePermissions
                .Where(permission => permission.IsActive)
                .Select(permission => permission.MasterCode)
                .ToHashSet(StringComparer.Ordinal);

            if (!rolePermissionCodes.IsSubsetOf(assignablePermissions))
            {
                continue;
            }

            presets.Add(new EventRolePreset(
                role.Id,
                role.MasterCode,
                role.FullName,
                role.Description,
                rolePermissionCodes.OrderBy(code => code, StringComparer.Ordinal).ToArray()));
        }

        return presets;
    }

    public async Task<EventRoleAssignmentAuthorityResult> CanAssignRoleAsync(
        Guid tenantId,
        Guid eventId,
        Guid assignerUserId,
        int roleId,
        CancellationToken cancellationToken)
    {
        if (!IsFirstReleaseEventRole(roleId))
        {
            return EventRoleAssignmentAuthorityResult.Denied(
                EventRoleAuthorityFailureCodes.RoleNotAssignable,
                "The requested event role is not assignable in this release.");
        }

        var assignablePermissions = await GetAssignablePermissionCodesAsync(
            tenantId, eventId, assignerUserId, cancellationToken);

        if (assignablePermissions.Count == 0)
        {
            return EventRoleAssignmentAuthorityResult.Denied(
                EventRoleAuthorityFailureCodes.AuthorityMissing,
                "You do not have event-team management authority for this event.");
        }

        var role = await _roleRepository.GetByIdAsync(roleId);
        if (role is null || role.Scope != RoleScopeEnum.Event)
        {
            return EventRoleAssignmentAuthorityResult.Denied(
                EventRoleAuthorityFailureCodes.RoleNotAssignable,
                "The requested role is not an event role.");
        }

        var rolePermissionCodes = (await _roleRepository.GetPermissionsForRoleAsync(roleId))
            .Where(permission => permission.IsActive)
            .Select(permission => permission.MasterCode)
            .ToHashSet(StringComparer.Ordinal);

        var missingPermissionCodes = rolePermissionCodes
            .Where(code => !assignablePermissions.Contains(code))
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();

        if (missingPermissionCodes.Length > 0)
        {
            return EventRoleAssignmentAuthorityResult.Denied(
                EventRoleAuthorityFailureCodes.AuthorityCeilingExceeded,
                "The role contains permissions outside your same-event authority ceiling.",
                missingPermissionCodes);
        }

        return EventRoleAssignmentAuthorityResult.Allowed(rolePermissionCodes);
    }

    private async Task<HashSet<string>> GetAssignablePermissionCodesAsync(
        Guid tenantId,
        Guid eventId,
        Guid assignerUserId,
        CancellationToken cancellationToken)
    {
        var snapshot = await _authoritySnapshotService.GetForUserAndEventsAsync(
            tenantId, assignerUserId, new[] { eventId }, cancellationToken);

        if (!snapshot.Events.TryGetValue(eventId, out var authority) ||
            !authority.PermissionCodes.Contains(PermissionCodes.EventManageTeam))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var activePermissionByCode = (await _permissionRegistry.GetAllPermissionsAsync())
            .Where(permission => permission.IsActive)
            .ToDictionary(permission => permission.MasterCode, StringComparer.Ordinal);

        var assignablePermissions = authority.PermissionCodes
            .Where(code => activePermissionByCode.TryGetValue(code, out var permission) && !permission.IsFiltered)
            .Where(code => !NonDelegablePermissionCodes.Contains(code))
            .ToHashSet(StringComparer.Ordinal);

        return assignablePermissions;
    }

    private static bool IsFirstReleaseEventRole(int roleId)
    {
        return roleId is
            (int)RoleEnum.EventOwner or
            (int)RoleEnum.EventManager or
            (int)RoleEnum.RegistrationManager or
            (int)RoleEnum.CheckInStaff;
    }
}

public sealed record EventRolePreset(
    int RoleId,
    string MasterCode,
    string FullName,
    string? Description,
    IReadOnlyCollection<string> PermissionCodes);

public sealed record EventRoleAssignmentAuthorityResult(
    bool IsAllowed,
    string? FailureCode,
    string? ErrorMessage,
    IReadOnlyCollection<string> PermissionCodes,
    IReadOnlyCollection<string> MissingPermissionCodes)
{
    public static EventRoleAssignmentAuthorityResult Allowed(IReadOnlyCollection<string> permissionCodes)
    {
        return new EventRoleAssignmentAuthorityResult(true, null, null, permissionCodes, Array.Empty<string>());
    }

    public static EventRoleAssignmentAuthorityResult Denied(
        string failureCode,
        string errorMessage,
        IReadOnlyCollection<string>? missingPermissionCodes = null)
    {
        return new EventRoleAssignmentAuthorityResult(
            false,
            failureCode,
            errorMessage,
            Array.Empty<string>(),
            missingPermissionCodes ?? Array.Empty<string>());
    }
}

public static class EventRoleAuthorityFailureCodes
{
    public const string AuthorityMissing = "event_role_authority_missing";
    public const string AuthorityCeilingExceeded = "event_role_authority_ceiling_exceeded";
    public const string RoleNotAssignable = "event_role_not_assignable";
}
