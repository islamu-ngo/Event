using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;

namespace Explore.Infrastructure.Services;

public partial class FallbackAuthorizationService
{
    private AuthorityProfile ProfileFromOriginalSources(HumanPrincipalSourceFacts principal)
    {
        Guid tenant = _tenantContext.TenantId;
        if (principal.SelectedTenantId != tenant)
            throw new UnauthorizedAccessException("HAL policy sources belong to another selected tenant.");
        bool instanceAdmin = principal.PlatformGrants.Any(grant => principal.Roles.Any(role =>
            role.Id == grant.RoleId && role.RoleScopeId == (int)RoleScopeEnum.Platform
                && role.MatchesPlatformAdministratorCode));
        bool tenantAdmin = !instanceAdmin && principal.TenantGrants.Any(grant => grant.TenantId == tenant
            && grant.RoleId == (int)RoleEnum.TenantAdmin && grant.RevokedAt is null
            && principal.TenantMemberships.Any(member => member.Id == grant.TenantUserId
                && member.StatusId == (int)TenantUserStatusEnum.Active && !member.IsDeleted));
        return new(instanceAdmin, tenantAdmin, tenant,
            principal.Organizations.Where(member => member.RoleId == (int)RoleEnum.OrgAdmin
                && !member.IsDeleted && !member.PlacementIsDeleted && !member.OwnerIsDeleted)
                .Select(member => member.OwnerId).ToHashSet(),
            principal.Groups.Where(member => member.RoleId == (int)RoleEnum.GroupAdmin
                && !member.IsDeleted && !member.PlacementIsDeleted && !member.OwnerIsDeleted)
                .Select(member => member.OwnerId).ToHashSet(),
            principal.EventCreateOrganizations.ToHashSet(), principal.EventCreateGroups.ToHashSet(),
            principal.EventFinanceOrganizations.ToHashSet(), principal.EventFinanceGroups.ToHashSet(), principal.PrincipalId);
    }

    private static bool OrganizerFromOriginalSources(HumanPrincipalSourceFacts principal,
        IDictionary<string, object>? attributes) =>
        TryResolveGuidAttribute(attributes, "organizerUserId", out var user) && user == principal.PrincipalId
        || TryResolveGuidAttribute(attributes, "organizerOrganizationId", out var organization)
            && principal.EventCreateOrganizations.Contains(organization)
        || TryResolveGuidAttribute(attributes, "organizerGroupId", out var group)
            && principal.EventCreateGroups.Contains(group);

    /// <summary>Projects the existing Local read branches from original persisted source facts.</summary>
    internal static bool EvaluatePrivateEventManagementRead(HumanPrincipalSourceFacts principal,
        HumanEventAssignmentSourceFacts assignments, AuthorizationRequest question, DateTime evaluatedAt)
    {
        if (question.ResourceKind != ResourceKinds.Event || question.Facts is not EventAuthorizationFacts resource
            || resource.TenantId != principal.SelectedTenantId || assignments.PrincipalId != principal.PrincipalId
            || assignments.TenantId != resource.TenantId
            || question.Action is not (AuthorizationActions.Events.ViewManagement or AuthorizationActions.Update
                or AuthorizationActions.Events.ManageRegistrations))
            return false;
        var inputs = PreparedTenantInputs(principal, question, resource.TenantId);
        if (inputs.InstanceAdmin && IsInstanceAdminFallbackAllowed(question.ResourceKind, question.Action)) return true;
        if (inputs.InstanceAdmin && IsInstanceAdminFallbackDenied(question.ResourceKind, question.Action)) return false;
        bool tenantAdmin = principal.TenantGrants.Any(grant => grant.TenantId == resource.TenantId
            && grant.RoleId == (int)RoleEnum.TenantAdmin && grant.RevokedAt is null
            && principal.TenantMemberships.Any(member => member.Id == grant.TenantUserId
                && member.StatusId == (int)TenantUserStatusEnum.Active && !member.IsDeleted));
        bool permission = HumanEventAssignmentProjection.Authority(assignments, evaluatedAt).Events
            .TryGetValue(resource.EventId, out var authority)
            && authority.PermissionCodes.Contains(PermissionCodeFor(question.ResourceKind, question.Action));
        if (question.Action == AuthorizationActions.Events.ManageRegistrations)
            return resource.OrganizerUserId == principal.PrincipalId
                || resource.OrganizerOrganizationId is { } organizationId && principal.EventCreateOrganizations.Contains(organizationId)
                || resource.OrganizerGroupId is { } groupId && principal.EventCreateGroups.Contains(groupId)
                || !tenantAdmin && permission;
        if (tenantAdmin && IsTenantAdminEventAction(question.Action)) return true;
        Guid? organization = resource.OrganizationId ?? (resource.GroupId is null ? resource.EventId : null);
        bool organizationAdmin = organization is { } id && principal.Organizations.Any(member =>
            member.TenantId == resource.TenantId && member.OwnerId == id && !member.IsDeleted
            && !member.PlacementIsDeleted && !member.OwnerIsDeleted && member.RoleId == (int)RoleEnum.OrgAdmin);
        return organizationAdmin || resource.UserId == principal.PrincipalId || permission;
    }
}
