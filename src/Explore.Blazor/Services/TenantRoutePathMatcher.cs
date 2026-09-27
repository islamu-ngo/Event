using Microsoft.AspNetCore.Http;

namespace Explore.Blazor.Services;

internal static class TenantRoutePathMatcher
{
    public static bool TryMatch(
        PathString requestPath,
        string? configuredPathPrefix,
        out string tenantSlug,
        out PathString matchedPathBase,
        out PathString remainingPath,
        IReadOnlyCollection<string>? reservedSlugs = null)
    {
        tenantSlug = string.Empty;
        matchedPathBase = PathString.Empty;
        remainingPath = requestPath;

        var pathPrefix = NormalizePathPrefix(configuredPathPrefix);
        var remainingAfterPrefix = requestPath;
        if (pathPrefix is not null &&
            !requestPath.StartsWithSegments(pathPrefix, out remainingAfterPrefix))
        {
            return false;
        }

        var pathSegments = (remainingAfterPrefix.Value ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathSegments.Length == 0)
        {
            return false;
        }

        var slug = pathSegments[0];
        if (pathPrefix is null &&
            (slug.Length is < 3 or > 500 ||
             slug[0] == '-' ||
             slug[^1] == '-' ||
             slug.Contains("--", StringComparison.Ordinal) ||
             slug.Any(static character =>
                 character is not ('-' or >= '0' and <= '9' or >= 'a' and <= 'z')) ||
             reservedSlugs is null ||
             reservedSlugs.Count == 0 ||
             reservedSlugs.Contains(slug, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        tenantSlug = slug;
        matchedPathBase = new PathString(pathPrefix + "/" + tenantSlug);
        remainingPath = remainingAfterPrefix.StartsWithSegments(
            new PathString("/" + tenantSlug),
            out var pathAfterSlug)
            ? pathAfterSlug
            : PathString.Empty;

        return true;
    }

    private static string? NormalizePathPrefix(string? pathPrefix)
    {
        if (string.IsNullOrWhiteSpace(pathPrefix))
        {
            return null;
        }

        var normalized = pathPrefix.Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        var prefix = normalized.TrimEnd('/');
        return prefix.Length == 0 ? null : prefix;
    }
}
