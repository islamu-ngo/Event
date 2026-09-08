using System.Net;
using System.Net.Sockets;

namespace Explore.Infrastructure.Webhooks;

internal sealed class WebhookIpNetwork
{
    private readonly byte[] _networkBytes;

    private WebhookIpNetwork(IPAddress networkAddress, int prefixLength)
    {
        NetworkAddress = Normalize(networkAddress);
        PrefixLength = prefixLength;
        _networkBytes = NetworkAddress.GetAddressBytes();
    }

    public IPAddress NetworkAddress { get; }
    public int PrefixLength { get; }

    public static bool TryParse(string? value, out WebhookIpNetwork? network)
    {
        network = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Trim().Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2 || !IPAddress.TryParse(parts[0], out var address))
        {
            return false;
        }

        address = Normalize(address);
        var maxPrefix = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        var prefixLength = maxPrefix;

        if (parts.Length == 2
            && (!int.TryParse(parts[1], out prefixLength) || prefixLength < 0 || prefixLength > maxPrefix))
        {
            return false;
        }

        network = new WebhookIpNetwork(address, prefixLength);
        return true;
    }

    public bool Contains(IPAddress address)
    {
        address = Normalize(address);
        if (address.AddressFamily != NetworkAddress.AddressFamily)
        {
            return false;
        }

        var addressBytes = address.GetAddressBytes();
        var fullBytes = PrefixLength / 8;
        var remainingBits = PrefixLength % 8;

        for (var i = 0; i < fullBytes; i++)
        {
            if (addressBytes[i] != _networkBytes[i])
            {
                return false;
            }
        }

        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (addressBytes[fullBytes] & mask) == (_networkBytes[fullBytes] & mask);
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
