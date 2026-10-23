using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;

namespace Explore.Application.Features.Tenants;

/// <summary>
/// Compares source data after an already completed self-administrator effect. This
/// projection issues no authority; the original writer must independently prove completion.
/// </summary>
public static class HumanTenantCreationPrincipalProjection
{
    public static HumanPrincipalSourceFacts ExcludingCompletedEffect(HumanPrincipalSourceFacts original,
        HumanPrincipalSourceFacts current, HumanTenantCreationAdministratorEffect? effect,
        HumanTenantCreationPlan plan, Guid originalHuman)
    {
        if (effect is null) return current;
        if (effect.TenantId != plan.Creation.TenantId || effect.UserId != originalHuman
            || effect.UserId != plan.AdministratorUserId || effect.MembershipId != plan.AdministratorMembershipId
            || effect.GrantId != plan.AdministratorGrantId || effect.RoleScopeId != (int)RoleScopeEnum.Tenant
            || current.TenantMemberships.Count(row => row.Id == effect.MembershipId) != 1
            || current.TenantGrants.Count(row => row.Id == effect.GrantId) != 1
            || current.TenantMemberships.Single(row => row.Id == effect.MembershipId)
                != new HumanPrincipalTenantMembershipFact(effect.MembershipId, effect.TenantId,
                    (int)TenantUserStatusEnum.Active, false)
            || current.TenantGrants.Single(row => row.Id == effect.GrantId)
                != new HumanPrincipalTenantGrantFact(effect.GrantId, effect.TenantId, effect.MembershipId,
                    effect.RoleId, effect.RoleScopeId, null)
            || current.Roles.Count(row => row.Id == effect.RoleId) != 1
            || !current.Roles.Any(row => row.Id == effect.RoleId && row.RoleScopeId == effect.RoleScopeId
                && row.MasterCode == effect.RoleMasterCode)
            || current.Tenants.Count(row => row.Id == effect.TenantId) != 1
            || effect.TenantStatusId != plan.Creation.TenantStatusId
            || current.Tenants.Single(row => row.Id == effect.TenantId)
                != new HumanPrincipalTenantFact(effect.TenantId, effect.TenantStatusId, effect.TenantIsActive)
            || original.TenantMemberships.Any(row => row.Id == effect.MembershipId)
            || original.TenantGrants.Any(row => row.Id == effect.GrantId)
            || original.Tenants.Any(row => row.Id == effect.TenantId))
            throw new UnauthorizedAccessException("The completed administrator source image changed.");

        bool introducedRole = !original.Roles.Any(row => row.Id == effect.RoleId);
        if (introducedRole && original.Permissions.Any(row => row.RoleId == effect.RoleId))
            throw new UnauthorizedAccessException("The original administrator role origins are inconsistent.");

        // RoleId is returned by the existing principal source for every permission.
        // Existing-role permissions and unrelated new grants must remain visible.
        return current with
        {
            TenantMemberships = current.TenantMemberships.Where(row => row.Id != effect.MembershipId).ToImmutableArray(),
            TenantGrants = current.TenantGrants.Where(row => row.Id != effect.GrantId).ToImmutableArray(),
            Tenants = current.Tenants.Where(row => row.Id != effect.TenantId).ToImmutableArray(),
            Roles = current.Roles.Where(row => !introducedRole || row.Id != effect.RoleId).ToImmutableArray(),
            Permissions = current.Permissions.Where(row => !introducedRole || row.RoleId != effect.RoleId).ToImmutableArray()
        };
    }

    public static void RequireUnchanged(HumanPrincipalSourceFacts original, HumanPrincipalSourceFacts current)
    {
        if (original.PrincipalId != current.PrincipalId || original.SelectedTenantId != current.SelectedTenantId
            || !original.Users.OrderBy(row => row.Id).SequenceEqual(current.Users.OrderBy(row => row.Id))
            || !original.Bindings.OrderBy(row => row.Id).SequenceEqual(current.Bindings.OrderBy(row => row.Id))
            || !original.PlatformGrants.OrderBy(row => row.Id).SequenceEqual(current.PlatformGrants.OrderBy(row => row.Id))
            || !original.Roles.OrderBy(row => row.Id).SequenceEqual(current.Roles.OrderBy(row => row.Id))
            || !original.TenantMemberships.OrderBy(row => row.Id).SequenceEqual(current.TenantMemberships.OrderBy(row => row.Id))
            || !original.TenantGrants.OrderBy(row => row.Id).SequenceEqual(current.TenantGrants.OrderBy(row => row.Id))
            || !original.Tenants.OrderBy(row => row.Id).SequenceEqual(current.Tenants.OrderBy(row => row.Id))
            || !original.Organizations.OrderBy(row => row.Id).SequenceEqual(current.Organizations.OrderBy(row => row.Id))
            || !original.Groups.OrderBy(row => row.Id).SequenceEqual(current.Groups.OrderBy(row => row.Id))
            || !Permissions(original).SequenceEqual(Permissions(current))
            || original.PermissionMappingsExist != current.PermissionMappingsExist
            || !original.EventCreateOrganizations.SequenceEqual(current.EventCreateOrganizations)
            || !original.EventCreateGroups.SequenceEqual(current.EventCreateGroups)
            || !original.EventFinanceOrganizations.SequenceEqual(current.EventFinanceOrganizations)
            || !original.EventFinanceGroups.SequenceEqual(current.EventFinanceGroups))
            throw new UnauthorizedAccessException("The original tenant creator's policy sources changed.");
    }

    private static IOrderedEnumerable<HumanPrincipalPermissionFact> Permissions(HumanPrincipalSourceFacts facts) =>
        facts.Permissions.OrderBy(row => row.RoleId).ThenBy(row => row.PermissionId)
            .ThenBy(row => row.MasterCode, StringComparer.Ordinal).ThenBy(row => row.IsActive)
            .ThenBy(row => row.MatchesEventCreateCode).ThenBy(row => row.MatchesEventFinanceCode);
}
