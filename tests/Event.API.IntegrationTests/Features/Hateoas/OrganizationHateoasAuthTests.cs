using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Identity;
using Explore.Persistence;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features.Hateoas;

[NotInParallel("AuthenticatedApiFixture")]
[ClassDataSource<AuthenticatedApiTestFixture>(Shared = SharedType.PerAssembly)]
public class OrganizationHateoasAuthTests
{
    private readonly AuthenticatedApiTestFixture _fixture;
    private const string BaseUrl = "/api/organization";

    public OrganizationHateoasAuthTests(AuthenticatedApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static string WithCacheBust(string endpoint)
    {
        var separator = endpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{endpoint}{separator}pageNumber=1&pageSize=20&testRun={Guid.NewGuid():N}";
    }

    [Test]
    public async Task GetOrganizations_AsAuthenticatedUser_EmbeddedItemsIncludeEditLink()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var seed = await TenantScenarioSeed.SeedActiveTenantWithOrganizationPublisherAsync(context);

        // Evict from cache to make sure the fresh roles are loaded
        var cache = scope.ServiceProvider.GetRequiredService<IAdminCacheInvalidator>();
        cache.InvalidateUser(seed.UserId);

        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Get, WithCacheBust(BaseUrl), seed.UserId);
        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!TryGetEmbeddedItemById(json, seed.OrganizationId, out var item))
        {
            Assert.Fail("Expected seeded organization item in response.");
            return;
        }

        if (item.TryGetProperty("_links", out var itemLinks))
        {
            var hasEditLink = itemLinks.TryGetProperty("edit", out _);
            await Assert.That(hasEditLink).IsTrue();
        }
        else
        {
            Assert.Fail("Expected _links to be present on the organization item.");
        }
    }

    [Test]
    public async Task GetOrganizations_AsAuthenticatedUser_EmbeddedItemsIncludeDeleteLink()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var seed = await TenantScenarioSeed.SeedActiveTenantWithOrganizationPublisherAsync(context);

        // Evict from cache to make sure the fresh roles are loaded
        var cache = scope.ServiceProvider.GetRequiredService<IAdminCacheInvalidator>();
        cache.InvalidateUser(seed.UserId);

        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Get, WithCacheBust(BaseUrl), seed.UserId);
        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!TryGetEmbeddedItemById(json, seed.OrganizationId, out var item))
        {
            Assert.Fail("Expected seeded organization item in response.");
            return;
        }

        if (item.TryGetProperty("_links", out var itemLinks))
        {
            var hasDeleteLink = itemLinks.TryGetProperty("delete", out _);
            await Assert.That(hasDeleteLink).IsTrue();
        }
        else
        {
            Assert.Fail("Expected _links to be present on the organization item.");
        }
    }

    [Test]
    public async Task GetOrganizations_Anonymous_EmbeddedItemsDoNotIncludeManagementLinks()
    {
        var response = await _fixture.Client.GetAsync(WithCacheBust(BaseUrl));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!TryGetFirstEmbeddedItem(json, out var firstItem))
        {
            return;
        }

        if (firstItem.TryGetProperty("_links", out var itemLinks))
        {
            var hasEditLink = itemLinks.TryGetProperty("edit", out _);
            var hasDeleteLink = itemLinks.TryGetProperty("delete", out _);

            await Assert.That(hasEditLink).IsFalse();
            await Assert.That(hasDeleteLink).IsFalse();
        }
    }

    private static bool TryGetFirstEmbeddedItem(JsonDocument json, out JsonElement firstItem)
    {
        firstItem = default;
        if (!json.RootElement.TryGetProperty("_embedded", out var embedded) ||
            !embedded.TryGetProperty("items", out var items) ||
            items.GetArrayLength() == 0)
        {
            return false;
        }

        firstItem = items[0];
        return true;
    }

    private static bool TryGetEmbeddedItemById(JsonDocument json, Guid id, out JsonElement matchedItem)
    {
        matchedItem = default;
        if (!json.RootElement.TryGetProperty("_embedded", out var embedded) ||
            !embedded.TryGetProperty("items", out var items))
        {
            return false;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("id", out var idProp) &&
                idProp.TryGetGuid(out var itemGuid) &&
                itemGuid == id)
            {
                matchedItem = item;
                return true;
            }
        }

        return false;
    }
}
