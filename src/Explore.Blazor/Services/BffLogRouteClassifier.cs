namespace Explore.Blazor.Services;

internal static class BffLogRouteClassifier
{
    public static string Classify(Uri? requestUri)
    {
        var path = requestUri?.AbsolutePath;
        if (string.IsNullOrEmpty(path))
        {
            return "unknown";
        }

        if (path.Equals("/bff/auth/refresh-session/internal", StringComparison.OrdinalIgnoreCase))
        {
            return "internal_refresh";
        }

        if (path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase))
        {
            return "circuit_transport";
        }

        if (path.StartsWith("/bff/", StringComparison.OrdinalIgnoreCase))
        {
            return "bff_endpoint";
        }

        return path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ? "api_endpoint" : "other";
    }
}
