using Explore.Infrastructure.Http;
using System.Net;

namespace Explore.Infrastructure.Ai;

/// <summary>Separates public tenant traffic from the configured instance origin.</summary>
public static class AiTransportAuthority
{
    public const string InstanceClientSuffix = ".Instance";

    public static HttpMessageHandler CreateHandler(
        AiProviderSettings settings,
        bool instanceScope,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
    {
        var connector = new VettedAddressConnector(() =>
            new OutboundEndpointPolicy(blockPrivateNetworks:
                !instanceScope || !settings.AllowLocalProviderEndpoints), resolve);
        var handler = connector.CreateHandler(TimeSpan.FromSeconds(10));
        handler.AutomaticDecompression = DecompressionMethods.None;
        return instanceScope
            ? new InstanceOriginHandler(ResolveInstanceEndpoint(settings)) { InnerHandler = handler }
            : handler;
    }

    public static Uri ResolveInstanceEndpoint(AiProviderSettings settings) =>
        new(string.IsNullOrWhiteSpace(settings.EndpointUrl)
            ? settings.Provider switch
            {
                AiProviderSettings.ProviderOpenAi => "https://api.openai.com",
                AiProviderSettings.ProviderAnthropic => "https://api.anthropic.com",
                _ => throw new OutboundEndpointDeniedException("invalid_url")
            }
            : settings.EndpointUrl.Trim(), UriKind.Absolute);

    public static bool HasSameOrigin(Uri left, Uri right) =>
        left.Scheme.Equals(right.Scheme, StringComparison.OrdinalIgnoreCase)
        && left.IdnHost.Equals(right.IdnHost, StringComparison.OrdinalIgnoreCase)
        && left.Port == right.Port;

    private sealed class InstanceOriginHandler(Uri endpoint) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is null || !HasSameOrigin(endpoint, request.RequestUri))
                throw new OutboundEndpointDeniedException("origin_mismatch");
            return base.SendAsync(request, cancellationToken);
        }
    }
}
