using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Infrastructure.Services;

public partial class FallbackAuthorizationService
{
    // This is the exact Local webhook branch, not an alternate grant for Cerbos.
    // Short-circuit inputs deliberately exclude irrelevant memberships after an instance grant.
    internal static (bool Instance, bool Tenant, bool Organization, bool Group, bool Self) PreparedWebhookInputs(
        HumanPrincipalSourceFacts principal, AuthorizationRequest request, Guid selectedTenant)
    {
        bool instance = principal.PlatformGrants.Any(grant => principal.Roles.Any(role =>
            role.Id == grant.RoleId && role.RoleScopeId == (int)RoleScopeEnum.Platform
            && role.MatchesPlatformAdministratorCode));
        if (instance && IsInstanceAdminFallbackAllowed(request.ResourceKind, request.Action))
            return (true, false, false, false, false);

        var attributes = TrustedAttributes(request);
        if (!TryResolveWebhookOwnerKind(attributes, out var kind) || kind == WebhookConsumerKind.Tenant)
        {
            Guid tenant = ResolveTenantId(attributes, selectedTenant);
            bool administrator = tenant == selectedTenant && principal.TenantGrants.Any(grant =>
                grant.TenantId == tenant && grant.RoleId == (int)RoleEnum.TenantAdmin && grant.RevokedAt is null
                && principal.TenantMemberships.Any(member => member.Id == grant.TenantUserId
                    && !member.IsDeleted && member.StatusId == (int)TenantUserStatusEnum.Active));
            return (false, administrator, false, false, false);
        }
        if (kind == WebhookConsumerKind.Instance || !IsDelegatedWebhookAction(request.Action))
            return (false, false, false, false, false);

        // AdminContext uses the FIRST visible membership, not Any(admin) and not permission
        // mappings. Preserve selected-tenant and navigation soft-delete filters, with no
        // approval, active-placement, actor-suspension or tenant-admin alternative.
        HumanPrincipalOwnerMembershipFact? Member(IEnumerable<HumanPrincipalOwnerMembershipFact> members, Guid id) =>
            members.FirstOrDefault(row => row.OwnerId == id && row.TenantId == selectedTenant
                && !row.IsDeleted && !row.PlacementIsDeleted && !row.OwnerIsDeleted);
        return kind switch
        {
            WebhookConsumerKind.Organization when TryResolveGuidAttribute(attributes, "organizationId", out var id) =>
                (false, false, Member(principal.Organizations, id)?.RoleId == (int)RoleEnum.OrgAdmin, false, false),
            WebhookConsumerKind.Group when TryResolveGuidAttribute(attributes, "groupId", out var id) =>
                (false, false, false, Member(principal.Groups, id)?.RoleId == (int)RoleEnum.GroupAdmin, false),
            WebhookConsumerKind.User when TryResolveGuidAttribute(attributes, "userId", out var id) =>
                (false, false, false, false, id == principal.PrincipalId),
            _ => (false, false, false, false, false)
        };
    }

    internal static bool EvaluatePreparedWebhook(HumanPrincipalSourceFacts principal,
        AuthorizationRequest request, Guid selectedTenant)
    {
        if (request.ResourceKind != ResourceKinds.Webhook || string.IsNullOrWhiteSpace(request.ResourceId))
            return false;
        var input = PreparedWebhookInputs(principal, request, selectedTenant);
        return input.Instance || input.Tenant || input.Organization || input.Group || input.Self;
    }
}
