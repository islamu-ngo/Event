using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Helpers;

public static class CanonicalUrlHelper
{
    public static string Build(NavigationManager navigation, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(navigation);

        if (!string.IsNullOrWhiteSpace(path))
        {
            return BuildFromPath(navigation.BaseUri, path);
        }

        var current = navigation.ToAbsoluteUri(navigation.Uri);
        var builder = new UriBuilder(current)
        {
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

    private static string BuildFromPath(string baseUri, string path)
    {
        var normalizedBase = baseUri.TrimEnd('/');
        var normalizedPath = path.StartsWith('/') ? path : string.Concat('/', path);
        return string.Concat(normalizedBase, normalizedPath).TrimEnd('/');
    }
}
