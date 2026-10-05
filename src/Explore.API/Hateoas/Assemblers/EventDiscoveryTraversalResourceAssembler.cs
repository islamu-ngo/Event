using System.Collections.Immutable;
using Explore.API.Models;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.WebUtilities;

namespace Explore.API.Hateoas.Assemblers;

public sealed class EventDiscoveryTraversalResourceAssembler(
    IResourceAssembler<EventDiscoveryItemDto> items,
    IHateoasLinkGenerator linkGenerator)
{
    public async Task<EventDiscoveryTraversalResource> ToResource(
        EventDiscoveryTraversalDto traversal, HttpContext httpContext)
    {
        var representation = await items.ToCollectionResource(traversal.Items, RouteNames.GetEvents, httpContext);
        var links = representation.Links
            .Where(link => link.Key != LinkRelations.Self)
            .Concat(Navigation(traversal, httpContext))
            .ToImmutableDictionary(link => link.Key, link => link.Value, StringComparer.Ordinal);
        return new(traversal.SnapshotCount, traversal.Truncated, traversal.ExpiresAt,
            traversal.HasMore, links, representation.Embedded);
    }

    private IEnumerable<KeyValuePair<string, HalLink>> Navigation(
        EventDiscoveryTraversalDto traversal, HttpContext httpContext)
    {
        string? path = linkGenerator.GeneratePath(RouteNames.GetEvents, null, httpContext);
        if (path is null)
            yield break;
        var query = httpContext.Request.Query.ToDictionary(pair => pair.Key, pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        yield return new(LinkRelations.Self, HalLink.Create(QueryHelpers.AddQueryString(path, query)));
        if (traversal.HasMore && traversal.NextCursor is not null)
        {
            query["cursor"] = traversal.NextCursor;
            yield return new(LinkRelations.Next, HalLink.Create(QueryHelpers.AddQueryString(path, query)));
        }
    }
}
