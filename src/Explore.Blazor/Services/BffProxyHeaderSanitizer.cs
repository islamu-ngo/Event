using SharedBffProxyHeaderSanitizer = Event.Web.BffHosting.Security.BffProxyHeaderSanitizer;

namespace Explore.Blazor.Services;

public static class BffProxyHeaderSanitizer
{
    public static void RemoveBrowserControlledHeaders(HttpRequestMessage proxyRequest)
    {
        SharedBffProxyHeaderSanitizer.RemoveBrowserControlledHeaders(proxyRequest);
    }
}
