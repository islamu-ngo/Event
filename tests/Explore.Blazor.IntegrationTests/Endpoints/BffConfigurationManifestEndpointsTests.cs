using System.Net.Http.Headers;
using System.Text;
using Explore.Blazor.Client.Clients;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class BffConfigurationManifestEndpointsTests
{
    private const string BffRoute = "/bff/control-plane/configuration-manifest/export";
    private const string ApiOverviewRoute = "/api/admin/control-plane/overview";
    private const string ApiExportRoute = "/api/control-plane/configuration-manifest/export";
    private const string MediaType = "application/vnd.islamu.configuration-manifest.v1alpha2+json";
    private const int MaximumBytes = 4 * 1024 * 1024;

    [Test]
    [Arguments("Overrides", "export-configuration-overrides", "configuration-manifest-overrides.json")]
    [Arguments("Portable", "export-configuration-portable", "configuration-manifest-portable.json")]
    public async Task Download_UsesFixedApiRouteAndReturnsValidatedNoStoreFile(
        string view,
        string relation,
        string fileName)
    {
        var paths = new List<string>();
        var payload = Encoding.UTF8.GetBytes("""{"kind":"ConfigurationManifest"}""");

        await using var factory = CreateFactory((request, _) =>
        {
            paths.Add(request.RequestUri?.PathAndQuery ?? string.Empty);
            return Task.FromResult(
                request.RequestUri?.AbsolutePath == ApiOverviewRoute
                    ? OverviewResponse(relation)
                    : FileResponse(payload, MediaType, fileName));
        });
        using var client = CreateClient(factory);
        using var request = AuthenticatedRequest(
            $"{BffRoute}?view={view}&apiUrl=https://attacker.example/export&access_token=browser-secret");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsByteArrayAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo(MediaType);
        await Assert.That(response.Content.Headers.ContentDisposition?.FileNameStar).IsEqualTo(fileName);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(body).IsEquivalentTo(payload);
        await Assert.That(Encoding.UTF8.GetString(body)).DoesNotContain("browser-secret");
        await Assert.That(paths).IsEquivalentTo(
        [
            ApiOverviewRoute,
            $"{ApiExportRoute}?view={view}"
        ]);
    }

    [Test]
    public async Task Download_WithoutMatchingHalCapabilityFailsClosedBeforeExport()
    {
        var exportCalls = 0;
        await using var factory = CreateFactory((request, _) =>
        {
            if (request.RequestUri?.AbsolutePath == ApiOverviewRoute)
            {
                return Task.FromResult(OverviewResponse());
            }

            exportCalls++;
            return Task.FromResult(FileResponse([], MediaType, "configuration-manifest-overrides.json"));
        });
        using var client = CreateClient(factory);
        using var request = AuthenticatedRequest($"{BffRoute}?view=Overrides");

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(exportCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments("application/json", "configuration-manifest-overrides.json")]
    [Arguments(MediaType, "unexpected.json")]
    public async Task Download_RejectsInvalidDownstreamMediaTypeOrFileName(
        string contentType,
        string fileName)
    {
        await using var factory = CreateFactory((request, _) => Task.FromResult(
            request.RequestUri?.AbsolutePath == ApiOverviewRoute
                ? OverviewResponse("export-configuration-overrides")
                : FileResponse([1, 2, 3], contentType, fileName)));
        using var client = CreateClient(factory);
        using var request = AuthenticatedRequest($"{BffRoute}?view=Overrides");

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadGateway);
        await Assert.That(await response.Content.ReadAsStringAsync()).DoesNotContain(fileName);
    }

    [Test]
    public async Task Download_RejectsPayloadBeyondFourMiB()
    {
        await using var factory = CreateFactory((request, _) => Task.FromResult(
            request.RequestUri?.AbsolutePath == ApiOverviewRoute
                ? OverviewResponse("export-configuration-overrides")
                : FileResponse(
                    new byte[MaximumBytes + 1],
                    MediaType,
                    "configuration-manifest-overrides.json")));
        using var client = CreateClient(factory);
        using var request = AuthenticatedRequest($"{BffRoute}?view=Overrides");

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadGateway);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }

    [Test]
    public async Task Download_MapsDownstreamFailureWithoutLeakingBody()
    {
        await using var factory = CreateFactory((request, _) => Task.FromResult(
            request.RequestUri?.AbsolutePath == ApiOverviewRoute
                ? OverviewResponse("export-configuration-overrides")
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = JsonContent.Create(new
                    {
                        title = "provider-internal",
                        detail = "access_token=downstream-secret"
                    })
                }));
        using var client = CreateClient(factory);
        using var request = AuthenticatedRequest($"{BffRoute}?view=Overrides");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(body).DoesNotContain("downstream-secret");
        await Assert.That(body).DoesNotContain("provider-internal");
    }

    private static WebApplicationFactory<Program> CreateFactory(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        new BlazorBffWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var http = new HttpClient(new StubHttpMessageHandler(handler))
                {
                    BaseAddress = new Uri("https://api.test/")
                };
                services.RemoveAll<IControlPlaneClient>();
                services.AddSingleton<IControlPlaneClient>(new ControlPlaneClient(http));
                services.RemoveAll<IControl_Plane_ConfigurationClient>();
                services.AddSingleton<IControl_Plane_ConfigurationClient>(new Control_Plane_ConfigurationClient(http));
                services.RemoveAll<ITenantOnboardingClient>();
                services.AddSingleton<ITenantOnboardingClient>(new TenantOnboardingClient(http));
                services.RemoveAll<ITenant_ConfigurationClient>();
                services.AddSingleton<ITenant_ConfigurationClient>(new Tenant_ConfigurationClient(http));
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private static HttpRequestMessage AuthenticatedRequest(string route)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateInstanceAdminHeaderValue(Guid.NewGuid()));
        return request;
    }

    private static HttpResponseMessage OverviewResponse(params string[] relations) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new HalResourceOfControlPlaneOverviewDto
            {
                _links = relations.ToDictionary(
                    relation => relation,
                    relation => new HalLink
                    {
                        Href = $"{ApiExportRoute}?view=Overrides",
                        Method = "GET"
                    },
                    StringComparer.Ordinal)
            })
        };

    private static HttpResponseMessage FileResponse(
        byte[] payload,
        string contentType,
        string fileName)
    {
        var content = new ByteArrayContent(payload);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileNameStar = fileName
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        };
    }


    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
