namespace Explore.API.Hateoas;

using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;  // For ILinkPolicy, ICollectionLinkPolicy
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Base class for resource assemblers that provides common link generation logic.
/// Implements a four-phase capability planning pipeline:
/// 1. Candidate links — link policies yield link definitions for each resource
/// 2. Normalized checks — evaluator extracts authorization checks from permission-bearing links
/// 3. Batch decisions — deduplicated checks are sent to IAuthorizationProvider in a single call
/// 4. Materialized links — allowed links are resolved to URLs via HateoasLinkGenerator
/// </summary>
/// <typeparam name="TDto">The detail DTO type.</typeparam>
/// <typeparam name="TListDto">The list DTO type.</typeparam>
public abstract class ResourceAssemblerBase<TDto, TListDto> : IResourceAssembler<TDto, TListDto>
    where TDto : class
    where TListDto : class
{
    private readonly IHateoasLinkGenerator _linkGenerator;
    private readonly ILinkPolicy<TDto> _detailLinkPolicy;
    private readonly ICollectionLinkPolicy<TListDto> _collectionLinkPolicy;

    protected IHateoasLinkGenerator LinkGenerator => _linkGenerator;

    protected ResourceAssemblerBase(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<TDto> detailLinkPolicy,
        ICollectionLinkPolicy<TListDto> collectionLinkPolicy)
    {
        _linkGenerator = linkGenerator;
        _detailLinkPolicy = detailLinkPolicy;
        _collectionLinkPolicy = collectionLinkPolicy;
    }

    /// <inheritdoc />
    public virtual async Task<HalResource<TDto>> ToResource(TDto dto, HttpContext httpContext)
    {
        if (IsMinimalResponse(httpContext))
        {
            return new HalResource<TDto>(dto);
        }

        var user = ResolveCapabilityPrincipal(httpContext);
        var definitions = await GetDetailLinkDefinitionsAsync(dto, user, httpContext);
        var links = await GenerateLinks(definitions, user, httpContext);

        return new HalResource<TDto>
        {
            Data = dto,
            Links = links,
            Embedded = GetEmbeddedResources(dto, httpContext)
        };
    }

    /// <inheritdoc />
    public virtual async Task<HalResource<TListDto>> ToListResource(TListDto dto, HttpContext httpContext)
    {
        if (IsMinimalResponse(httpContext))
        {
            return new HalResource<TListDto>(dto);
        }

        var user = ResolveCapabilityPrincipal(httpContext);
        var definitions = await GetListItemLinkDefinitionsAsync(dto, user, httpContext);
        var links = await GenerateLinks(definitions, user, httpContext);

        return new HalResource<TListDto>
        {
            Data = dto,
            Links = links
        };
    }

    /// <inheritdoc />
    public virtual async Task<HalCollectionResource<TListDto>> ToCollectionResource(
        PaginatedResult<TListDto> paginatedResult,
        string routeName,
        object? additionalRouteValues,
        HttpContext httpContext)
    {
        var user = ResolveCapabilityPrincipal(httpContext);
        var isMinimal = IsMinimalResponse(httpContext);

        // Minimal response: wrap items without link generation (skip auth evaluation entirely)
        if (isMinimal)
        {
            var minimalItems = paginatedResult.Items
                .Select(item => new HalResource<TListDto>(item))
                .ToList();

            return new HalCollectionResource<TListDto>
            {
                PageNumber = paginatedResult.PageNumber,
                PageSize = paginatedResult.PageSize,
                TotalCount = paginatedResult.TotalCount,
                TotalPages = paginatedResult.TotalPages,
                Embedded = new HalCollectionEmbedded<TListDto> { Items = minimalItems }
            };
        }

        // Full response: candidate links → batch auth → materialized links
        var items = await BuildListResourcesWithBatch(paginatedResult.Items, user, httpContext);

        // Generate pagination links
        var links = _linkGenerator.GeneratePaginationLinks(
            routeName,
            paginatedResult.PageNumber,
            paginatedResult.PageSize,
            paginatedResult.TotalPages,
            additionalRouteValues,
            httpContext);

        // Add collection-level links (create, search, etc.) — separate batch for collection actions
        var authorizationContext = additionalRouteValues as ICollectionAuthorizationContext;
        var collectionActionLinks = await GenerateLinks(
            _collectionLinkPolicy.GetCollectionLinks(user, authorizationContext),
            user,
            httpContext);
        foreach (var pair in collectionActionLinks)
        {
            links[pair.Key] = pair.Value;
        }

        EnsureCollectionSelfLink(links, httpContext);

        return HalCollectionResource<TListDto>.FromPagination(
            items,
            paginatedResult.PageNumber,
            paginatedResult.PageSize,
            paginatedResult.TotalCount,
            paginatedResult.TotalPages,
            links);
    }

    /// <inheritdoc />
    public virtual async Task<HalCollectionResource<TListDto>> ToCollectionResource(
        IEnumerable<TListDto> items,
        string routeName,
        object? additionalRouteValues,
        HttpContext httpContext)
    {
        var itemsList = items.ToList();
        var user = ResolveCapabilityPrincipal(httpContext);
        var isMinimal = IsMinimalResponse(httpContext);

        // Minimal response: wrap items without link generation
        if (isMinimal)
        {
            var minimalItems = itemsList
                .Select(item => new HalResource<TListDto>(item))
                .ToList();

            return new HalCollectionResource<TListDto>
            {
                PageNumber = 1,
                PageSize = itemsList.Count,
                TotalCount = itemsList.Count,
                TotalPages = 1,
                Embedded = new HalCollectionEmbedded<TListDto> { Items = minimalItems }
            };
        }

        var halItems = await BuildListResourcesWithBatch(itemsList, user, httpContext);

        var links = new Dictionary<string, HalLink>();

        // Self link for the collection
        var selfPath = _linkGenerator.GeneratePath(routeName, additionalRouteValues, httpContext);
        if (selfPath is not null)
        {
            links[LinkRelations.Self] = HalLink.Create(selfPath);
        }

        // Collection-level links
        var authorizationContext = additionalRouteValues as ICollectionAuthorizationContext;
        var collectionActionLinks = await GenerateLinks(
            _collectionLinkPolicy.GetCollectionLinks(user, authorizationContext),
            user,
            httpContext);
        foreach (var pair in collectionActionLinks)
        {
            links[pair.Key] = pair.Value;
        }

        EnsureCollectionSelfLink(links, httpContext);

        return new HalCollectionResource<TListDto>
        {
            PageNumber = 1,
            PageSize = itemsList.Count,
            TotalCount = itemsList.Count,
            TotalPages = 1,
            Links = links,
            Embedded = new HalCollectionEmbedded<TListDto> { Items = halItems }
        };
    }

    public virtual Task<HalCollectionResource<TListDto>> ToCollectionResource(
        IEnumerable<TListDto> items,
        string routeName,
        HttpContext httpContext)
    {
        return ToCollectionResource(items, routeName, null, httpContext);
    }

    /// <summary>
    /// Override to provide embedded resources for detail views.
    /// </summary>
    protected virtual Dictionary<string, object>? GetEmbeddedResources(TDto dto, HttpContext httpContext)
    {
        return null;
    }

    protected virtual ClaimsPrincipal? ResolveCapabilityPrincipal(HttpContext httpContext)
        => httpContext.User;

    protected virtual Task<IReadOnlyList<LinkDefinition>> GetDetailLinkDefinitionsAsync(
        TDto dto,
        ClaimsPrincipal? user,
        HttpContext httpContext) =>
        Task.FromResult<IReadOnlyList<LinkDefinition>>(_detailLinkPolicy.GetLinks(dto, user).ToList());

    protected virtual Task<IReadOnlyList<LinkDefinition>> GetListItemLinkDefinitionsAsync(
        TListDto dto,
        ClaimsPrincipal? user,
        HttpContext httpContext) =>
        Task.FromResult<IReadOnlyList<LinkDefinition>>(_collectionLinkPolicy.GetItemLinks(dto, user).ToList());

    protected virtual Task<IReadOnlyList<IReadOnlyList<LinkDefinition>>> GetCollectionItemLinkDefinitionsAsync(
        IReadOnlyList<TListDto> items,
        ClaimsPrincipal? user,
        HttpContext httpContext) =>
        Task.FromResult<IReadOnlyList<IReadOnlyList<LinkDefinition>>>(items
            .Select(item => (IReadOnlyList<LinkDefinition>)_collectionLinkPolicy.GetItemLinks(item, user).ToList())
            .ToList());

    private static void EnsureCollectionSelfLink(Dictionary<string, HalLink> links, HttpContext httpContext)
    {
        if (links.ContainsKey(LinkRelations.Self))
        {
            return;
        }

        var requestPath = httpContext.Request.Path + httpContext.Request.QueryString;
        if (string.IsNullOrWhiteSpace(requestPath))
        {
            return;
        }

        links[LinkRelations.Self] = HalLink.Create(requestPath);
    }

    /// <summary>
    /// Generates HAL links from link definitions, filtering by authorization.
    /// Pipeline: definitions → evaluator (static + batch auth with dedup) → link generator (URL resolution).
    /// </summary>
    protected async Task<Dictionary<string, HalLink>> GenerateLinks(
        IEnumerable<LinkDefinition> definitions,
        ClaimsPrincipal? user,
        HttpContext httpContext)
    {
        var definitionList = definitions.ToList();
        if (definitionList.Count == 0)
            return new Dictionary<string, HalLink>();

        var evaluator = httpContext.RequestServices.GetRequiredService<IHateoasAuthorizationEvaluator>();
        var decisions = await evaluator.AreLinksAllowedAsync(definitionList, user, httpContext);

        var links = new Dictionary<string, HalLink>();

        for (var index = 0; index < definitionList.Count; index++)
        {
            if (index >= decisions.Count || !decisions[index])
                continue;

            var definition = definitionList[index];

            var link = _linkGenerator.GenerateLink(definition, httpContext);
            if (link is not null)
            {
                links[definition.Rel] = link;
            }
        }

        return links;
    }

    /// <summary>
    /// Builds HAL resources for list items with batched link authorization.
    /// All item link definitions are flattened into a single batch for the evaluator,
    /// which deduplicates identical checks before calling the authorization provider.
    /// </summary>
    private async Task<List<HalResource<TListDto>>> BuildListResourcesWithBatch(
        IEnumerable<TListDto> items,
        ClaimsPrincipal? user,
        HttpContext httpContext)
    {
        var itemList = items.ToList();
        if (itemList.Count == 0)
            return [];

        // Phase 1: Collect candidate link definitions for every item
        var definitionsByItem = await GetCollectionItemLinkDefinitionsAsync(itemList, user, httpContext);

        // Phase 2-3: Flatten and batch evaluate (evaluator handles dedup internally)
        var flattenedDefinitions = definitionsByItem.SelectMany(x => x).ToList();
        var evaluator = httpContext.RequestServices.GetRequiredService<IHateoasAuthorizationEvaluator>();
        var decisions = await evaluator.AreLinksAllowedAsync(flattenedDefinitions, user, httpContext);

        // Phase 4: Materialize allowed links into HAL resources
        var resources = new List<HalResource<TListDto>>(itemList.Count);
        var cursor = 0;

        for (var itemIndex = 0; itemIndex < itemList.Count; itemIndex++)
        {
            var itemDefinitions = definitionsByItem[itemIndex];
            var links = new Dictionary<string, HalLink>();

            for (var definitionIndex = 0; definitionIndex < itemDefinitions.Count; definitionIndex++)
            {
                var globalIndex = cursor + definitionIndex;
                if (globalIndex >= decisions.Count || !decisions[globalIndex])
                    continue;

                var definition = itemDefinitions[definitionIndex];
                var link = _linkGenerator.GenerateLink(definition, httpContext);
                if (link is not null)
                {
                    links[definition.Rel] = link;
                }
            }

            cursor += itemDefinitions.Count;
            resources.Add(new HalResource<TListDto>
            {
                Data = itemList[itemIndex],
                Links = links
            });
        }

        return resources;
    }

    /// <summary>
    /// Checks if the client requested minimal response (Prefer: return=minimal).
    /// </summary>
    protected static bool IsMinimalResponse(HttpContext httpContext)
    {
        return httpContext.Items.TryGetValue(HateoasConstants.MinimalResponseKey, out var value)
            && value is true;
    }
}

/// <summary>
/// Base class for resource assemblers with a single DTO type.
/// </summary>
/// <typeparam name="TDto">The DTO type used for both detail and list views.</typeparam>
public abstract class ResourceAssemblerBase<TDto> : ResourceAssemblerBase<TDto, TDto>, IResourceAssembler<TDto>
    where TDto : class
{
    protected ResourceAssemblerBase(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<TDto> detailLinkPolicy,
        ICollectionLinkPolicy<TDto> collectionLinkPolicy)
        : base(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    {
    }
}
