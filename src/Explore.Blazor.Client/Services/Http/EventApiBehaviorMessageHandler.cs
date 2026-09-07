using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Services.Http;

public sealed class EventApiBehaviorMessageHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        EventApiTransportBehavior.PrepareRequest(request);
        var response = await base.SendAsync(request, cancellationToken);
        EventApiTransportBehavior.ProcessResponse(request, response);
        return response;
    }
}
