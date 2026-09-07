using Microsoft.AspNetCore.Http;

namespace Explore.Blazor.Services;

internal static class TenantRoutePathMatcher
{
    public static bool TryMatch(
        PathString requestPath,
        string? configuredPathPrefix,
        out string tenantSlug,
        out PathString matchedPathBase,
        out PathString remainingPath)
    {
        tenantSlug = string.Empty;
        matchedPathBase = PathString.Empty;
        remainingPath = requestPath;

        var pathPrefix = NormalizePathPrefix(configuredPathPrefix);
        if (!requestPath.StartsWithSegments(pathPrefix, out var remainingAfterPrefix))
        {
            return false;
        }

        var pathSegments = (remainingAfterPrefix.Value ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathSegments.Length == 0)
        {
            return false;
        }

        tenantSlug = pathSegments[0];
        matchedPathBase = new PathString(pathPrefix + "/" + tenantSlug);
        remainingPath = remainingAfterPrefix.StartsWithSegments(
            new PathString("/" + tenantSlug),
            out var pathAfterSlug)
            ? pathAfterSlug
            : PathString.Empty;

        return true;
    }

    private static string NormalizePathPrefix(string? pathPrefix)
    {
        if (string.IsNullOrWhiteSpace(pathPrefix))
        {
            return "/t";
        }

        var normalized = pathPrefix.Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        return normalized.TrimEnd('/');
    }
}
