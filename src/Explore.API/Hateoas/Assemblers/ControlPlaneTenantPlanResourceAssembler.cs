namespace Explore.API.Hateoas.Assemblers;

using Explore.API.Hateoas.Policies;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Http;

public sealed class ControlPlaneTenantPlanResourceAssembler
    : ResourceAssemblerBase<ControlPlaneTenantPlanDetailDto, ControlPlaneTenantPlanListItemDto>
{
    public ControlPlaneTenantPlanResourceAssembler(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<ControlPlaneTenantPlanDetailDto> detailLinkPolicy,
        ICollectionLinkPolicy<ControlPlaneTenantPlanListItemDto> collectionLinkPolicy)
        : base(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    {
    }

    public override async Task<HalResource<ControlPlaneTenantPlanDetailDto>> ToResource(
        ControlPlaneTenantPlanDetailDto dto,
        HttpContext httpContext)
    {
        foreach (ControlPlaneTenantPlanVersionDto version in dto.Versions)
        {
            version.Links = null;
        }

        HalResource<ControlPlaneTenantPlanDetailDto> resource = await base.ToResource(dto, httpContext);
        if (resource.Links.Count == 0)
        {
            return resource;
        }

        foreach (ControlPlaneTenantPlanVersionDto version in dto.Versions)
        {
            Dictionary<string, HalLink> links = await GenerateLinks(
                ControlPlaneTenantPlanVersionLinks.GetLinks(dto.Key, version),
                httpContext.User,
                httpContext);
            version.Links = links.Count == 0 ? null : links;
        }

        return resource;
    }
}
