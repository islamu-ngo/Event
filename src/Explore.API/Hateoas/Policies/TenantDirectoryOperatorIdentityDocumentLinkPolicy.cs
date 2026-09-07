namespace Explore.API.Hateoas.Policies;

using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Application.Hateoas;

public sealed class TenantDirectoryOperatorIdentityDocumentLinkPolicy :
    ILinkPolicy<TenantDirectoryOperatorIdentityDocumentDto>
{
    public IEnumerable<LinkDefinition> GetLinks(
        TenantDirectoryOperatorIdentityDocumentDto dto,
        ClaimsPrincipal? user)
    {
        _ = dto;
        _ = user;
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetTenantDirectoryOperatorIdentityDocument,
            null,
            "GET",
            "Tenant directory operator identity");

        if (!dto.CanEdit)
        {
            yield break;
        }

        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.PatchTenantDirectoryOperatorIdentityDocument,
            null,
            "PATCH",
            "Patch tenant directory operator identity",
            RequiresAuth: true)
            .RequirePermission(
                AuthorizationActions.Update,
                ResourceDescriptors.TenantDirectoryOperatorIdentityDocument,
                dto);
    }
}

public sealed class TenantDirectoryOperatorIdentityDocumentCollectionLinkPolicy :
    ICollectionLinkPolicy<TenantDirectoryOperatorIdentityDocumentDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(
        TenantDirectoryOperatorIdentityDocumentDto dto,
        ClaimsPrincipal? user) => [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
