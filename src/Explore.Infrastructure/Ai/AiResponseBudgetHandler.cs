namespace Explore.Infrastructure.Ai;

/// <summary>
/// Runs below HttpClient completion buffering and SDK deserialization. Only bounded,
/// validated JSON is returned upstream; HTTP error bodies are discarded unread.
/// </summary>
public sealed class AiResponseBudgetHandler(AiResponseLimits limits) : DelegatingHandler
{
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new Explore.Infrastructure.Http.ProviderResponseRejectedException("provider_transport_unsupported");

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        try
        {
            var original = response.Content;
            byte[] bytes = response.IsSuccessStatusCode
                ? await AiResponseReader.ReadAsync(original, limits, request.Method == HttpMethod.Get, cancellationToken)
                : [];
            var bounded = new ByteArrayContent(bytes);
            // Content-Length is recomputed from trusted bytes, not the remote declaration.
            if (response.IsSuccessStatusCode)
                bounded.Headers.ContentType = original.Headers.ContentType;
            original.Dispose();
            response.Content = bounded;
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }
}
