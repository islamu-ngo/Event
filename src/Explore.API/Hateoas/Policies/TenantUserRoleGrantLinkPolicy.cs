namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.TenantUserRoleGrant;
using Explore.Application.Hateoas;

/// <summary>
/// Link policy for TenantUserRoleGrantDto (detail view).
/// Provides links for tenant user role grant operations.
/// </summary>
public sealed class TenantUserRoleGrantDetailLinkPolicy : ILinkPolicy<TenantUserRoleGrantDto>
{
    public IEnumerable<LinkDefinition> GetLinks(TenantUserRoleGrantDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetTenantUserRoleGrantById,
            new { id = dto.Id },
            "GET",
            $"{dto.UserFullName} - {dto.RoleName}");

        yield return new LinkDefinition(
            "tenant",
            RouteNames.GetTenantById,
            new { id = dto.TenantId },
            "GET",
            dto.TenantFullName);

        yield return new LinkDefinition(
            LinkRelations.Revoke,
            RouteNames.RevokeTenantUserRoleGrant,
            new { id = dto.Id },
            "DELETE",
            "Revoke role grant",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Delete, ResourceDescriptors.TenantUserRoleGrant, dto);
    }
}

/// <summary>
/// Link policy for TenantUserRoleGrantListDto (collection items).
/// </summary>
public sealed class TenantUserRoleGrantCollectionLinkPolicy : ICollectionLinkPolicy<TenantUserRoleGrantListDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(TenantUserRoleGrantListDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetTenantUserRoleGrantById,
            new { id = dto.Id },
            "GET",
            $"{dto.UserFullName} - {dto.RoleName}");

    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Create,
            RouteNames.CreateTenantUserRoleGrant,
            null,
            "POST",
            "Grant tenant role",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Create, ResourceKinds.TenantUserRoleGrant);
    }
}
