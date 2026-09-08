namespace Explore.API.Hateoas.Assemblers;

using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Hateoas;
using Explore.Application.Responses;

/// <summary>
/// Resource assembler for StorageObject entities.
/// Converts StorageObjectDto and StorageObjectListDto to HAL resources with appropriate links.
/// </summary>
public sealed class StorageObjectResourceAssembler : ResourceAssemblerBase<StorageObjectDto, StorageObjectListDto>
{
    private readonly TimeProvider _timeProvider;

    public StorageObjectResourceAssembler(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<StorageObjectDto> detailLinkPolicy,
        ICollectionLinkPolicy<StorageObjectListDto> collectionLinkPolicy,
        TimeProvider timeProvider)
        : base(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    {
        _timeProvider = timeProvider;
    }

    public override async Task<HalResource<StorageObjectDto>> ToResource(StorageObjectDto dto, HttpContext httpContext)
    {
        var resource = await base.ToResource(dto, httpContext);
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        ApplyDisclosureBoundary(resource.Links, dto.ContentEligibility, utcNow);
        return new(dto.ForDisclosureAt(utcNow), resource.Links);
    }

    public override async Task<HalResource<StorageObjectListDto>> ToListResource(StorageObjectListDto dto, HttpContext httpContext)
    {
        var resource = await base.ToListResource(dto, httpContext);
        return ApplyDisclosureBoundary(resource);
    }

    public override async Task<HalCollectionResource<StorageObjectListDto>> ToCollectionResource(
        PaginatedResult<StorageObjectListDto> paginatedResult, string routeName,
        object? additionalRouteValues, HttpContext httpContext)
    {
        var resource = await base.ToCollectionResource(paginatedResult, routeName, additionalRouteValues, httpContext);
        ApplyDisclosureBoundary(resource);
        return resource;
    }

    public override async Task<HalCollectionResource<StorageObjectListDto>> ToCollectionResource(
        IEnumerable<StorageObjectListDto> items, string routeName,
        object? additionalRouteValues, HttpContext httpContext)
    {
        var resource = await base.ToCollectionResource(items, routeName, additionalRouteValues, httpContext);
        ApplyDisclosureBoundary(resource);
        return resource;
    }

    private void ApplyDisclosureBoundary(HalCollectionResource<StorageObjectListDto> resource)
    {
        for (var index = 0; index < resource.Embedded.Items.Count; index++)
            resource.Embedded.Items[index] = ApplyDisclosureBoundary(resource.Embedded.Items[index]);
    }

    private HalResource<StorageObjectListDto> ApplyDisclosureBoundary(HalResource<StorageObjectListDto> resource)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        ApplyDisclosureBoundary(resource.Links, resource.Data.ContentEligibility, utcNow);
        return new(resource.Data.ForDisclosureAt(utcNow), resource.Links);
    }

    private static void ApplyDisclosureBoundary(
        Dictionary<string, HalLink> links, StorageObjectContentEligibilityDto eligibility, DateTime utcNow)
    {
        // Authorization may have awaited past the bound after candidates were captured.
        if (!eligibility.CanReadAt(utcNow))
        {
            links.Remove("content");
            links.Remove("public-image");
            links.Remove("presigned-download");
            if (links.TryGetValue(LinkRelations.Self, out var self))
                links[LinkRelations.Self] = new HalLink { Href = self.Href, Method = self.Method };
        }
        else if (!eligibility.PresignedDownloadAllowed)
        {
            links.Remove("presigned-download");
        }
    }
}
