namespace Explore.API.Hateoas.Policies;

using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Footer;
using Explore.Application.Hateoas;

public sealed class TenantFooterSettingsLinkPolicy : ILinkPolicy<TenantFooterSettingsDto>
{
    public IEnumerable<LinkDefinition> GetLinks(TenantFooterSettingsDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetTenantFooterSettings,
            null,
            "GET",
            "Tenant footer settings",
            RequiresAuth: true);

        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.PatchTenantFooterSettings,
            null,
            "PATCH",
            "Patch tenant footer settings",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update,
                ResourceKinds.Tenant,
                dto.TenantId.ToString("D"),
                new AuthorizationScope(TenantId: dto.TenantId.ToString("D")),
                new TenantScopedAuthorizationFacts(dto.TenantId));

        if (!dto.LockTenantLinkGroups)
        {
            yield return new LinkDefinition(
                "manage-link-groups",
                RouteNames.GetFooterLinkGroups,
                null,
                "GET",
                "Manage footer link groups",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Update,
                    ResourceKinds.Tenant,
                    dto.TenantId.ToString("D"),
                    new AuthorizationScope(TenantId: dto.TenantId.ToString("D")),
                    new TenantScopedAuthorizationFacts(dto.TenantId));
        }
    }
}

public sealed class TenantFooterSettingsCollectionLinkPolicy : ICollectionLinkPolicy<TenantFooterSettingsDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(TenantFooterSettingsDto dto, ClaimsPrincipal? user) => [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
