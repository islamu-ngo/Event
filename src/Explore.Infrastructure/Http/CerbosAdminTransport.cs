using System.Net;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Http;

/// <summary>Preserves the server-selected Cerbos target classification through the actual socket.</summary>
public static class CerbosAdminTransport
{
    public const string ByoClient = "CerbosByoAdminClient";
    public const string InstanceClient = "CerbosInstanceAdminClient";

    public static void AddClients(
        IServiceCollection services,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
    {
        services.AddSingleton<CerbosAdminEndpointValidator>();
        services.AddHttpClient(ByoClient, client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => CreateHandler(true, [], resolve));
        services.AddHttpClient(InstanceClient, client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var validator = sp.GetRequiredService<CerbosAdminEndpointValidator>();
                var origins = sp.GetRequiredService<IOptions<CerbosAdminApiSettings>>().Value.Endpoints
                    .Select(value => validator.TryNormalize(value, false, out var endpoint, out _)
                        ? endpoint : throw new InvalidOperationException("cerbos_admin_origin_invalid"))
                    .ToArray();
                return CreateHandler(false, origins, resolve);
            });
    }

    public static HttpMessageHandler CreateHandler(
        bool isByo,
        IReadOnlyList<Uri> instanceOrigins,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
    {
        var policy = new OutboundEndpointPolicy(blockPrivateNetworks: isByo);
        return new OriginHandler(isByo, instanceOrigins.ToArray(), policy)
        {
            InnerHandler = new VettedAddressConnector(() => policy, resolve)
                .CreateHandler(TimeSpan.FromSeconds(10))
        };
    }

    private sealed class OriginHandler(
        bool isByo, IReadOnlyList<Uri> instanceOrigins, OutboundEndpointPolicy policy) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new OutboundEndpointDeniedException("invalid_url");
            policy.RequireEndpoint(uri);
            if (isByo && uri.Scheme != Uri.UriSchemeHttps)
                throw new OutboundEndpointDeniedException("https_required");
            if (!isByo && !instanceOrigins.Any(origin =>
                    origin.Scheme == uri.Scheme && origin.IdnHost == uri.IdnHost && origin.Port == uri.Port))
                throw new OutboundEndpointDeniedException("origin_mismatch");
            return base.SendAsync(request, cancellationToken);
        }
    }
}
