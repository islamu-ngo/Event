using Explore.API.Hateoas.Policies;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventAddOns;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Assemblers;

public sealed class RegistrationOrderAddOnResourceAssembler(
    IHateoasLinkGenerator linkGenerator,
    ILinkPolicy<RegistrationOrderAddOnSummaryDto> detailPolicy,
    ICollectionLinkPolicy<RegistrationOrderAddOnSummaryDto> collectionPolicy,
    RegistrationOrderAddOnLineLinkPolicy linePolicy) :
    ResourceAssemblerBase<
        RegistrationOrderAddOnSummaryDto,
        RegistrationOrderAddOnSummaryDto>(
        linkGenerator,
        detailPolicy,
        collectionPolicy)
{
    public override async Task<HalResource<RegistrationOrderAddOnSummaryDto>> ToResource(
        RegistrationOrderAddOnSummaryDto dto,
        HttpContext httpContext)
    {
        HalResource<RegistrationOrderAddOnSummaryDto> resource =
            await base.ToResource(dto, httpContext);
        if (IsMinimalResponse(httpContext) || dto.Lines.Count == 0)
        {
            return resource;
        }

        var embedded = new List<HalResource<RegistrationOrderAddOnLineDto>>(
            dto.Lines.Count);
        foreach (RegistrationOrderAddOnLineDto line in dto.Lines)
        {
            Dictionary<string, HalLink> links = await GenerateLinks(
                linePolicy.GetLinks(line, httpContext.User),
                httpContext.User,
                httpContext);
            embedded.Add(new HalResource<RegistrationOrderAddOnLineDto>
            {
                Data = line,
                Links = links,
            });
        }

        return new HalResource<RegistrationOrderAddOnSummaryDto>
        {
            Data = resource.Data,
            Links = resource.Links,
            Embedded = new Dictionary<string, object>
            {
                ["add-ons"] = embedded,
            },
        };
    }
}
