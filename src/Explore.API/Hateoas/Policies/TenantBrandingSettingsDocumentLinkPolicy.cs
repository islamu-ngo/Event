namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Application.Hateoas;

public sealed class TenantBrandingSettingsDocumentLinkPolicy : ILinkPolicy<TenantBrandingSettingsDocumentDto>
{
    public IEnumerable<LinkDefinition> GetLinks(TenantBrandingSettingsDocumentDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetTenantBrandingSettingsDocument,
            null,
            "GET",
            "Tenant branding settings document");

        if (!dto.CanChangeDisplayName &&
            !dto.CanChangeLogoUrl &&
            !dto.CanChangeFaviconUrl &&
            !dto.CanChangeCustomCssUrl)
        {
            yield break;
        }

        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.PatchTenantBrandingSettingsDocument,
            null,
            "PATCH",
            "Patch tenant branding settings",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.TenantBrandingSettingsDocument, dto);
    }
}

public sealed class TenantBrandingSettingsDocumentCollectionLinkPolicy : ICollectionLinkPolicy<TenantBrandingSettingsDocumentDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(TenantBrandingSettingsDocumentDto dto, ClaimsPrincipal? user) => [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
