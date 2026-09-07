using System.Net.Http.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class BffAdmissionRecoveryEndpointTests : IAsyncDisposable
{
    private const string Capability = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private readonly IAdmissionTicketRecoveryClient _apiClient = Substitute.For<IAdmissionTicketRecoveryClient>();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public BffAdmissionRecoveryEndpointTests()
    {
        _factory = new BlazorBffWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAdmissionTicketRecoveryClient>();
                services.AddSingleton(_apiClient);
            });
        });
        _client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Test]
    public async Task ConsumeWithoutAntiforgeryNeverReachesApi()
    {
        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            Route,
            new AdmissionRecoveryBffRequest(Capability));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await _apiClient.DidNotReceiveWithAnyArgs()
            .ConsumeAdmissionTicketRecoveryAsync(default, default, default, default);
    }

    [Test]
    public async Task ConsumeForwardsCapabilityAndReturnsPrivateRedactedDelivery()
    {
        var delivery = new AdmissionTicketRecoveryDeliveryDto
        {
            Id = Guid.CreateVersion7(),
            TicketId = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            StatusCode = "ACTIVE",
            DisplayReference = "TKT-1234",
            ManualCode = "manual-sensitive-code",
            ManualCodeClassificationCode = "SENSITIVE_BEARER",
            QrRepresentation = "<svg/>",
            PrintModel = "print-model"
        };
        _apiClient.ConsumeAdmissionTicketRecoveryAsync(
                Capability,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(delivery);
        string token = await IssueAntiforgeryCookieAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, Route)
        {
            Content = JsonContent.Create(new AdmissionRecoveryBffRequest(Capability))
        };
        request.Headers.Add("X-CSRF-TOKEN", token);

        using HttpResponseMessage response = await _client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl!.NoStore).IsTrue();
        await Assert.That(response.Headers.CacheControl.Private).IsTrue();
        await Assert.That(response.Headers.GetValues("Referrer-Policy").Single())
            .IsEqualTo("no-referrer");
        await Assert.That(body).DoesNotContain(Capability);
        await _apiClient.Received(1).ConsumeAdmissionTicketRecoveryAsync(
            Capability,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task InvalidCapabilityUsesOneNotFoundFingerprint()
    {
        _apiClient.ConsumeAdmissionTicketRecoveryAsync(
                Capability,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<AdmissionTicketRecoveryDeliveryDto>(
                new ApiException(
                    "not found",
                    StatusCodes.Status404NotFound,
                    string.Empty,
                    new Dictionary<string, IEnumerable<string>>(),
                    null)));
        string token = await IssueAntiforgeryCookieAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, Route)
        {
            Content = JsonContent.Create(new AdmissionRecoveryBffRequest(Capability))
        };
        request.Headers.Add("X-CSRF-TOKEN", token);

        using HttpResponseMessage response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.Headers.CacheControl!.NoStore).IsTrue();
        await Assert.That(response.Headers.GetValues("Referrer-Policy").Single())
            .IsEqualTo("no-referrer");
    }

    [Test]
    public async Task LocallyInvalidCapabilitiesUseSameNotFoundFingerprint()
    {
        string token = await IssueAntiforgeryCookieAsync();
        string? fingerprint = null;
        foreach (string invalid in new[] { string.Empty, new string('A', 257) })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Route)
            {
                Content = JsonContent.Create(new AdmissionRecoveryBffRequest(invalid))
            };
            request.Headers.Add("X-CSRF-TOKEN", token);
            using HttpResponseMessage response = await _client.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(response.Headers.CacheControl!.NoStore).IsTrue();
            fingerprint ??= body;
            await Assert.That(body).IsEqualTo(fingerprint);
        }

        await _apiClient.DidNotReceiveWithAnyArgs()
            .ConsumeAdmissionTicketRecoveryAsync(default, default, default, default);
    }

    [Test]
    public async Task TicketPageUsesPrivateNoReferrerHeaders()
    {
        using HttpResponseMessage response = await _client.GetAsync("/tickets/recovery");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl!.NoStore).IsTrue();
        await Assert.That(response.Headers.CacheControl.Private).IsTrue();
        await Assert.That(response.Headers.GetValues("Referrer-Policy").Single())
            .IsEqualTo("no-referrer");
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<string> IssueAntiforgeryCookieAsync()
    {
        using HttpResponseMessage response = await _client.GetAsync("/auth/status");
        await Assert.That(response.Headers.TryGetValues("Set-Cookie", out var values)).IsTrue();
        return values!
            .Select(ReadXsrfToken)
            .First(value => !string.IsNullOrWhiteSpace(value))!;
    }

    private static string? ReadXsrfToken(string setCookie)
    {
        const string prefix = "XSRF-TOKEN=";
        if (!setCookie.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        int end = setCookie.IndexOf(';', prefix.Length);
        return Uri.UnescapeDataString(
            end < 0 ? setCookie[prefix.Length..] : setCookie[prefix.Length..end]);
    }

    private const string Route = "/bff/admission-recovery/consume";
}
