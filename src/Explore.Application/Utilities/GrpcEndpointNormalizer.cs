using System.Net;

namespace Explore.Application.Utilities;

public static class GrpcEndpointNormalizer
{
    public static string Normalize(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return string.Empty;
        }

        var trimmed = endpoint.Trim().TrimEnd('/');
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var scheme = IsLocalEndpoint(trimmed) ? Uri.UriSchemeHttp : Uri.UriSchemeHttps;
        return $"{scheme}://{trimmed}";
    }

    public static bool IsValid(string? endpoint)
    {
        var normalized = Normalize(endpoint);
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
               && !string.IsNullOrWhiteSpace(uri.Host)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
               && string.IsNullOrEmpty(uri.UserInfo)
               && string.IsNullOrEmpty(uri.Query)
               && string.IsNullOrEmpty(uri.Fragment)
               && uri.AbsolutePath == "/";
    }

    public static bool IsLocalEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return false;
        }

        var candidate = endpoint.Contains("://", StringComparison.Ordinal)
            ? endpoint
            : $"{Uri.UriSchemeHttp}://{endpoint}";

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(uri.Host, out var ipAddress) && IPAddress.IsLoopback(ipAddress);
    }
}
