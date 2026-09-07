namespace Explore.Application.Features.ConfigurationManifest.Managed;

using System.Net;
using System.Net.Sockets;

public static class ConfigurationDirectTransferPolicy
{
    public const string DestinationPath =
        "/api/configuration-transfers/v1alpha2";

    public static Uri ValidateDestinationOrigin(
        Uri origin,
        IReadOnlyCollection<IPAddress> resolvedAddresses)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(resolvedAddresses);
        if (!origin.IsAbsoluteUri
            || !string.Equals(origin.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || !string.IsNullOrEmpty(origin.UserInfo)
            || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment)
            || origin.AbsolutePath != "/"
            || origin.Port != 443
            || resolvedAddresses.Count == 0
            || resolvedAddresses.Any(address => !IsPublic(address)))
        {
            throw new ArgumentException(
                "Direct-transfer destination must be a public HTTPS origin.",
                nameof(origin));
        }

        return new Uri(origin, DestinationPath);
    }

    private static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None)
            || address.Equals(IPAddress.IPv6None)
            || address.IsIPv6LinkLocal
            || address.IsIPv6Multicast
            || address.IsIPv6SiteLocal)
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] bytes = address.GetAddressBytes();
            return bytes[0] != 0
                && bytes[0] != 10
                && bytes[0] != 127
                && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                && !(bytes[0] == 169 && bytes[1] == 254)
                && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                && !(bytes[0] == 192 && bytes[1] == 168)
                && !(bytes[0] >= 224);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            byte first = address.GetAddressBytes()[0];
            return (first & 0xfe) != 0xfc;
        }
        return false;
    }
}
