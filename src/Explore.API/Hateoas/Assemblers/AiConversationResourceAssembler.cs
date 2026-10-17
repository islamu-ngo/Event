namespace Explore.API.Hateoas.Assemblers;

using Explore.API.Hateoas.Policies;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Ai;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Http;

public sealed class AiConversationResourceAssembler : ResourceAssemblerBase<AiConversationDto, AiConversationSummaryDto>
{
    private readonly TimeProvider _timeProvider;

    public AiConversationResourceAssembler(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<AiConversationDto> detailLinkPolicy,
        ICollectionLinkPolicy<AiConversationSummaryDto> collectionLinkPolicy,
        TimeProvider timeProvider)
        : base(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    {
        _timeProvider = timeProvider;
    }

    public override async Task<HalResource<AiConversationDto>> ToResource(AiConversationDto dto, HttpContext httpContext)
    {
        var resource = await base.ToResource(dto, httpContext);
        if (resource.Links.Count == 0)
        {
            return resource;
        }

        foreach (var action in dto.ProposedActions)
        {
            var links = await GenerateLinks(AiProposedActionLinkPolicy.GetLinks(dto, action,
                _timeProvider.GetUtcNow().UtcDateTime), httpContext.User, httpContext);
            action.Links = links.Count == 0 ? null : links;
        }

        return resource;
    }
}
