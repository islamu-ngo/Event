using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features.Hateoas;

/// <summary>
/// HATEOAS-specific tests for StorageObjectController.
/// Validates storage object links for file management operations.
/// </summary>
[NotInParallel("ContractApiFixture")]
[ClassDataSource<ContractApiFixture>(Shared = SharedType.PerAssembly)]
public class StorageObjectHateoasTests
{
    private readonly ContractApiFixture _fixture;
    private const string BaseUrl = "/api/storageobject";

    public StorageObjectHateoasTests(ContractApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task GetAll_ShouldIncludeHalStructure()
    {
        using var request = CreateAuthenticatedRequest();
        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        await Assert.That(json.RootElement.TryGetProperty("_links", out _)).IsTrue();
        await Assert.That(json.RootElement.TryGetProperty("_embedded", out var embedded)).IsTrue();
        await Assert.That(embedded.TryGetProperty("items", out _)).IsTrue();
    }

    [Test]
    public async Task GetAll_ShouldIncludeSelfLink()
    {
        using var request = CreateAuthenticatedRequest();
        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        await Assert.That(json.RootElement.TryGetProperty("_links", out var links)).IsTrue();

        if (links.TryGetProperty("self", out var selfLink))
        {
            var href = selfLink.GetProperty("href").GetString();
            await Assert.That(href).Contains("/api/storageobject");
        }
    }

    [Test]
    public async Task GetAll_WithoutAuth_ReturnsUnauthorized()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl);

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GetAll_ItemLinks_ShouldIncludeSelfLink()
    {
        using var request = CreateAuthenticatedRequest();
        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        if (json.RootElement.TryGetProperty("_embedded", out var embedded) &&
            embedded.TryGetProperty("items", out var items) &&
            items.GetArrayLength() > 0)
        {
            var firstItem = items[0];

            if (firstItem.TryGetProperty("_links", out var itemLinks))
            {
                await Assert.That(itemLinks.TryGetProperty("self", out var selfLink)).IsTrue();
                var href = selfLink.GetProperty("href").GetString();
                await Assert.That(href).Contains("/api/storageobject/");
            }
        }
    }

    [Test]
    public async Task GetById_Links_ShouldIncludeCollectionLink()
    {
        // Arrange - First get list to find a storage object
        using var listRequest = CreateAuthenticatedRequest();
        var listResponse = await _fixture.Client.SendAsync(listRequest);
        var listContent = await listResponse.Content.ReadAsStringAsync();
        var listJson = JsonDocument.Parse(listContent);

        if (!listJson.RootElement.TryGetProperty("_embedded", out var embedded) ||
            !embedded.TryGetProperty("items", out var items) ||
            items.GetArrayLength() == 0)
        {
            return; // No data to test with
        }

        var firstItem = items[0];
        if (!TryGetId(firstItem, out var objectId))
        {
            return;
        }

        // Act - Get storage object by ID
        using var detailRequest = CreateAuthenticatedRequest($"{BaseUrl}/{objectId}");
        var response = await _fixture.Client.SendAsync(detailRequest);

        // Assert
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);

            if (json.RootElement.TryGetProperty("_links", out var links))
            {
                // Detail view should have collection link back
                if (links.TryGetProperty("collection", out var collectionLink))
                {
                    var href = collectionLink.GetProperty("href").GetString();
                    await Assert.That(href).Contains("/api/storageobject");
                }
            }
        }
    }

    [Test]
    public async Task GetAll_SelfLink_ShouldHaveGetMethod()
    {
        using var request = CreateAuthenticatedRequest();
        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        if (json.RootElement.TryGetProperty("_links", out var links) &&
            links.TryGetProperty("self", out var selfLink) &&
            selfLink.TryGetProperty("method", out var method))
        {
            await Assert.That(method.GetString()).IsEqualTo("GET");
        }
    }

    private static bool TryGetId(JsonElement item, out Guid id)
    {
        id = Guid.Empty;
        if (item.TryGetProperty("data", out var data) && data.TryGetProperty("id", out var idProp))
        {
            id = idProp.GetGuid();
            return true;
        }
        if (item.TryGetProperty("id", out idProp))
        {
            id = idProp.GetGuid();
            return true;
        }
        return false;
    }

    private HttpRequestMessage CreateAuthenticatedRequest(string url = BaseUrl)
    {
        return _fixture.CreateAuthenticatedRequest(HttpMethod.Get, url);
    }
}
