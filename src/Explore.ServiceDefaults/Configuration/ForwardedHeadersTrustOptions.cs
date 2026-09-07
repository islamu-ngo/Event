using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Explore.ServiceDefaults.Configuration;

public sealed class ForwardedHeadersTrustOptions
{
    public const string SectionName = "ForwardedHeadersTrust";
    private const int MaximumForwardLimit = 10;
    private const int MaximumTrustEntries = 32;

    public int ForwardLimit { get; set; } = 1;

    public bool TrustLoopbackProxy { get; set; }

    public List<string> KnownProxies { get; init; } = [];

    public List<string> KnownNetworks { get; init; } = [];

    public void ApplyTo(ForwardedHeadersOptions options, ForwardedHeaders forwardedHeaders)
    {
        Validate();
        var hasTrustedProxyBoundary = TrustLoopbackProxy || KnownProxies.Count > 0 || KnownNetworks.Count > 0;

        options.ForwardedHeaders = hasTrustedProxyBoundary ? forwardedHeaders : ForwardedHeaders.None;
        options.ForwardLimit = ForwardLimit;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        if (TrustLoopbackProxy)
        {
            options.KnownProxies.Add(IPAddress.Loopback);
            options.KnownProxies.Add(IPAddress.IPv6Loopback);
        }

        foreach (string knownProxy in KnownProxies)
        {
            options.KnownProxies.Add(IPAddress.Parse(knownProxy));
        }

        foreach (string knownNetwork in KnownNetworks)
        {
            options.KnownIPNetworks.Add(global::System.Net.IPNetwork.Parse(knownNetwork));
        }
    }

    public void Validate()
    {
        List<string> failures = [];
        if (ForwardLimit is < 1 or > MaximumForwardLimit)
        {
            failures.Add($"ForwardLimit must be between 1 and {MaximumForwardLimit}.");
        }

        if (KnownProxies.Count + KnownNetworks.Count > MaximumTrustEntries)
        {
            failures.Add($"At most {MaximumTrustEntries} trusted proxies and networks may be configured.");
        }

        foreach (string candidate in KnownProxies)
        {
            if (!IPAddress.TryParse(candidate, out IPAddress? address))
            {
                failures.Add($"Known proxy '{candidate}' is not an exact IP address.");
            }
            else if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            {
                failures.Add($"Known proxy '{candidate}' is a trust-all address.");
            }
        }

        foreach (string candidate in KnownNetworks)
        {
            if (!TryParseNetwork(candidate, out _))
            {
                failures.Add($"Known network '{candidate}' is not a bounded IPv4 or IPv6 CIDR.");
            }
        }

        if (failures.Count > 0)
        {
            throw new OptionsValidationException(SectionName, typeof(ForwardedHeadersTrustOptions), failures);
        }
    }

    private static bool TryParseNetwork(string candidate, out global::System.Net.IPNetwork network)
    {
        network = default;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        string[] segments = candidate.Split('/', StringSplitOptions.TrimEntries);
        if (segments.Length != 2 ||
            !IPAddress.TryParse(segments[0], out IPAddress? prefix) ||
            !int.TryParse(segments[1], out int prefixLength))
        {
            return false;
        }

        int maximumPrefixLength = prefix.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        if (prefixLength is < 1 || prefixLength > maximumPrefixLength)
        {
            return false;
        }

        network = global::System.Net.IPNetwork.Parse($"{prefix}/{prefixLength}");
        return true;
    }
}
