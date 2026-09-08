using System.Net.Http.Headers;
using Event.Web.BffHosting.Abstractions;

namespace Event.Web.BffHosting.Proxy;

public sealed class EventBffBearerForwardingHandler(
    IHttpContextAccessor httpContextAccessor,
    IEventBffAccessTokenProvider tokenProvider)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpContext? httpContext = httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            string? token = await tokenProvider.ResolveAccessTokenAsync(httpContext, cancellationToken);
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
