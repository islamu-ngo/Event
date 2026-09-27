using Explore.Blazor.Client.Clients;
using Explore.Blazor.Middleware;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explore.Blazor.IntegrationTests.Middleware;

public class PathTenantResolverMiddlewareTests
{
    [Test]
    public async Task Request_WithRootSlug_ExtractsSlugAndRewritesPath()
    {
        using var baseFactory = new BlazorBffWebApplicationFactory();
        using var factory = baseFactory.WithResolverConfiguration(CreateRootConfiguration());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/acme/test/tenant-info");
        var payload = await response.Content.ReadFromJsonAsync<TenantInfoResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(payload).IsNotNull();
        await Assert.That(payload!.Slug).IsEqualTo("acme");
        await Assert.That(payload.Path).IsEqualTo("/test/tenant-info");
        await Assert.That(payload.PathBase).IsEqualTo("/acme");
    }

    [Test]
    public async Task Request_WithReservedRootSegment_PassesThrough()
    {
        using var baseFactory = new BlazorBffWebApplicationFactory();
        using var factory = baseFactory.WithResolverConfiguration(CreateRootConfiguration());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid()));

        var response = await client.GetAsync("/admin/tenant/settings");
        var document = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(document).Contains("<base href=\"/\"");
    }

    [Test]
    [Arguments("/_framework/blazor.web.js")]
    [Arguments("/api/events")]
    [Arguments("/payments/checkout/success")]
    [Arguments("/forbidden")]
    [Arguments("/oauth/client-metadata.json")]
    [Arguments("/manifest.webmanifest")]
    [Arguments("/test-endpoint")]
    [Arguments("/appsettings.json")]
    [Arguments("/appsettings.Development.json")]
    [Arguments("/Explore.Blazor.fingerprint.styles.css")]
    [Arguments("/push-service-worker.fingerprint.js")]
    public async Task Request_WithInfrastructurePath_PassesThrough(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        var routeContext = new TenantRouteContextAccessor(
            new HttpContextAccessor { HttpContext = context });
        var configurationProvider = Substitute.For<IBffResolverConfigurationProvider>();
        configurationProvider.GetConfigurationAsync(Arg.Any<CancellationToken>())
            .Returns(CreateRootConfiguration());
        var nextCalled = false;
        var middleware = new PathTenantResolverMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, configurationProvider, routeContext);

        await Assert.That(nextCalled).IsTrue();
        await Assert.That(context.Request.Path.Value).IsEqualTo(path);
        await Assert.That(context.Request.PathBase.Value).IsEqualTo(string.Empty);
        await Assert.That(routeContext.TenantSlug).IsNull();
    }

    [Test]
    public async Task Request_WithoutReservedCatalog_DoesNotSelectRootTenant()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/acme/events";
        var routeContext = new TenantRouteContextAccessor(
            new HttpContextAccessor { HttpContext = context });
        var configurationProvider = Substitute.For<IBffResolverConfigurationProvider>();
        configurationProvider.GetConfigurationAsync(Arg.Any<CancellationToken>())
            .Returns(new ResolverConfigurationDto
            {
                PathEnabled = true,
                PathPrefix = string.Empty,
                ReservedSlugs = []
            });
        var middleware = new PathTenantResolverMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, configurationProvider, routeContext);

        await Assert.That(context.Request.Path.Value).IsEqualTo("/acme/events");
        await Assert.That(context.Request.PathBase.Value).IsEqualTo(string.Empty);
        await Assert.That(routeContext.TenantSlug).IsNull();
    }

    [Test]
    public async Task BlazorDocument_WithRootSlug_UsesTenantBaseHref()
    {
        using var baseFactory = new BlazorBffWebApplicationFactory();
        using var factory = baseFactory.WithResolverConfiguration(CreateRootConfiguration());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid()));

        var response = await client.GetAsync("/acme/admin/tenant/settings");
        var document = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(document).Contains("<base href=\"/acme/\"");
    }

    [Test]
    [Arguments("/actors/018e4e5c-7f00-7000-8000-000000000001")]
    [Arguments("/home")]
    [Arguments("/settings/admin")]
    [Arguments("/users/42")]
    [Arguments("/organizations/42")]
    [Arguments("/organization/42")]
    [Arguments("/group/42")]
    [Arguments("/event-created")]
    [Arguments("/Explore.Blazor.styles.css")]
    [Arguments("/Explore.Blazor.Client.bundle.scp.css")]
    [Arguments("/push-service-worker.js")]
    [Arguments("/image/Icon_landingpage.png")]
    [Arguments("/appsettings.json")]
    [Arguments("/appsettings.Development.json")]
    [Arguments("/Explore.Blazor.fingerprint.styles.css")]
    [Arguments("/push-service-worker.fingerprint.js")]
    public async Task ResolverConfiguration_WhenApiReadFails_PreservesMappedRoot(string path)
    {
        var apiClient = Substitute.For<IInstanceMessagingSettingsClient>();
        apiClient.GetInstanceResolverConfigurationAsync(
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ResolverConfigurationDto>(
                new HttpRequestException("Resolver API unavailable")));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new BffResolverConfigurationProvider(
            apiClient, cache, NullLogger<BffResolverConfigurationProvider>.Instance);

        var configuration = await provider.GetConfigurationAsync();

        await Assert.That(configuration.PathEnabled).IsTrue();
        await Assert.That(configuration.PathPrefix).IsEqualTo(string.Empty);
        await Assert.That(configuration.ReservedSlugs).Contains("admin");
        await Assert.That(configuration.ReservedSlugs).Contains("_framework");
        await Assert.That(configuration.ReservedSlugs).Contains("events");

        var context = new DefaultHttpContext();
        context.Request.Path = path;
        var routeContext = new TenantRouteContextAccessor(
            new HttpContextAccessor { HttpContext = context });
        var nextCalled = false;
        var middleware = new PathTenantResolverMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, provider, routeContext);

        await Assert.That(nextCalled).IsTrue();
        await Assert.That(context.Request.Path.Value).IsEqualTo(path);
        await Assert.That(context.Request.PathBase.Value).IsEqualTo(string.Empty);
        await Assert.That(routeContext.TenantSlug).IsNull();
    }

    [Test]
    public async Task Request_WithReservedTestEndpoint_PassesThrough()
    {
        using var factory = new BlazorBffWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/test/tenant-info");
        var payload = await response.Content.ReadFromJsonAsync<TenantInfoResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(payload).IsNotNull();
        await Assert.That(payload!.Slug).IsNull();
        await Assert.That(payload.Path).IsEqualTo("/test/tenant-info");
        await Assert.That(payload.PathBase).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Request_AtRootWithoutSlug_PassesThrough()
    {
        using var factory = new BlazorBffWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var contentType = response.Content.Headers.ContentType?.MediaType;

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(contentType).IsEqualTo("text/html");
    }

    [Test]
    public async Task Request_WithPathResolutionDisabled_PassesThrough()
    {
        using var baseFactory = new BlazorBffWebApplicationFactory();
        using var factory = baseFactory.WithResolverConfiguration(new ResolverConfigurationDto
        {
            PathEnabled = false,
            PathPrefix = string.Empty
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/acme/test/tenant-info");
        var contentType = response.Content.Headers.ContentType?.MediaType;

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(contentType).IsEqualTo("text/html");
    }

    [Test]
    public async Task Request_WithCustomPathPrefix_ExtractsCorrectly()
    {
        using var baseFactory = new BlazorBffWebApplicationFactory();
        using var factory = baseFactory.WithResolverConfiguration(new ResolverConfigurationDto
        {
            PathEnabled = true,
            PathPrefix = "/communities"
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/communities/acme/test/tenant-info");
        var payload = await response.Content.ReadFromJsonAsync<TenantInfoResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(payload).IsNotNull();
        await Assert.That(payload!.Slug).IsEqualTo("acme");
        await Assert.That(payload.Path).IsEqualTo("/test/tenant-info");
        await Assert.That(payload.PathBase).IsEqualTo("/communities/acme");
    }

    private static ResolverConfigurationDto CreateRootConfiguration() => new()
    {
        PathEnabled = true,
        PathPrefix = string.Empty,
        ReservedSlugs =
            ["admin", "_framework", "events", "api", "test", "payments", "forbidden",
             "oauth", "manifest.webmanifest", "test-endpoint"]
    };

    private sealed class TenantInfoResponse
    {
        public string? Slug { get; set; }

        public string? Path { get; set; }

        public string? PathBase { get; set; }
    }
}
