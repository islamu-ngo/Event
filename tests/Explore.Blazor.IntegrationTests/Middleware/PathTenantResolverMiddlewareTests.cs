using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.IntegrationTests.Middleware;

public class PathTenantResolverMiddlewareTests
{
    [Test]
    public async Task Request_WithTenantSlugInPath_ExtractsSlugAndRewritesPath()
    {
        using var factory = new BlazorBffWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/t/acme/test/tenant-info");
        var payload = await response.Content.ReadFromJsonAsync<TenantInfoResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(payload).IsNotNull();
        await Assert.That(payload!.Slug).IsEqualTo("acme");
        await Assert.That(payload.Path).IsEqualTo("/test/tenant-info");
        await Assert.That(payload.PathBase).IsEqualTo("/t/acme");
    }

    [Test]
    public async Task BlazorDocument_WithTenantSlugInPath_UsesTenantBaseHref()
    {
        using var factory = new BlazorBffWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid()));

        var response = await client.GetAsync("/t/acme/admin/tenant/settings");
        var document = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(document).Contains("<base href=\"/t/acme/\"");
    }

    [Test]
    public async Task Request_WithoutTenantPrefix_PassesThrough()
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
    public async Task Request_WithTenantPrefixOnly_NoSlug_PassesThrough()
    {
        using var factory = new BlazorBffWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/t/");
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
            PathPrefix = "/t"
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/t/acme/test/tenant-info");
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
            PathPrefix = "/tenant"
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/tenant/acme/test/tenant-info");
        var payload = await response.Content.ReadFromJsonAsync<TenantInfoResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(payload).IsNotNull();
        await Assert.That(payload!.Slug).IsEqualTo("acme");
        await Assert.That(payload.Path).IsEqualTo("/test/tenant-info");
        await Assert.That(payload.PathBase).IsEqualTo("/tenant/acme");
    }

    private sealed class TenantInfoResponse
    {
        public string? Slug { get; set; }

        public string? Path { get; set; }

        public string? PathBase { get; set; }
    }
}
