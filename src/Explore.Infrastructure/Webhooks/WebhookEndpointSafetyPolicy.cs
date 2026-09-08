using System.Net;
using System.Net.Sockets;
using Explore.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Webhooks;

public sealed class WebhookEndpointSafetyPolicy
{
    private static readonly IPAddress AwsMetadataIpv4 = IPAddress.Parse("169.254.169.254");
    private static readonly IPAddress AwsMetadataIpv6 = IPAddress.Parse("fd00:ec2::254");
    private readonly IOptionsMonitor<WebhookOptions> _options;

    public WebhookEndpointSafetyPolicy(IOptionsMonitor<WebhookOptions> options)
    {
        _options = options;
    }

    public async Task<WebhookEndpointSafetyResult> ValidateAsync(
        Uri endpointUrl,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpointUrl);

        if (!endpointUrl.IsAbsoluteUri)
        {
            return WebhookEndpointSafetyResult.Blocked("invalid_url", "Webhook endpoint URL must be absolute.");
        }

        if (endpointUrl.Scheme is not ("http" or "https"))
        {
            return WebhookEndpointSafetyResult.Blocked("unsupported_scheme", "Webhook endpoint URL must use http or https.");
        }

        if (!string.IsNullOrWhiteSpace(endpointUrl.UserInfo))
        {
            return WebhookEndpointSafetyResult.Blocked("userinfo_not_allowed", "Webhook endpoint URL must not include user info.");
        }

        var host = endpointUrl.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            return WebhookEndpointSafetyResult.Blocked("missing_host", "Webhook endpoint URL must include a host.");
        }

        var localOptions = _options.CurrentValue.Local;
        if (!localOptions.BlockPrivateNetworks)
        {
            return WebhookEndpointSafetyResult.Allowed();
        }

        if (IsBlockedHostname(host))
        {
            return WebhookEndpointSafetyResult.Blocked("private_network_blocked", "Webhook endpoint host resolves to a blocked internal name.");
        }

        var allowedNetworks = ParseAllowedNetworks(localOptions.AllowedPrivateCidrs);
        if (IPAddress.TryParse(host, out var literalAddress))
        {
            return ValidateAddress(literalAddress, allowedNetworks);
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        }
        catch (SocketException ex)
        {
            return WebhookEndpointSafetyResult.Blocked("dns_resolution_failed", ex.SocketErrorCode.ToString());
        }

        if (addresses.Length == 0)
        {
            return WebhookEndpointSafetyResult.Blocked("dns_resolution_empty", "Webhook endpoint host did not resolve to an address.");
        }

        foreach (var address in addresses)
        {
            var result = ValidateAddress(address, allowedNetworks);
            if (!result.IsAllowed)
            {
                return result;
            }
        }

        return WebhookEndpointSafetyResult.Allowed();
    }

    private static WebhookEndpointSafetyResult ValidateAddress(
        IPAddress address,
        List<WebhookIpNetwork> allowedNetworks)
    {
        var normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        if (IsCloudMetadataAddress(normalized))
        {
            return WebhookEndpointSafetyResult.Blocked("cloud_metadata_blocked", "Webhook endpoint address targets cloud metadata.");
        }

        if (!IsUnsafeAddress(normalized))
        {
            return WebhookEndpointSafetyResult.Allowed();
        }

        if (allowedNetworks.Any(network => network.Contains(normalized)))
        {
            return WebhookEndpointSafetyResult.Allowed();
        }

        return WebhookEndpointSafetyResult.Blocked("private_network_blocked", "Webhook endpoint address targets a private or internal network.");
    }

    private static bool IsBlockedHostname(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        || host.Equals("metadata", StringComparison.OrdinalIgnoreCase)
        || host.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase);

    private static bool IsCloudMetadataAddress(IPAddress address) =>
        address.Equals(AwsMetadataIpv4) || address.Equals(AwsMetadataIpv6);

    private static bool IsUnsafeAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 0
                || bytes[0] == 10
                || bytes[0] == 127
                || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                || (bytes[0] == 169 && bytes[1] == 254)
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || bytes[0] >= 224;
        }

        return address.IsIPv6LinkLocal
            || address.IsIPv6Multicast
            || address.IsIPv6SiteLocal
            || address.Equals(IPAddress.IPv6None)
            || address.Equals(IPAddress.IPv6Any)
            || IsUniqueLocalIpv6(address);
    }

    private static bool IsUniqueLocalIpv6(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return (bytes[0] & 0xFE) == 0xFC;
    }

    private static List<WebhookIpNetwork> ParseAllowedNetworks(IEnumerable<string> cidrs)
    {
        List<WebhookIpNetwork> networks = [];
        foreach (var cidr in cidrs)
        {
            if (WebhookIpNetwork.TryParse(cidr, out var network) && network is not null)
            {
                networks.Add(network);
            }
        }

        return networks;
    }
}

public sealed record WebhookEndpointSafetyResult(
    bool IsAllowed,
    string? FailureCategory,
    string? SafeDetail)
{
    public static WebhookEndpointSafetyResult Allowed() => new(true, null, null);

    public static WebhookEndpointSafetyResult Blocked(string failureCategory, string? safeDetail = null) =>
        new(false, failureCategory, safeDetail);
}
