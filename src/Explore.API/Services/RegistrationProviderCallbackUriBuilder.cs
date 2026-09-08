using System.Net;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Services.Registration;
using Microsoft.AspNetCore.Routing;

namespace Explore.API.Services;

public sealed class RegistrationProviderCallbackUriBuilder(
    LinkGenerator linkGenerator,
    IHttpContextAccessor httpContextAccessor,
    IConfiguration configuration) : IRegistrationProviderCallbackUriBuilder
{
    public Uri Build(string providerCode, Guid bindingId)
    {
        HttpContext context = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("Registration provider callback URL requires an active HTTP request.");
        object routeValues = new { provider = providerCode.ToLowerInvariant(), bindingId };
        string? configuredBase = configuration["PublicApi:BaseUrl"];
        string uri = configuredBase is { Length: > 0 }
            ? new Uri(EnsureTrailingSlash(new Uri(configuredBase, UriKind.Absolute)),
                (linkGenerator.GetPathByName(context, RouteNames.RegistrationProviderCallback, routeValues)
                 ?? throw new InvalidOperationException("Registration provider callback route could not be generated.")).TrimStart('/')).ToString()
            : linkGenerator.GetUriByName(context, RouteNames.RegistrationProviderCallback, routeValues)
            ?? throw new InvalidOperationException("Registration provider callback route could not be generated.");
        Uri callback = new(uri, UriKind.Absolute);
        if (!string.Equals(callback.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || IsBlockedHost(callback.Host))
        {
            throw new InvalidOperationException("Registration provider callback URL must use a public HTTPS origin.");
        }

        return callback;
    }

    private static Uri EnsureTrailingSlash(Uri value) =>
        value.AbsoluteUri.EndsWith('/') ? value : new Uri(value.AbsoluteUri + '/', UriKind.Absolute);

    private static bool IsBlockedHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(host, out IPAddress? address)) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
            address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
        {
            return true;
        }

        byte[] bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return (bytes[0] & 0xFE) == 0xFC;
        }

        return bytes[0] is 0 or 10 or 127 ||
               bytes[0] == 169 && bytes[1] == 254 ||
               bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
               bytes[0] == 192 && bytes[1] == 168 ||
               bytes[0] >= 224;
    }
}
