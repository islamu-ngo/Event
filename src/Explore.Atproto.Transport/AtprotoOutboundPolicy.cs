using System.Net;
using System.Net.Sockets;

namespace Explore.Atproto.Transport;

public sealed class AtprotoOutboundPolicy(bool allowsDevelopmentLoopback)
{
    public bool AllowsDevelopmentLoopback { get; } = allowsDevelopmentLoopback;

    public void ValidateUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.Host.EndsWith('.')
            || uri.Host.Any(character => character > 0x7f)
            || !string.Equals(uri.Host, uri.IdnHost, StringComparison.Ordinal)
            || Uri.CheckHostName(uri.Host) is UriHostNameType.Unknown)
        {
            throw new AtprotoOAuthSecurityException("invalid_endpoint");
        }

        if (uri.Scheme == Uri.UriSchemeHttp && IsExactDevelopmentLoopbackHost(uri.Host))
        {
            return;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new AtprotoOAuthSecurityException("https_required");
        }

        if (IPAddress.TryParse(uri.Host, out var address) && !IsAllowedAddress(uri.Host, address))
        {
            throw new AtprotoOAuthSecurityException("unsafe_endpoint");
        }
    }

    public void ValidateResolvedAddresses(string host, IReadOnlyCollection<IPAddress> addresses)
    {
        if (addresses.Count == 0 || addresses.Any(address => !IsAllowedAddress(host, address)))
        {
            throw new AtprotoOAuthSecurityException("unsafe_dns_answer");
        }
    }

    private bool IsAllowedAddress(string host, IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return IsExactDevelopmentLoopbackHost(host);
        }

        return IsPublicAddress(address);
    }

    private bool IsExactDevelopmentLoopbackHost(string host) => AllowsDevelopmentLoopback
        && (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));

    private static bool IsPublicAddress(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] is not 0 and not 10 and not 127
                && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                && !(bytes[0] == 169 && bytes[1] == 254)
                && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                && !(bytes[0] == 192 && bytes[1] == 0)
                && !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2)
                && !(bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99)
                && !(bytes[0] == 192 && bytes[1] == 168)
                && !(bytes[0] == 198 && bytes[1] is 18 or 19)
                && !(bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
                && !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
                && bytes[0] is < 224;
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return false;
        }

        return IsInPrefix(bytes, [0x20], 3)
            && !IsInPrefix(bytes, [0x20, 0x01, 0x00, 0x00], 32)
            && !IsInPrefix(bytes, [0x20, 0x01, 0x00, 0x02], 48)
            && !IsInPrefix(bytes, [0x20, 0x01, 0x00, 0x10], 28)
            && !IsInPrefix(bytes, [0x20, 0x01, 0x00, 0x20], 28)
            && !IsInPrefix(bytes, [0x20, 0x01, 0x0d, 0xb8], 32)
            && !IsInPrefix(bytes, [0x3f, 0xff], 20)
            && !IsInPrefix(bytes, [0x5f, 0x00], 16);
    }

    private static bool IsInPrefix(ReadOnlySpan<byte> address, ReadOnlySpan<byte> prefix, int prefixLength)
    {
        var wholeBytes = prefixLength / 8;
        if (!address[..wholeBytes].SequenceEqual(prefix[..wholeBytes]))
        {
            return false;
        }

        var remainingBits = prefixLength % 8;
        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte)(0xff << (8 - remainingBits));
        return (address[wholeBytes] & mask) == (prefix[wholeBytes] & mask);
    }
}
