using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.InstanceAdmin;
using Explore.Blazor.Configuration;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class BffAuthenticationCredentialReceiptTests
{
    private const string Route = "/bff/auth/credential-acquisition-receipts";
    private const string OverviewRoute = "/api/admin/instance/overview";

    [Test]
    public async Task AnonymousRequestCannotInspectProcessAcquisitions()
    {
        using var factory = CreateFactory(new(), () =>
            throw new InvalidOperationException("Anonymous inspection must not reach the operator API."));
        using var client = CreateClient(factory);
        using var response = await client.GetAsync(Route);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    [Arguments("missing")]
    [Arguments("post")]
    [Arguments("blank")]
    public async Task AuthenticationDoesNotSubstituteForFreshGetCapability(string capability)
    {
        Guid profile = Guid.CreateVersion7();
        var receipts = CreateReceipts(profile);
        using var factory = CreateFactory(receipts, () => Overview(profile, "Environment", capability));
        using var client = CreateClient(factory);
        using var request = AuthenticatedRequest();
        using var response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(await response.Content.ReadAsStringAsync())
            .DoesNotContain(receipts.ProcessBootId.ToString("D"));
    }

    [Test]
    [Arguments("missing-profile")]
    [Arguments("wrong-profile")]
    [Arguments("wrong-authority")]
    [Arguments("no-acquisition")]
    public async Task AcquisitionsCannotBeExportedAgainstAnotherSelectedProfile(string rejection)
    {
        Guid profile = Guid.CreateVersion7();
        var receipts = rejection == "no-acquisition" ? new(profile)
            : CreateReceipts(rejection == "missing-profile" ? null : profile);
        Guid apiRevision = rejection == "wrong-profile" ? Guid.CreateVersion7() : profile;
        string authority = rejection == "wrong-authority" ? "Infisical" : "Environment";
        using var factory = CreateFactory(receipts, () => Overview(apiRevision, authority));
        using var client = CreateClient(factory);
        using var request = AuthenticatedRequest();
        using var response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(await response.Content.ReadAsStringAsync())
            .DoesNotContain(receipts.ProcessBootId.ToString("D"));
    }

    [Test]
    public async Task InspectionReturnsOnlyLocalFixedMetadataAndRechecksRevokedCapability()
    {
        Guid profile = Guid.CreateVersion7();
        var receipts = CreateReceipts(profile);
        Guid generation = receipts.Snapshot().Single().Generation;
        string canary = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        bool allowed = true;
        using var factory = CreateFactory(receipts, () =>
            Overview(profile, "Environment", allowed ? "get" : "missing", canary));
        using var client = CreateClient(factory);
        using (var firstRequest = AuthenticatedRequest())
        using (var firstResponse = await client.SendAsync(firstRequest))
        {
            await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(firstResponse.Headers.CacheControl?.NoStore).IsTrue();
            string body = await firstResponse.Content.ReadAsStringAsync();
            using var parsed = JsonDocument.Parse(body);
            var receipt = parsed.RootElement.EnumerateArray().Single();
            await Assert.That(receipt.GetProperty("processBootId").GetGuid()).IsEqualTo(receipts.ProcessBootId);
            await Assert.That(receipt.GetProperty("generation").GetGuid()).IsEqualTo(generation);
            await Assert.That(receipt.GetProperty("authorityProfileRevision").GetGuid()).IsEqualTo(profile);
            await Assert.That(receipt.GetProperty("installed").GetBoolean()).IsTrue();
            await Assert.That(body.Contains(canary, StringComparison.Ordinal)).IsFalse();
        }
        allowed = false;
        using var revokedRequest = AuthenticatedRequest();
        using var revokedResponse = await client.SendAsync(revokedRequest);
        await Assert.That(revokedResponse.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(await revokedResponse.Content.ReadAsStringAsync())
            .DoesNotContain(receipts.ProcessBootId.ToString("D"));
    }

    [Test]
    public async Task DownstreamFailureCannotForwardProviderResponseContents()
    {
        string canary = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        using var factory = CreateFactory(CreateReceipts(Guid.CreateVersion7()), () =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = JsonContent.Create(new { title = "provider-internal", detail = canary })
            });
        using var client = CreateClient(factory);
        using var request = AuthenticatedRequest();
        using var response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(await response.Content.ReadAsStringAsync()).DoesNotContain(canary);
    }

    private static AuthenticationCredentialAcquisitionReceipts CreateReceipts(Guid? profile)
    {
        var receipts = new AuthenticationCredentialAcquisitionReceipts(profile);
        receipts.RecordAcquired(AuthenticationCredentialAcquisitionReceipts.Consumer.GoogleOidc,
            AuthenticationCredentialAcquisitionReceipts.Authority.Environment);
        receipts.SetInstalled(AuthenticationCredentialAcquisitionReceipts.Consumer.GoogleOidc, true);
        return receipts;
    }

    private static HttpResponseMessage Overview(
        Guid profile, string authority, string capability = "get", string? canary = null) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                authenticationCredentialProfileRevision = profile,
                authenticationCredentialAuthority = authority,
                providerSecret = canary,
                _links = capability == "missing" ? new Dictionary<string, HalLink>()
                    : new Dictionary<string, HalLink>
                    {
                        [InstanceAdminLinkRelations.AuthenticationCredentialCutovers] = new()
                        {
                            Href = capability == "blank" ? " " : "/api/admin/instance/authentication-credential-cutovers/current",
                            Method = capability == "post" ? "POST" : "GET"
                        }
                    }
            })
        };

    private static WebApplicationFactory<Program> CreateFactory(
        AuthenticationCredentialAcquisitionReceipts receipts, Func<HttpResponseMessage> response) =>
        new BlazorBffWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var http = new HttpClient(new OverviewTransport(response))
                {
                    BaseAddress = new Uri("https://api.test/")
                };
                services.RemoveAll<IInstanceAdminClient>();
                services.AddSingleton<IInstanceAdminClient>(new InstanceAdminClient(http));
                services.RemoveAll<AuthenticationCredentialAcquisitionReceipts>();
                services.AddSingleton(receipts);
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private static HttpRequestMessage AuthenticatedRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Route);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateInstanceAdminHeaderValue(Guid.CreateVersion7()));
        return request;
    }

    private sealed class OverviewTransport(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath != OverviewRoute)
                throw new InvalidOperationException("Unexpected downstream request during credential inspection.");
            return Task.FromResult(response());
        }
    }
}
