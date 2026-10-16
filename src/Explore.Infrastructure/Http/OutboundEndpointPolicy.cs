using System.Net;
using System.Net.Sockets;

namespace Explore.Infrastructure.Http;

/// <summary>Validates the complete connection-time answer set for one outbound policy profile.</summary>
public sealed class OutboundEndpointPolicy
{
    private static readonly IPAddress AwsMetadataIpv6 = IPAddress.Parse("fd00:ec2::254");
    private static readonly IPAddress AlibabaMetadataIpv4 = IPAddress.Parse("100.100.100.200");
    private readonly bool _blockPrivateNetworks;
    private readonly OutboundIpNetwork[] _allowedNetworks;

    public OutboundEndpointPolicy(bool blockPrivateNetworks = true, IEnumerable<string>? allowedPrivateCidrs = null)
    {
        _blockPrivateNetworks = blockPrivateNetworks;
        _allowedNetworks = (allowedPrivateCidrs ?? []).Select(value =>
            OutboundIpNetwork.TryParse(value, out var network) && network is not null
                ? network : throw new ArgumentException("Invalid outbound private CIDR.", nameof(allowedPrivateCidrs)))
            .ToArray();
    }

    public void RequireAllowed(Uri endpoint, IReadOnlyList<IPAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        RequireEndpoint(endpoint);
        if (addresses.Count == 0)
            throw new OutboundEndpointDeniedException("dns_resolution_empty");
        foreach (IPAddress address in addresses)
        {
            IPAddress normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
            byte[] bytes = normalized.GetAddressBytes();
            bool ipv4 = normalized.AddressFamily == AddressFamily.InterNetwork;
            if (ipv4 && bytes[0] == 169 && bytes[1] == 254
                || normalized.Equals(AwsMetadataIpv6) || normalized.Equals(AlibabaMetadataIpv4))
                throw new OutboundEndpointDeniedException("cloud_metadata_blocked");
            if (ipv4 && (bytes[0] == 0 || bytes[0] >= 224)
                || !ipv4 && (normalized.IsIPv6LinkLocal || normalized.IsIPv6Multicast
                    || normalized.Equals(IPAddress.IPv6Any) || normalized.ScopeId != 0))
                throw new OutboundEndpointDeniedException("private_network_blocked");
            bool privateAddress = IPAddress.IsLoopback(normalized)
                || ipv4 && (bytes[0] == 10 || bytes[0] == 127
                    || bytes[0] == 100 && bytes[1] is >= 64 and <= 127
                    || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
                    || bytes[0] == 192 && bytes[1] == 168)
                || !ipv4 && (normalized.IsIPv6SiteLocal || (bytes[0] & 0xFE) == 0xFC);
            if (_blockPrivateNetworks && privateAddress
                && !_allowedNetworks.Any(network => network.Contains(normalized)))
                throw new OutboundEndpointDeniedException("private_network_blocked");
        }
    }

    public void RequireEndpoint(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(endpoint.UserInfo) || string.IsNullOrEmpty(endpoint.Host))
            throw new OutboundEndpointDeniedException("invalid_url");
        string host = endpoint.IdnHost.TrimEnd('.');
        if (host.Equals("metadata", StringComparison.OrdinalIgnoreCase)
            || host.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase))
            throw new OutboundEndpointDeniedException("cloud_metadata_blocked");
    }
}

/// <summary>A permanent destination rejection before any socket or request bytes are handed off.</summary>
public sealed class OutboundEndpointDeniedException(string failureCategory)
    : Exception("Outbound endpoint denied by its connection policy.")
{
    public string FailureCategory { get; } = failureCategory;

    public static OutboundEndpointDeniedException? Find(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is OutboundEndpointDeniedException denied)
                return denied;
        return null;
    }
}
