using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Registration;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Assemblers;

/// <summary>Rechecks filename retention after HAL authorization without changing release authority.</summary>
public sealed class RegistrationAnswerFileResourceAssembler(
    IHateoasLinkGenerator linkGenerator,
    ILinkPolicy<RegistrationAnswerFileDto> detailLinkPolicy,
    ICollectionLinkPolicy<RegistrationAnswerFileDto> collectionLinkPolicy,
    TimeProvider timeProvider)
    : ResourceAssemblerBase<RegistrationAnswerFileDto>(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
{
    public override async Task<HalResource<RegistrationAnswerFileDto>> ToResource(
        RegistrationAnswerFileDto dto, HttpContext httpContext)
    {
        var resource = await base.ToResource(dto, httpContext);
        return new(dto.ForDisclosureAt(timeProvider.GetUtcNow().UtcDateTime), resource.Links);
    }
}
