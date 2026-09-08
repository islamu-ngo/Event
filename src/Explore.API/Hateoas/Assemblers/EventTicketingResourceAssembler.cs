using Explore.API.Hateoas.Policies;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventTicketing;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Assemblers;

public sealed class EventTicketCatalogManagementResourceAssembler
    : ResourceAssemblerBase<EventTicketCatalogManagementDto, EventTicketCatalogManagementDto>
{
    private readonly IHateoasLinkGenerator _linkGenerator;
    private readonly EventTicketCatalogManagementLinkPolicy _ticketingPolicy;
    private readonly IHateoasAuthorizationEvaluator _authorizationEvaluator;

    public EventTicketCatalogManagementResourceAssembler(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<EventTicketCatalogManagementDto> detailPolicy,
        ICollectionLinkPolicy<EventTicketCatalogManagementDto> collectionPolicy,
        EventTicketCatalogManagementLinkPolicy ticketingPolicy,
        IHateoasAuthorizationEvaluator authorizationEvaluator)
        : base(linkGenerator, detailPolicy, collectionPolicy)
    {
        _linkGenerator = linkGenerator;
        _ticketingPolicy = ticketingPolicy;
        _authorizationEvaluator = authorizationEvaluator;
    }

    public override async Task<HalResource<EventTicketCatalogManagementDto>> ToResource(
        EventTicketCatalogManagementDto dto,
        HttpContext httpContext)
    {
        if (IsMinimalResponse(httpContext))
        {
            return new HalResource<EventTicketCatalogManagementDto>(dto);
        }

        var user = ResolveCapabilityPrincipal(httpContext);
        IReadOnlyList<LinkDefinition> rootDefinitions =
            await GetDetailLinkDefinitionsAsync(dto, user, httpContext);
        var itemGroups = dto.TicketTypes
            .Select(ticket => _ticketingPolicy.GetTicketTypeLinks(dto, ticket).ToArray())
            .Concat(dto.CapacityPools.Select(pool => _ticketingPolicy.GetCapacityPoolLinks(dto, pool).ToArray()))
            .ToArray();
        var definitions = rootDefinitions.Concat(itemGroups.SelectMany(group => group)).ToArray();
        IReadOnlyList<bool> decisions = await _authorizationEvaluator.AreLinksAllowedAsync(
            definitions,
            user,
            httpContext);
        var groupIndex = 0;
        var decisionIndex = rootDefinitions.Count;

        return new HalResource<EventTicketCatalogManagementDto>
        {
            Data = dto,
            Links = MaterializeLinks(rootDefinitions, decisions, 0, httpContext, _linkGenerator),
            Embedded = new Dictionary<string, object>
            {
                ["ticket-types"] = dto.TicketTypes
                    .Select(ticket => new HalResource<EventTicketTypeDto>
                    {
                        Data = ticket,
                        Links = MaterializeLinks(itemGroups[groupIndex++], decisions, ref decisionIndex, httpContext, _linkGenerator)
                    })
                    .ToArray(),
                ["capacity-pools"] = dto.CapacityPools
                    .Select(pool => new HalResource<EventCapacityPoolDto>
                    {
                        Data = pool,
                        Links = MaterializeLinks(itemGroups[groupIndex++], decisions, ref decisionIndex, httpContext, _linkGenerator)
                    })
                    .ToArray()
            }
        };
    }

    private static Dictionary<string, HalLink> MaterializeLinks(
        IReadOnlyList<LinkDefinition> definitions,
        IReadOnlyList<bool> decisions,
        ref int decisionIndex,
        HttpContext httpContext,
        IHateoasLinkGenerator linkGenerator)
    {
        var links = new Dictionary<string, HalLink>();
        foreach (LinkDefinition definition in definitions)
        {
            if (decisionIndex < decisions.Count && decisions[decisionIndex])
            {
                HalLink? link = linkGenerator.GenerateLink(definition, httpContext);
                if (link is not null)
                {
                    links[definition.Rel] = link;
                }
            }

            decisionIndex++;
        }

        return links;
    }

    private static Dictionary<string, HalLink> MaterializeLinks(
        IReadOnlyList<LinkDefinition> definitions,
        IReadOnlyList<bool> decisions,
        int decisionIndex,
        HttpContext httpContext,
        IHateoasLinkGenerator linkGenerator) =>
        MaterializeLinks(definitions, decisions, ref decisionIndex, httpContext, linkGenerator);
}
