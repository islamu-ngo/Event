namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Hateoas;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Link policy for TenantDto (detail view).
/// Provides links for tenant-related operations.
/// </summary>
public sealed class TenantDetailLinkPolicy : ILinkPolicy<TenantDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetLinks(TenantDto dto, ClaimsPrincipal? user)
    {
        // Self link
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetTenantById,
            new { id = dto.Id },
            "GET",
            dto.FullName);

        // Collection link
        yield return new LinkDefinition(
            LinkRelations.Collection,
            RouteNames.GetTenants,
            null,
            "GET",
            "All tenants");

        // Tenant user role grants link
        yield return new LinkDefinition(
            "tenant-user-role-grants",
            RouteNames.GetTenantUserRoleGrants,
            new { tenantId = dto.Id },
            "GET",
            "Tenant user role grants",
            RequiresAuth: true);
        // Edit link - requires authentication
        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateTenant,
            new { id = dto.Id },
            "PATCH",
            "Update tenant",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.Tenant, dto);

        // Delete link - requires authentication
        yield return new LinkDefinition(
            "delete",
            RouteNames.DeleteTenant,
            new { id = dto.Id },
            "DELETE",
            "Delete tenant",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Delete, ResourceDescriptors.Tenant, dto);

        yield return new LinkDefinition(
                LinkRelations.CreateConfigurationImportSession,
                RouteNames.CreateTenantConfigurationImportSession,
                new { tenantId = dto.Id },
                HttpMethods.Post,
                "Import tenant configuration package",
                RequiresAuth: true)
            .RequirePermission(
                AuthorizationActions.TenantSettings.Update,
                ResourceKinds.TenantSetting,
                CreateTenantConfigurationImportSessionCommand.ResourceKey,
                facts: new TenantSettingAuthorizationFacts(
                    dto.Id,
                    CreateTenantConfigurationImportSessionCommand.ResourceKey));

        yield return new LinkDefinition(
                LinkRelations.ExportTenantConfigurationPackage,
                RouteNames.ExportTenantConfigurationPackage,
                new { tenantId = dto.Id },
                HttpMethods.Get,
                "Export tenant configuration package",
                RequiresAuth: true)
            .RequirePermission(
                AuthorizationActions.TenantSettings.View,
                ResourceKinds.TenantSetting,
                ExportTenantConfigurationPackageQuery.ResourceKey,
                facts: new TenantSettingAuthorizationFacts(
                    dto.Id,
                    ExportTenantConfigurationPackageQuery.ResourceKey));
    }
}

/// <summary>
/// Link policy for TenantListDto (collection items).
/// </summary>
public sealed class TenantCollectionLinkPolicy : ICollectionLinkPolicy<TenantListDto>
{
    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetItemLinks(TenantListDto dto, ClaimsPrincipal? user)
    {
        // Self link for item
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetTenantById,
            new { id = dto.Id },
            "GET",
            dto.FullName);

    }

    /// <inheritdoc />
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        // Create link - requires authentication
        yield return new LinkDefinition(
            "create",
            RouteNames.CreateTenant,
            null,
            "POST",
            "Create new tenant",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Create, typeof(TenantDto), "tenant");
    }
}
