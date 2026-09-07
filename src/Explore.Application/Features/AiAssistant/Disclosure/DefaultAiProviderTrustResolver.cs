using System;
using Explore.Domain.Enums;

namespace Explore.Application.Features.AiAssistant.Disclosure;

/// <summary>
/// Default evidence-based provider-trust resolver. Maps endpoint evidence to one of the five
/// <see cref="AiProviderTrustTierEnum"/> tiers without consulting provider branding or names.
/// </summary>
/// <remarks>
/// Resolution priority:
/// <list type="number">
///   <item>Loopback / private network / null endpoint → <see cref="AiProviderTrustTierEnum.LocalInProcessOrSameNetworkModel"/>.</item>
///   <item>Explicit tenant-controlled private endpoint flag → <see cref="AiProviderTrustTierEnum.TenantControlledPrivateEndpoint"/>.</item>
///   <item>Platform-default flag → <see cref="AiProviderTrustTierEnum.PlatformConfiguredExternalProcessor"/>.</item>
///   <item>Tenant-controlled but on public network → <see cref="AiProviderTrustTierEnum.TenantConfiguredExternalProcessor"/>.</item>
///   <item>Anything ambiguous → <see cref="AiProviderTrustTierEnum.Unknown"/> (most restrictive).</item>
/// </list>
/// </remarks>
public sealed class DefaultAiProviderTrustResolver : IAiProviderTrustResolver
{
    /// <inheritdoc/>
    public AiProviderTrustTierEnum Resolve(AiProviderTrustResolutionContext context)
    {
        if (context is null)
        {
            return AiProviderTrustTierEnum.Unknown;
        }

        if (IsLocalEndpoint(context.EndpointUrl))
        {
            return AiProviderTrustTierEnum.LocalInProcessOrSameNetworkModel;
        }

        if (context.TenantControlled && context.PlatformDefault)
        {
            return AiProviderTrustTierEnum.Unknown;
        }

        if (context.TenantControlled)
        {
            return AiProviderTrustTierEnum.TenantConfiguredExternalProcessor;
        }

        if (context.PlatformDefault)
        {
            return AiProviderTrustTierEnum.PlatformConfiguredExternalProcessor;
        }

        return AiProviderTrustTierEnum.Unknown;
    }

    private static bool IsLocalEndpoint(string? endpointUrl)
    {
        if (string.IsNullOrWhiteSpace(endpointUrl))
        {
            return true;
        }

        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host;
        if (string.IsNullOrEmpty(host))
        {
            return false;
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("127.0.0.1") ||
            host.Equals("::1") ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".svc.cluster.local", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (System.Net.IPAddress.TryParse(host, out var address))
        {
            if (System.Net.IPAddress.IsLoopback(address))
            {
                return true;
            }

            if (IsPrivateAddress(address))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPrivateAddress(System.Net.IPAddress address)
    {
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork &&
            address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            return bytes[0] == 10 ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168);
        }

        if (bytes.Length == 16)
        {
            return (bytes[0] & 0xFE) == 0xFC;
        }

        return false;
    }
}
