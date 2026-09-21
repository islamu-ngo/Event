using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed class ApiFixtureLifecycleTests
{
    [Test]
    public async Task OrdinaryApiFixtureHasActiveDefaultAndDeniesProvisioning()
    {
        await using var fixture = new ApiTestFixture();
        await fixture.InitializeAsync();
        await AssertLifecycleBoundaryAsync(fixture.Factory, fixture.Client);
    }

    [Test]
    public async Task ExternalApiFixtureHasActiveDefaultAndDeniesProvisioning()
    {
        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DeploymentMode = DeploymentMode.SingleTenant
        };
        using var client = factory.CreateClient();
        await AssertLifecycleBoundaryAsync(factory, client);
    }

    [Test]
    public async Task PublishedAuthenticatedFixtureHasActiveDefaultAndDeniesProvisioning()
    {
        await using var factory = new AuthenticatedWebApplicationFactory { SeedActiveDefaultTenant = true };
        using var client = factory.CreateClient();
        await AssertLifecycleBoundaryAsync(factory, client);
    }

    [Test]
    public async Task FreshAuthenticatedFixtureDoesNotManufacturePublishedTenant()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await database.Tenants.AnyAsync()).IsFalse();
        using var response = await client.GetAsync("/api/category");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    private static async Task AssertLifecycleBoundaryAsync(WebApplicationFactory<Program> factory, HttpClient client)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await database.Tenants.SingleAsync(tenant => tenant.Id == PlatformDefaults.DefaultTenantId);
        await Assert.That(tenant.TenantStatusId).IsEqualTo((int)TenantStatusEnum.Active);
        using var active = await client.GetAsync("/api/category");
        await Assert.That(active.StatusCode).IsEqualTo(HttpStatusCode.OK);

        tenant.TenantStatusId = (int)TenantStatusEnum.Provisioning;
        await database.SaveChangesAsync();
        using var provisioning = await client.GetAsync("/api/category");
        await Assert.That(provisioning.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(provisioning.Headers.CacheControl!.NoStore).IsTrue();
        using var body = JsonDocument.Parse(await provisioning.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("code").GetString()).IsEqualTo("tenant_lifecycle_unavailable");
    }
}
