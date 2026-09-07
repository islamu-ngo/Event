using System.Net;
using System.Net.Sockets;

namespace Explore.Infrastructure.WebPush;

public sealed class WebPushEndpointSafetyPolicy
{
    private static readonly IPAddress AwsMetadataIpv4 = IPAddress.Parse("169.254.169.254");
    private static readonly IPAddress AwsMetadataIpv6 = IPAddress.Parse("fd00:ec2::254");
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> resolveAddresses;

    public WebPushEndpointSafetyPolicy()
        : this(Dns.GetHostAddressesAsync)
    {
    }

    internal WebPushEndpointSafetyPolicy(Func<string, CancellationToken, Task<IPAddress[]>> resolveAddresses)
    {
        this.resolveAddresses = resolveAddresses;
    }

    public async Task<WebPushEndpointSafetyResult> ValidateAsync(
        string endpoint,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || IsBlockedHostname(uri.Host))
        {
            return WebPushEndpointSafetyResult.Blocked();
        }

        if (IPAddress.TryParse(uri.DnsSafeHost, out var literalAddress))
        {
            return IsUnsafeAddress(literalAddress)
                ? WebPushEndpointSafetyResult.Blocked()
                : WebPushEndpointSafetyResult.Allowed([literalAddress]);
        }

        return await ResolveHostAsync(uri.DnsSafeHost, cancellationToken);
    }

    internal async Task<WebPushEndpointSafetyResult> ResolveHostAsync(
        string host,
        CancellationToken cancellationToken)
    {
        if (IsBlockedHostname(host))
        {
            return WebPushEndpointSafetyResult.Blocked();
        }

        try
        {
            var addresses = await resolveAddresses(host, cancellationToken);
            if (addresses.Length == 0)
            {
                return WebPushEndpointSafetyResult.Retryable();
            }

            return addresses.Any(IsUnsafeAddress)
                ? WebPushEndpointSafetyResult.Blocked()
                : WebPushEndpointSafetyResult.Allowed(addresses);
        }
        catch (SocketException)
        {
            return WebPushEndpointSafetyResult.Retryable();
        }
    }

    private static bool IsBlockedHostname(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        || host.Equals("metadata", StringComparison.OrdinalIgnoreCase)
        || host.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnsafeAddress(IPAddress address)
    {
        var normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (IPAddress.IsLoopback(normalized)
            || normalized.Equals(AwsMetadataIpv4)
            || normalized.Equals(AwsMetadataIpv6))
        {
            return true;
        }

        if (normalized.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = normalized.GetAddressBytes();
            return bytes[0] == 0
                || bytes[0] == 10
                || bytes[0] == 127
                || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                || (bytes[0] == 169 && bytes[1] == 254)
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || bytes[0] >= 224;
        }

        var ipv6 = normalized.GetAddressBytes();
        return normalized.IsIPv6LinkLocal
            || normalized.IsIPv6Multicast
            || normalized.IsIPv6SiteLocal
            || normalized.Equals(IPAddress.IPv6None)
            || normalized.Equals(IPAddress.IPv6Any)
            || (ipv6[0] & 0xFE) == 0xFC;
    }
}

public sealed record WebPushEndpointSafetyResult(
    bool IsAllowed,
    bool IsRetryable,
    IReadOnlyList<IPAddress> Addresses)
{
    public static WebPushEndpointSafetyResult Allowed(IReadOnlyList<IPAddress> addresses) =>
        new(true, false, addresses);

    public static WebPushEndpointSafetyResult Blocked() => new(false, false, []);
    public static WebPushEndpointSafetyResult Retryable() => new(false, true, []);
}
