namespace Explore.Blazor.Client.Services.Shell;

public sealed class WorkspaceRouteClassifier(IWorkspaceRegistry registry)
{
    public WorkspaceKey Classify(string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);

        var path = NormalizePath(route);
        var workspace = registry.Workspaces
            .OrderByDescending(candidate => candidate.BaseRoute.Length)
            .FirstOrDefault(candidate => Matches(path, candidate.BaseRoute));

        return workspace?.Key ?? WorkspaceKey.Events;
    }

    private static bool Matches(string path, string baseRoute)
    {
        var normalizedBaseRoute = NormalizePath(baseRoute);
        return normalizedBaseRoute == "/"
            || path.Equals(normalizedBaseRoute, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith($"{normalizedBaseRoute}/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string route)
    {
        string path;
        if (Uri.TryCreate(route, UriKind.Absolute, out var absoluteUri)
            && (absoluteUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || absoluteUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            path = absoluteUri.AbsolutePath;
        }
        else
        {
            var suffixIndex = route.IndexOfAny(['?', '#']);
            path = suffixIndex < 0 ? route : route[..suffixIndex];
        }

        path = $"/{path.Trim().Trim('/')}";
        return path.Length == 1 ? "/" : path;
    }
}
