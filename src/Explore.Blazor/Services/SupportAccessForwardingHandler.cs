using Event.Web.BffHosting.Security;

namespace Explore.Blazor.Services;

public sealed class SupportAccessForwardingHandler(
    IBffSupportAccessSessionStore sessionStore) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RemoveSupportAccessHeaders(request);

        if (!IsApiRequest(request))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var resolution = await sessionStore.ResolveCurrentAsync(cancellationToken);
        if (resolution.Success && resolution.Session is not null)
        {
            request.Headers.TryAddWithoutValidation(
                EventBffHeaderNames.SupportAccessSessionId,
                resolution.Session.SessionId.ToString("D"));
        }

        return await base.SendAsync(request, cancellationToken);
    }

    internal static void RemoveSupportAccessHeaders(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var headerNames = request.Headers
            .Select(header => header.Key)
            .Where(EventBffHeaderNames.IsSupportAccessHeader)
            .ToArray();

        foreach (var headerName in headerNames)
        {
            _ = request.Headers.Remove(headerName);
        }
    }

    private static bool IsApiRequest(HttpRequestMessage request)
    {
        var path = request.RequestUri?.IsAbsoluteUri == true
            ? request.RequestUri.AbsolutePath
            : request.RequestUri?.OriginalString;

        return path?.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) == true
            || path?.StartsWith("api/", StringComparison.OrdinalIgnoreCase) == true;
    }
}
