using System.Net;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

public sealed class CerbosAdminEndpointValidator(IOptions<CerbosPolicyPackageOptions> options)
{
    private readonly CerbosPolicyPackageOptions _options = options.Value;

    public bool TryNormalize(
        string? endpoint,
        bool isByo,
        out Uri normalizedEndpoint,
        out string warning)
    {
        normalizedEndpoint = null!;
        warning = string.Empty;

        var normalizedInput = NormalizeInput(endpoint);

        if (!Uri.TryCreate(normalizedInput, UriKind.Absolute, out var uri)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !uri.IsAbsoluteUri)
        {
            warning = "Cerbos Admin API endpoint must be an absolute HTTP(S) URL.";
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            warning = "Cerbos Admin API endpoint must use the http or https scheme.";
            return false;
        }

        if (isByo && uri.Scheme != Uri.UriSchemeHttps && !_options.AllowInsecureByoAdminEndpoints)
        {
            warning = "BYO Cerbos Admin API endpoint must use https.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            warning = "Cerbos Admin API endpoint must not include credentials, query, or fragment components.";
            return false;
        }

        if (isByo && !_options.AllowPrivateByoAdminEndpoints && IsPrivateOrLocalEndpoint(uri))
        {
            warning = "BYO Cerbos Admin API endpoint must not target local or private network addresses.";
            return false;
        }

        normalizedEndpoint = uri;
        return true;
    }

    private static string NormalizeInput(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return string.Empty;

        var trimmed = endpoint.Trim().TrimEnd('/');
        return trimmed.Contains("://", StringComparison.Ordinal)
            ? trimmed
            : $"{Uri.UriSchemeHttps}://{trimmed}";
    }

    public static string ToSafeEndpoint(Uri endpoint)
    {
        var builder = new UriBuilder(endpoint)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static bool IsPrivateOrLocalEndpoint(Uri uri)
    {
        if (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        return IPAddress.TryParse(uri.Host, out var address) && IsPrivateOrLocalAddress(address);
    }

    private static bool IsPrivateOrLocalAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None))
            return true;

        if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
            return true;

        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            return bytes[0] == 10
                || bytes[0] == 127
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254)
                || bytes[0] >= 224;
        }

        return address.IsIPv6UniqueLocal;
    }
}
