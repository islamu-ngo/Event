namespace Explore.API.Hateoas.Assemblers;

using Explore.API.Hateoas.Policies;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Http;

public sealed class InstanceTenantPlanResourceAssembler
    : ResourceAssemblerBase<InstanceTenantPlanDetailDto, InstanceTenantPlanListItemDto>
{
    public InstanceTenantPlanResourceAssembler(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<InstanceTenantPlanDetailDto> detailLinkPolicy,
        ICollectionLinkPolicy<InstanceTenantPlanListItemDto> collectionLinkPolicy)
        : base(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    {
    }

    public override async Task<HalResource<InstanceTenantPlanDetailDto>> ToResource(
        InstanceTenantPlanDetailDto dto,
        HttpContext httpContext)
    {
        foreach (InstanceTenantPlanVersionDto version in dto.Versions)
        {
            version.Links = null;
        }

        HalResource<InstanceTenantPlanDetailDto> resource = await base.ToResource(dto, httpContext);
        if (resource.Links.Count == 0)
        {
            return resource;
        }

        foreach (InstanceTenantPlanVersionDto version in dto.Versions)
        {
            Dictionary<string, HalLink> links = await GenerateLinks(
                InstanceTenantPlanVersionLinks.GetLinks(dto.Key, version),
                httpContext.User,
                httpContext);
            version.Links = links.Count == 0 ? null : links;
        }

        return resource;
    }
}
