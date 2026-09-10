
using System.Text;
using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class VisitorAccessBffDiscoveryTests
{
    private static CancellationToken Token => TestContext.Current!.Execution.CancellationToken;

    [Test]
    [Arguments("FullRegistrationAndAuth", true)]
    [Arguments("AnonymousOnly", false)]
    [Arguments("DirectoryListingOnly", false)]
    public async Task GeneratedVisitorFactsAndSignupLinksRemainSeparateFromLocalOperatorLogin(string mode, bool signup)
    {
        using var upstream = new DiscoveryTransport(mode, signup);
        await using var root = new BlazorBffWebApplicationFactory();
        await using var factory = CreateFactory(root, upstream);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var response = await client.GetAsync("/auth/providers", Token);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsEqualTo(true);
        await Assert.That(response.Headers.CacheControl?.Private).IsEqualTo(true);
        var body = await response.Content.ReadFromJsonAsync<BffAuthProvidersResponse>(cancellationToken: Token);
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.PrimaryProvider).IsEqualTo("local");
        await Assert.That(body.Providers.Single().Name).IsEqualTo("local");
        await Assert.That(body.VisitorAccess).IsNotNull();
        await Assert.That(body.VisitorAccess!.Mode?.ToString()).IsEqualTo(mode);
        await Assert.That(body.VisitorAccess.AllowsAccountRequiredParticipation).IsEqualTo(signup);
        await Assert.That(body.VisitorAccess.SignupDestinations!.Count).IsEqualTo(signup ? 1 : 0);
        await Assert.That(body._links!.ContainsKey("recover-password")).IsTrue();
        await Assert.That(body._links.ContainsKey("signup:google")).IsEqualTo(signup);
        if (signup)
        {
            await Assert.That(body.VisitorAccess.SignupDestinations!.Single().Url).IsEqualTo("https://accounts.example.test/create-account");
            await Assert.That(body.VisitorAccess.SignupDestinations.Single().Provider.ToString()).IsEqualTo("Google");
            await Assert.That(body._links["signup:google"].Href).IsEqualTo("https://accounts.example.test/create-account");
        }
    }

    [Test]
    public async Task UpstreamDiscoveryFailureDoesNotInventSignupOrHideLocalOperatorLogin()
    {
        using var upstream = new DiscoveryTransport("FullRegistrationAndAuth", false, HttpStatusCode.ServiceUnavailable);
        await using var root = new BlazorBffWebApplicationFactory();
        await using var factory = CreateFactory(root, upstream);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var response = await client.GetAsync("/auth/providers", Token);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BffAuthProvidersResponse>(cancellationToken: Token);
        await Assert.That(body!.Providers.Single().Name).IsEqualTo("local");
        await Assert.That(body.VisitorAccess).IsNull();
        await Assert.That(body._links).IsNull();
    }

    private static WebApplicationFactory<Program> CreateFactory(BlazorBffWebApplicationFactory root, DiscoveryTransport upstream) =>
        root.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Provider"] = "local",
                ["Keycloak:Authority"] = null,
                ["Keycloak:ClientId"] = null
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDynamicAuthSchemeManager>();
                services.AddSingleton<IDynamicAuthSchemeManager, DynamicAuthSchemeManager>();
                services.RemoveAll<IInstanceOnboardingClient>();
                services.AddHttpClient("VisitorDiscovery", client => client.BaseAddress = new Uri("https://api.example.test"))
                    .ConfigurePrimaryHttpMessageHandler(() => upstream);
                services.AddScoped<IInstanceOnboardingClient>(provider => new InstanceOnboardingClient(
                    provider.GetRequiredService<IHttpClientFactory>().CreateClient("VisitorDiscovery")));
            });
        });

    // Only the upstream HTTP transport is substituted. The BFF endpoint, scheme manager,
    // generated client and response deserializer are real; API resolver facts have their SQLite HTTP lane.
    private sealed class DiscoveryTransport(string mode, bool signup, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!request.RequestUri!.AbsolutePath.Equals("/api/InstanceOnboarding/auth-provider-configuration", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            string body = JsonSerializer.Serialize(new
            {
                primaryProviderId = 4,
                primaryProviderCode = "local",
                visitorAccess = new
                {
                    mode,
                    allowsNewNativeAllocation = mode != "DirectoryListingOnly",
                    allowsAnonymousParticipation = mode != "DirectoryListingOnly",
                    allowsAccountRequiredParticipation = signup,
                    allowsExistingAccountLogin = signup,
                    signupDestinations = signup ? new[] { new { provider = "Google", url = "https://accounts.example.test/create-account" } } : []
                },
                _links = signup
                    ? new Dictionary<string, object> { ["recover-password"] = new { href = "/api/auth/local/password-recoveries", method = "POST" }, ["signup:google"] = new { href = "https://accounts.example.test/create-account" } }
                    : new Dictionary<string, object> { ["recover-password"] = new { href = "/api/auth/local/password-recoveries", method = "POST" } }
            });
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
