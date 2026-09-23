using Explore.Application.Authorization;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;

namespace Explore.Application.Services;

public sealed partial class EventResourceAuthoritySnapshotReader
{
    private async Task<EventModerationPrincipalFacts> ReadModerationPrincipalAsync(
        Guid tenantId, Guid userId, IReadOnlyCollection<Guid> eventIds,
        IReadOnlyList<EventRoleAssignment> assignments, IReadOnlyList<RolePermission> eventPermissions,
        IReadOnlyList<PlatformUserRole> platform, IReadOnlyList<TenantUserRoleGrant> tenants,
        CancellationToken cancellationToken)
    {
        var organizations = await resources.GetSubjectOrganizationMembershipsAsync(userId, FactLimit, cancellationToken);
        var groups = await resources.GetSubjectGroupMembershipsAsync(userId, FactLimit, cancellationToken);
        var adminOrganizations = await resources.GetSubjectOrganizationAdminMembershipsAsync(userId, FactLimit, cancellationToken);
        var adminGroups = await resources.GetSubjectGroupAdminMembershipsAsync(userId, FactLimit, cancellationToken);
        var permissions = await resources.GetRolePermissionsAsync(organizations.Select(value => value.RoleId)
            .Concat(groups.Select(value => value.RoleId)).Distinct().ToArray(), FactLimit, cancellationToken);
        // Preserve the native principal's legacy empty-table semantics, not the resource organizer-control gate.
        bool permissionsSeeded = await resources.GetFirstRolePermissionAsync(cancellationToken) is not null;

        bool HasPermission(int roleId, string code, RoleEnum fallbackRole) => permissions.Any(value =>
            value.RoleId == roleId && value.Permission.IsActive && value.Permission.MasterCode == code)
            || !permissionsSeeded && roleId == (int)fallbackRole;

        var principal = new EventModerationPrincipal(userId,
            IsNativeInstanceAdmin(userId, platform), new(NativeAdminTenantIds(userId, tenants)),
            new(adminOrganizations.Where(value => value.RoleId == (int)RoleEnum.OrgAdmin)
                .Select(value => value.OrganizationTenant.OrganizationId)),
            new(adminGroups.Where(value => value.RoleId == (int)RoleEnum.GroupAdmin)
                .Select(value => value.GroupTenant.GroupId)),
            new(organizations.Where(value => HasPermission(value.RoleId, PermissionCodes.EventCreate, RoleEnum.OrgAdmin))
                .Select(value => value.OrganizationTenant.OrganizationId)),
            new(groups.Where(value => HasPermission(value.RoleId, PermissionCodes.EventCreate, RoleEnum.GroupAdmin))
                .Select(value => value.GroupTenant.GroupId)),
            new(organizations.Where(value => HasPermission(value.RoleId, PermissionCodes.EventManageFinance, RoleEnum.OrgAdmin))
                .Select(value => value.OrganizationTenant.OrganizationId)),
            new(groups.Where(value => HasPermission(value.RoleId, PermissionCodes.EventManageFinance, RoleEnum.GroupAdmin))
                .Select(value => value.GroupTenant.GroupId)), new([]));

        return new(principal, tenantId, eventIds, assignments
            .Where(value => value.Status == EventRoleAssignmentStatus.Active)
            .Select(value => new EventModerationTimedAssignment(value.EventId, value.Role.MasterCode,
                new(eventPermissions.Where(permission => permission.RoleId == value.RoleId && permission.Permission.IsActive)
                    .Select(permission => permission.Permission.MasterCode)),
                new(true, Utc(value.StartsAtUtc), value.ExpiresAtUtc is { } expiry ? Utc(expiry) : null))));
    }

    private static bool IsNativeInstanceAdmin(Guid userId, IEnumerable<PlatformUserRole> platform) =>
        platform.Any(value => value.UserId == userId && value.Role.RoleScopeId == (int)RoleScopeEnum.Platform
            && value.Role.MasterCode == "platform.admin");

    private static IEnumerable<Guid> NativeAdminTenantIds(Guid userId, IEnumerable<TenantUserRoleGrant> tenants) =>
        tenants.Where(value => value.RoleId == (int)RoleEnum.TenantAdmin && value.RevokedAt is null
                && value.TenantUserId == value.TenantUser.Id && value.TenantId == value.TenantUser.TenantId
                && value.TenantUser.UserId == userId && !value.TenantUser.IsDeleted
                && value.TenantUser.StatusId == (int)TenantUserStatusEnum.Active)
            .Select(value => value.TenantId);

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static EventAuthorizationFacts CaptureNativeParent(Event parent) => new(
        parent.TenantId, parent.Id, parent.ActorId, parent.Actor?.UserId, parent.Actor?.OrganizationId,
        parent.Actor?.GroupId, parent.OrganizerActorId, parent.OrganizerActor?.UserId,
        parent.OrganizerActor?.OrganizationId, parent.OrganizerActor?.GroupId,
        parent.EventProvenanceType?.MasterCode ?? parent.EventProvenanceTypeId.ToString(), parent.SubmittedByUserId);
}
