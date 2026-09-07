namespace Explore.API.Hateoas.Assemblers;

using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.TenantUserRoleGrant;

/// <summary>
/// Resource assembler for tenant user role grant resources.
/// Converts TenantUserRoleGrantDto and TenantUserRoleGrantListDto to HAL resources with appropriate links.
/// </summary>
public sealed class TenantUserRoleGrantResourceAssembler : ResourceAssemblerBase<TenantUserRoleGrantDto, TenantUserRoleGrantListDto>
{
    public TenantUserRoleGrantResourceAssembler(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<TenantUserRoleGrantDto> detailLinkPolicy,
        ICollectionLinkPolicy<TenantUserRoleGrantListDto> collectionLinkPolicy)
        : base(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    {
    }

    protected override Dictionary<string, object>? GetEmbeddedResources(
        TenantUserRoleGrantDto dto,
        Microsoft.AspNetCore.Http.HttpContext httpContext)
    {
        return null;
    }
}
