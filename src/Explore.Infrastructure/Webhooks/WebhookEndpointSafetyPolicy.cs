using System.Net;
using System.Net.Sockets;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Webhooks;

public sealed class WebhookEndpointSafetyPolicy(IOptionsMonitor<WebhookOptions> options)
{
    public SocketsHttpHandler CreateHttpMessageHandler(TimeSpan connectTimeout) =>
        new VettedAddressConnector(CreatePolicy).CreateHandler(connectTimeout);

    public async Task<WebhookEndpointSafetyResult> ValidateAsync(
        Uri endpointUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpointUrl);
        try
        {
            var policy = CreatePolicy();
            policy.RequireEndpoint(endpointUrl);
            string host = endpointUrl.IdnHost.Trim('[', ']');
            IPAddress[] addresses = IPAddress.TryParse(host, out IPAddress? literal)
                ? [literal] : await Dns.GetHostAddressesAsync(host, cancellationToken);
            policy.RequireAllowed(endpointUrl, addresses);
            return WebhookEndpointSafetyResult.Allowed();
        }
        catch (OutboundEndpointDeniedException exception)
        {
            return WebhookEndpointSafetyResult.Blocked(exception.FailureCategory);
        }
        catch (SocketException)
        {
            return WebhookEndpointSafetyResult.Blocked("dns_resolution_failed");
        }
    }

    private OutboundEndpointPolicy CreatePolicy()
    {
        var local = options.CurrentValue.Local;
        return new OutboundEndpointPolicy(local.BlockPrivateNetworks, local.AllowedPrivateCidrs);
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
