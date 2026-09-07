using System.Net;

namespace Explore.Application.Services.Federation;

public static class AtprotoExternalUriPolicy
{
    public const int MaximumLength = 2048;

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumLength
            || !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.HostNameType != UriHostNameType.Dns
            || IPAddress.TryParse(uri.Host, out _)
            || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string normalized = uri.AbsoluteUri;
        return normalized.Length <= MaximumLength ? normalized : null;
    }
}
