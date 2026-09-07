using System.Net;
using Event.Web.BffHosting.Abstractions;
using Event.Web.BffHosting.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Event.Web.BffHosting.Security;

public sealed class EventBffAdminHostAccessPolicy(
    IOptions<EventBffHostingOptions> options,
    IEventBffHostClassifier hostClassifier)
{
    private readonly AllowedIpRange[] _allowedRanges = options.Value.AdminHostAllowedIpRanges
        .Select(range => TryParseAllowedRange(range, out var parsed) ? parsed : null)
        .Where(range => range is not null)
        .Select(range => range!)
        .ToArray();

    public bool IsAllowed(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (!hostClassifier.IsAdminHost(httpContext) || _allowedRanges.Length == 0)
        {
            return true;
        }

        var remoteAddress = httpContext.Connection.RemoteIpAddress;
        return remoteAddress is not null && _allowedRanges.Any(range => range.Contains(remoteAddress));
    }

    public static bool TryParseAllowedRange(string? value, out AllowedIpRange? range)
    {
        range = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Trim().Split('/', 2, StringSplitOptions.TrimEntries);
        if (!IPAddress.TryParse(parts[0], out var address))
        {
            return false;
        }

        var maxPrefixLength = GetAddressBytes(address).Length * 8;
        var prefixLength = maxPrefixLength;
        if (parts.Length == 2 && (!int.TryParse(parts[1], out prefixLength) || prefixLength < 0 || prefixLength > maxPrefixLength))
        {
            return false;
        }

        range = new AllowedIpRange(address, prefixLength);
        return true;
    }

    private static byte[] GetAddressBytes(IPAddress address)
    {
        return address.IsIPv4MappedToIPv6
            ? address.MapToIPv4().GetAddressBytes()
            : address.GetAddressBytes();
    }

    public sealed class AllowedIpRange(IPAddress networkAddress, int prefixLength)
    {
        private readonly byte[] _networkBytes = GetAddressBytes(networkAddress);

        public bool Contains(IPAddress address)
        {
            var addressBytes = GetAddressBytes(address);
            if (addressBytes.Length != _networkBytes.Length)
            {
                return false;
            }

            var fullBytes = prefixLength / 8;
            var remainingBits = prefixLength % 8;

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
    }
}
