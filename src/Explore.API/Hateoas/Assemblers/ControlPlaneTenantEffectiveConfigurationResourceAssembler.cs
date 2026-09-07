namespace Explore.API.Hateoas.Assemblers;

using Explore.API.Hateoas.Policies;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Http;

public sealed class ControlPlaneTenantEffectiveConfigurationResourceAssembler
    : ResourceAssemblerBase<ControlPlaneTenantEffectiveConfigurationDto, ControlPlaneTenantEffectiveConfigurationDto>
{
    public ControlPlaneTenantEffectiveConfigurationResourceAssembler(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<ControlPlaneTenantEffectiveConfigurationDto> detailLinkPolicy,
        ICollectionLinkPolicy<ControlPlaneTenantEffectiveConfigurationDto> collectionLinkPolicy)
        : base(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    {
    }

    public override async Task<HalResource<ControlPlaneTenantEffectiveConfigurationDto>> ToResource(
        ControlPlaneTenantEffectiveConfigurationDto dto,
        HttpContext httpContext)
    {
        foreach (var setting in dto.Settings)
        {
            setting.Links = null;
        }

        var resource = await base.ToResource(dto, httpContext);
        if (resource.Links.Count == 0)
        {
            return resource;
        }

        foreach (var setting in dto.Settings)
        {
            var links = await GenerateLinks(
                ControlPlaneTenantEffectiveSettingLinks.GetLinks(dto.TenantId, setting),
                httpContext.User,
                httpContext);
            setting.Links = links.Count == 0 ? null : links;
        }

        return resource;
    }
}
