namespace Explore.Blazor.Client.Services.Http;

public sealed class AdmissionScannerHttpClient(IHttpClientFactory httpClientFactory)
{
    internal const string ClientName = "AdmissionScannerClient";

    public Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        httpClientFactory.CreateClient(ClientName).SendAsync(request, cancellationToken);
}
