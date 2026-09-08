using System.Net;
using Explore.Blazor.Services.Auth;
using Microsoft.Extensions.Options;

namespace Explore.Blazor.IntegrationTests.Services;

public sealed class BffAuthDiagnosticsServiceTests
{
    [Test]
    public async Task BuildDebugSnapshotAsync_WithSuccessfulDiscovery_ReportsDiscoveryStatusAndDocument()
    {
        using var handler = new DiagnosticsHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"issuer\":\"https://issuer.example\"}")
        });
        var service = CreateService(handler);

        var snapshot = await service.BuildDebugSnapshotAsync(CancellationToken.None);

        await Assert.That(snapshot["authority"]).IsEqualTo("https://issuer.example");
        await Assert.That(snapshot["metadataAddress"]).IsEqualTo("https://metadata.example/.well-known/openid-configuration");
        await Assert.That(snapshot["hasClientId"]).IsEqualTo(true);
        await Assert.That(snapshot["hasClientSecret"]).IsEqualTo(false);
        await Assert.That(snapshot["discoveryStatus"]).IsEqualTo((int)HttpStatusCode.OK);
        await Assert.That(snapshot["discoverySuccess"]).IsEqualTo(true);
        await Assert.That(snapshot).ContainsKey("discoveryDocument");
    }

    [Test]
    public async Task BuildDebugSnapshotAsync_WithDiscoveryException_ReportsSafeErrorMessage()
    {
        using var handler = new DiagnosticsHandler(_ => throw new HttpRequestException("metadata unavailable"));
        var service = CreateService(handler);

        var snapshot = await service.BuildDebugSnapshotAsync(CancellationToken.None);

        await Assert.That(snapshot["discoveryError"]).IsEqualTo("metadata unavailable");
    }

    private static BffAuthDiagnosticsService CreateService(DiagnosticsHandler handler)
    {
        var options = Options.Create(new BffAuthDiagnosticsOptions
        {
            Authority = "https://issuer.example",
            MetadataAddress = "https://metadata.example/.well-known/openid-configuration",
            ClientId = "islamu-event-blazor"
        });

        return new BffAuthDiagnosticsService(options, new DiagnosticsHttpClientFactory(handler));
    }

    private sealed class DiagnosticsHttpClientFactory(DiagnosticsHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class DiagnosticsHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responseFactory(request));
    }
}
