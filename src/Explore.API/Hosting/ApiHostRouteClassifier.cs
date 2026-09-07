using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;

namespace Explore.API.Hosting;

public sealed class ApiHostRouteClassifier(
    IEnumerable<EndpointDataSource> endpointDataSources,
    string? mcpPath,
    string? schedulerPath)
{
    private static readonly string[] ToolingPaths = ["/swagger", "/openapi", "/scalar"];
    private static readonly string[] OperationalPaths = ["/health", "/alive", "/metrics"];

    public bool IsApiOwned(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return IsApiOwned(context.Request.Path);
    }

    public bool IsApiOwned(PathString path) =>
        IsExplicitApiPath(path) || endpointDataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Any(endpoint => IsApiOwned(endpoint) && Matches(endpoint, path));

    public bool IsApiOwned(RouteEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.Assembly
            == typeof(ApiHostApplicationExtensions).Assembly)
        {
            return true;
        }

        var rawText = endpoint.RoutePattern.RawText;
        return !string.IsNullOrWhiteSpace(rawText) &&
            IsExplicitApiPath(new PathString('/' + rawText.TrimStart('~', '/')));
    }

    public static bool HasApiPrefix(PathString path) => StartsWith(path, "/api");

    private bool IsExplicitApiPath(PathString path) =>
        HasApiPrefix(path) ||
        StartsWith(path, mcpPath) ||
        StartsWith(path, schedulerPath) ||
        ToolingPaths.Any(toolingPath => StartsWith(path, toolingPath)) ||
        OperationalPaths.Any(operationalPath => StartsWith(path, operationalPath));

    private static bool Matches(RouteEndpoint endpoint, PathString path)
    {
        var rawText = endpoint.RoutePattern.RawText;
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return false;
        }

        var template = rawText.TrimStart('~', '/');
        return new TemplateMatcher(TemplateParser.Parse(template), new RouteValueDictionary())
            .TryMatch(path, new RouteValueDictionary());
    }

    private static bool StartsWith(PathString path, string? candidate) =>
        !string.IsNullOrWhiteSpace(candidate) &&
        path.StartsWithSegments(candidate, StringComparison.OrdinalIgnoreCase);
}
