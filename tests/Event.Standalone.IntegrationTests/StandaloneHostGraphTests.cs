// ABOUTME: Verifies the standalone host exposes one explicitly owned API, BFF, UI, and health graph.
// ABOUTME: Exercises referenced static assets and guards against duplicate controllers or YARP self-routing.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Event.Web.BffHosting.Options;
using Explore.API.Configuration;
using Explore.API.Hosting;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Extensions;
using Explore.Blazor.Services;
using Explore.Blazor.Services.Auth;
using Event.Standalone.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Event.Standalone.IntegrationTests;

public sealed class StandaloneHostGraphTests
{
    [Test]
    public async Task EndpointGraphMapsEachOwnedSurfaceOnceWithoutYarp()
    {
        await using var factory = new StandaloneWebApplicationFactory();
        _ = factory.CreateClient();
        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
        var patterns = endpoints
            .Select(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/'))
            .ToArray();
        var apiControllerRoutes = endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType()
                .Assembly == typeof(Explore.API.Hosting.ApiHostApplicationExtensions).Assembly)
            .Select(endpoint => new
            {
                Pattern = endpoint.RoutePattern.RawText,
                Methods = string.Join(",", endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
            })
            .ToArray();

        await Assert.That(patterns).Contains("api/EventType");
        await Assert.That(patterns).Contains("auth/status");
        await Assert.That(patterns).Contains("bff/theme");
        await Assert.That(patterns).Contains("oauth/client-metadata.json");
        await Assert.That(patterns).Contains("manifest.webmanifest");
        await Assert.That(patterns).Contains("css/layers.css");
        await Assert.That(patterns).Contains("_blazor");
        await Assert.That(patterns).Contains("health");
        await Assert.That(apiControllerRoutes.Length).IsGreaterThan(0);
        await Assert.That(apiControllerRoutes.GroupBy(route => route).Any(group => group.Count() > 1)).IsFalse();
        await Assert.That(endpoints.Any(endpoint =>
            endpoint.DisplayName?.Contains("YARP", StringComparison.OrdinalIgnoreCase) == true)).IsFalse();
    }

    [Test]
    public async Task RealHostServesApiBffStaticAssetAndHealth()
    {
        await using var factory = new StandaloneWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var api = await client.GetAsync("/api/EventType");
        using var auth = await client.GetAsync("/auth/status");
        using var css = await client.GetAsync("/css/layers.css");
        using var health = await client.GetAsync("/alive");
        var cssContent = await css.Content.ReadAsStringAsync();

        await Assert.That(api.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(auth.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(css.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(cssContent).Contains("@layer reset, base, tokens");
        await Assert.That(health.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task TenantManifestStartupCompletesBeforeTheHostAcceptsTraffic()
    {
        var startup = new ConfigurationManifestStartupProbe();
        await using var factory = new StandaloneWebApplicationFactory(
            startupRunner: startup);

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/alive");

        await Assert.That(startup.RunCount).IsEqualTo(1);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task UnknownApiRouteStaysInApiPipeline()
    {
        await using var factory = new StandaloneWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        using var response = await client.GetAsync("/api/definitely-not-a-route");
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        await Assert.That(body).Contains("\"status\":404");
        await Assert.That(response.Headers.GetValues("X-Content-Type-Options").Single()).IsEqualTo("nosniff");
        await Assert.That(response.Headers.GetValues("Content-Security-Policy").Single())
            .IsEqualTo("default-src 'none'; frame-ancestors 'none'");
        await Assert.That(body).DoesNotContain("<!DOCTYPE html");
        await Assert.That(body).DoesNotContain("Blazor");
    }

    [Test]
    public async Task ApiBridgeRunsBeforeApiAuthenticationConflictMiddleware()
    {
        await using var factory = new StandaloneWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/EventType");
        request.Headers.Add("Authorization", "Bearer external-token");
        request.Headers.Add("X-API-Key", "external-key");

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ApiRouteClassifierCoversEveryCurrentApiControllerActionRoute()
    {
        await using var factory = new StandaloneWebApplicationFactory();
        _ = factory.CreateClient();
        var classifier = new ApiHostRouteClassifier(
            factory.Services.GetServices<EndpointDataSource>(),
            factory.Services.GetRequiredService<IOptions<McpAdapterSettings>>().Value.EndpointPath,
            factory.Services.GetRequiredService<IConfiguration>()
                .GetValue<string>("Scheduler:Quartz:StatusEndpointPath") ?? "/admin/scheduler");
        var apiControllerRoutes = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.Assembly
                == typeof(ApiHostApplicationExtensions).Assembly)
            .ToArray();

        await Assert.That(apiControllerRoutes.Length).IsGreaterThan(0);
        await Assert.That(apiControllerRoutes.Where(endpoint => !classifier.IsApiOwned(endpoint))).IsEmpty();
        await Assert.That(classifier.IsApiOwned(new PathString("/sitemap.xml"))).IsTrue();
        await Assert.That(classifier.IsApiOwned(new PathString("/vapid-public-key"))).IsTrue();
        await Assert.That(classifier.IsApiOwned(new PathString("/api/definitely-not-a-route"))).IsTrue();
    }

    [Test]
    public async Task TrustedForwardedHeadersReachUiAccessControlAndCookieBackedApiBridge()
    {
        await using var factory = new StandaloneWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        var bffOptions = factory.Services.GetRequiredService<IOptions<EventBffHostingOptions>>().Value;
        var cookieOptions = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())],
                CookieAuthenticationDefaults.AuthenticationScheme)),
            CreateTokenProperties(),
            CookieAuthenticationDefaults.AuthenticationScheme);
        var cookie = cookieOptions.TicketDataFormat.Protect(ticket);

        using var uiRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/status");
        uiRequest.Headers.Add("X-Forwarded-Host", bffOptions.AdminHosts.Single());
        uiRequest.Headers.Add("X-Forwarded-For", "198.51.100.42");
        using var uiResponse = await client.SendAsync(uiRequest);

        using var apiRequest = new HttpRequestMessage(HttpMethod.Get, "/api/EventType");
        apiRequest.Headers.Add("Cookie", $"{cookieOptions.Cookie.Name}={Uri.EscapeDataString(cookie)}");
        apiRequest.Headers.Add("X-Forwarded-Host", "tenant.proxy.test");
        apiRequest.Headers.Add("X-Forwarded-Proto", "https");
        apiRequest.Headers.Add("X-Forwarded-For", "203.0.113.42");
        using var apiResponse = await client.SendAsync(apiRequest);
        var bridgeObservation = factory.Services.GetRequiredService<ForwardedRequestProbe>()
            .Observations.Single();

        await Assert.That(uiResponse.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(apiResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(bridgeObservation.Scheme).IsEqualTo("https");
        await Assert.That(bridgeObservation.Host).IsEqualTo("tenant.proxy.test");
        await Assert.That(bridgeObservation.RemoteIpAddress).IsEqualTo(IPAddress.Parse("203.0.113.42"));
    }

    [Test]
    public async Task CombinedApiClientsUseTheExistingApiPipelineWithoutAnyListener()
    {
        await using var factory = new StandaloneWebApplicationFactory();
        using var publicClient = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();

        var apiClient = scope.ServiceProvider.GetRequiredService<IEventTypeClient>();
        var eventTypes = await apiClient.GetEventTypesAsync(cancellationToken: CancellationToken.None);

        var clientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        using var admin = clientFactory.CreateClient("AdminAuthority");
        using var atproto = clientFactory.CreateClient(ApiBackedOAuthSessionStore.HttpClientName);
        using var adminResponse = await admin.GetAsync("api/EventType");
        using var atprotoResponse = await atproto.GetAsync("api/EventType");

        await Assert.That(eventTypes).IsNotNull();
        await Assert.That(admin.BaseAddress).IsEqualTo(InProcessEventApiDispatcher.InternalBaseAddress);
        await Assert.That(atproto.BaseAddress).IsEqualTo(InProcessEventApiDispatcher.InternalBaseAddress);
        await Assert.That(adminResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(atprotoResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task CombinedHostResolvesEveryPerTagClientWithTheSharedInProcessTransport()
    {
        await using var factory = new StandaloneWebApplicationFactory();
        using var publicClient = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();

        var registrations = GeneratedEventApiClients.ClientTypes;
        var resolvedClients = registrations
            .Select(pair => scope.ServiceProvider.GetRequiredService(pair.InterfaceType))
            .ToArray();
        var eventTypeClient = scope.ServiceProvider.GetRequiredService<IEventTypeClient>();
        var eventTypes = await eventTypeClient.GetEventTypesAsync(cancellationToken: CancellationToken.None);
        using var configuredHttpClient = scope.ServiceProvider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(nameof(IEventTypeClient));

        await Assert.That(registrations).IsNotEmpty();
        await Assert.That(resolvedClients).Count().IsEqualTo(registrations.Length);
        await Assert.That(resolvedClients.Zip(registrations)
            .All(pair => pair.Second.ImplementationType.IsInstanceOfType(pair.First))).IsTrue();
        await Assert.That(configuredHttpClient.BaseAddress).IsEqualTo(InProcessEventApiDispatcher.InternalBaseAddress);
        await Assert.That(eventTypes).IsNotNull();
    }

    [Test]
    public async Task CombinedInternalApiMutationIsNotRejectedByBrowserAntiforgery()
    {
        await using var factory = new StandaloneWebApplicationFactory();
        using var publicClient = factory.CreateClient();
        var clientFactory = factory.Services.GetRequiredService<IHttpClientFactory>();
        using var internalClient = clientFactory.CreateClient("AdminAuthority");

        using var response = await internalClient.PostAsJsonAsync(
            "api/InstanceOnboarding/validate-secret",
            new { Secret = "test-secret" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Gone);
    }

    [Test]
    public async Task InternalProtectedRequestUsesBearerPipelineAndReturnsProblemDetails()
    {
        await using var factory = new StandaloneWebApplicationFactory();
        using var publicClient = factory.CreateClient();
        var clientFactory = factory.Services.GetRequiredService<IHttpClientFactory>();
        using var admin = clientFactory.CreateClient("AdminAuthority");
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");

        using var response = await admin.GetAsync("api/user/admin-authority");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
    }

    [Test]
    public async Task InternalApiResponseRunsRealPipelineStartingHeaders()
    {
        await using var factory = new StandaloneWebApplicationFactory();
        using var publicClient = factory.CreateClient();
        var clientFactory = factory.Services.GetRequiredService<IHttpClientFactory>();
        using var internalClient = clientFactory.CreateClient("AdminAuthority");
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/EventType");
        request.Headers.Add("Prefer", "return=minimal");
        request.Headers.Add("X-Correlation-ID", "standalone-transport-probe");

        using var response = await internalClient.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.GetValues("X-Content-Type-Options").Single()).IsEqualTo("nosniff");
        await Assert.That(response.Headers.GetValues("X-Correlation-ID").Single())
            .IsEqualTo("standalone-transport-probe");
        await Assert.That(response.Headers.GetValues("Preference-Applied").Single()).IsEqualTo("return=minimal");
    }

    [Test]
    public async Task ConcurrentColdRequestsInitializeDynamicSchemesOnceWithoutRecursion()
    {
        await using var factory = new StandaloneWebApplicationFactory();
        using var client = factory.CreateClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => client.GetAsync("/auth/status")));

        await Assert.That(responses.All(response => response.StatusCode == HttpStatusCode.OK)).IsTrue();
        await Assert.That(factory.Services.GetRequiredService<DynamicAuthInitializationProbe>().InitializationCount)
            .IsEqualTo(1);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Test]
    public async Task HealthBypassesDeferredDynamicSchemeInitialization()
    {
        await using var factory = new StandaloneWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/alive");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(factory.Services.GetRequiredService<DynamicAuthInitializationProbe>().InitializationCount)
            .IsEqualTo(0);
    }

    private static AuthenticationProperties CreateTokenProperties()
    {
        var properties = new AuthenticationProperties();
        string token = new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityToken(
                expires: new DateTime(
                    2100,
                    1,
                    1,
                    0,
                    0,
                    0,
                    DateTimeKind.Utc)));
        properties.StoreTokens([
            new AuthenticationToken
            {
                Name = "access_token",
                Value = token
            }
        ]);
        return properties;
    }
}
