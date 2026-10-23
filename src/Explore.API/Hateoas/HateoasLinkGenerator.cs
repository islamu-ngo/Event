namespace Explore.API.Hateoas;

using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>
/// Implementation of IHateoasLinkGenerator using ASP.NET Core's LinkGenerator.
/// </summary>
public sealed class HateoasLinkGenerator : IHateoasLinkGenerator
{
    private readonly LinkGenerator _linkGenerator;
    private readonly ILogger<HateoasLinkGenerator> _logger;

    public HateoasLinkGenerator(LinkGenerator linkGenerator, ILogger<HateoasLinkGenerator> logger)
    {
        _linkGenerator = linkGenerator;
        _logger = logger;
    }

    /// <inheritdoc />
    public HalLink? GenerateLink(LinkDefinition definition, HttpContext httpContext)
    {
        var path = GeneratePath(definition.RouteName, definition.RouteValues, httpContext);

        if (path is null)
        {
            _logger.LogWarning("Failed to generate link for route.");
            return null;
        }

        return new HalLink
        {
            Href = path,
            Method = definition.Method,
            Title = definition.Title
        };
    }

    /// <inheritdoc />
    public string? GeneratePath(string routeName, object? routeValues, HttpContext httpContext)
    {
        try
        {
            return _linkGenerator.GetPathByRouteValues(
                httpContext,
                routeName,
                routeValues);
        }
        catch (Exception)
        {
            _logger.LogWarning("Exception generating path for route.");
            return null;
        }
    }

    /// <inheritdoc />
    public Dictionary<string, HalLink> GeneratePaginationLinks(
        string routeName,
        int pageNumber,
        int pageSize,
        int totalPages,
        object? additionalRouteValues,
        HttpContext httpContext)
    {
        var links = new Dictionary<string, HalLink>();

        // Helper to merge route values
        RouteValueDictionary MergeRouteValues(int targetPage)
        {
            var values = additionalRouteValues is not null
                ? new RouteValueDictionary(additionalRouteValues)
                : new RouteValueDictionary();

            values["pageNumber"] = targetPage;
            values["pageSize"] = pageSize;
            return values;
        }

        // Self link
        var selfPath = GeneratePath(routeName, MergeRouteValues(pageNumber), httpContext);
        if (selfPath is not null)
        {
            links[LinkRelations.Self] = HalLink.Create(selfPath);
        }

        // First link
        var firstPath = GeneratePath(routeName, MergeRouteValues(1), httpContext);
        if (firstPath is not null)
        {
            links[LinkRelations.First] = HalLink.Create(firstPath);
        }

        // Previous link (only if not on first page)
        if (pageNumber > 1)
        {
            var prevPath = GeneratePath(routeName, MergeRouteValues(pageNumber - 1), httpContext);
            if (prevPath is not null)
            {
                links[LinkRelations.Prev] = HalLink.Create(prevPath);
            }
        }

        // Next link (only if not on last page)
        if (pageNumber < totalPages)
        {
            var nextPath = GeneratePath(routeName, MergeRouteValues(pageNumber + 1), httpContext);
            if (nextPath is not null)
            {
                links[LinkRelations.Next] = HalLink.Create(nextPath);
            }
        }

        // Last link
        if (totalPages > 0)
        {
            var lastPath = GeneratePath(routeName, MergeRouteValues(totalPages), httpContext);
            if (lastPath is not null)
            {
                links[LinkRelations.Last] = HalLink.Create(lastPath);
            }
        }

        return links;
    }
}
