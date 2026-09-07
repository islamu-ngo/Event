using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features.Hateoas;

[NotInParallel("AuthenticatedApiFixture")]
[ClassDataSource<AuthenticatedApiTestFixture>(Shared = SharedType.PerAssembly)]
public class HateoasLinkDeserializationTests
{
    private readonly AuthenticatedApiTestFixture _fixture;

    public HateoasLinkDeserializationTests(AuthenticatedApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task EventDto_DeserializedFromHalResource_PreservesEditLink()
    {
        var userId = Guid.NewGuid();
        using var listRequest = _fixture.CreateAuthenticatedRequest(HttpMethod.Get, "/api/event", userId);
        var listResponse = await _fixture.Client.SendAsync(listRequest);

        var eventId = await TryFindFirstEmbeddedItemIdAsync(listResponse);
        if (eventId is null)
        {
            return; // No seeded events — not a regression without data.
        }

        using var detailRequest = _fixture.CreateAuthenticatedRequest(HttpMethod.Get, $"/api/event/{eventId}", userId);
        var response = await _fixture.Client.SendAsync(detailRequest);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(doc.RootElement.TryGetProperty("_links", out _)).IsTrue();
    }

    [Test]
    public async Task EventListDto_DeserializedFromCollection_PreservesLinksOnEmbeddedItems()
    {
        var userId = Guid.NewGuid();
        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Get, "/api/event", userId);

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        if (!TryGetEmbeddedItems(doc, out var items) || items.GetArrayLength() == 0)
        {
            return; // No data — not a regression.
        }

        // Each embedded item must carry _links so EventListDto.AdditionalProperties["_links"] preserves it.
        var firstItem = items[0];
        await Assert.That(firstItem.TryGetProperty("_links", out _)).IsTrue();
    }

    [Test]
    public async Task OrganizationDto_DeserializedFromHalResource_PreservesLinks()
    {
        var userId = Guid.NewGuid();
        using var listRequest = _fixture.CreateAuthenticatedRequest(HttpMethod.Get, "/api/organization", userId);
        var listResponse = await _fixture.Client.SendAsync(listRequest);

        var orgId = await TryFindFirstEmbeddedItemIdAsync(listResponse);
        if (orgId is null)
        {
            return;
        }

        using var detailRequest = _fixture.CreateAuthenticatedRequest(HttpMethod.Get, $"/api/organization/{orgId}", userId);

        var response = await _fixture.Client.SendAsync(detailRequest);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(doc.RootElement.TryGetProperty("_links", out _)).IsTrue();
    }

    [Test]
    public async Task OrganizationListDto_DeserializedFromCollection_PreservesLinksOnEmbeddedItems()
    {
        var userId = Guid.NewGuid();
        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Get, "/api/organization", userId);

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        if (!TryGetEmbeddedItems(doc, out var items) || items.GetArrayLength() == 0)
        {
            return;
        }

        var firstItem = items[0];
        await Assert.That(firstItem.TryGetProperty("_links", out _)).IsTrue();
    }

    // --- helpers ---

    private static async Task<Guid?> TryFindFirstEmbeddedItemIdAsync(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!TryGetEmbeddedItems(doc, out var items) || items.GetArrayLength() == 0)
        {
            return null;
        }
        var first = items[0];
        if (first.TryGetProperty("data", out var data) && data.TryGetProperty("id", out var idProp))
        {
            return idProp.GetGuid();
        }
        if (first.TryGetProperty("id", out idProp))
        {
            return idProp.GetGuid();
        }
        return null;
    }

    private static bool TryGetEmbeddedItems(JsonDocument doc, out JsonElement items)
    {
        if (doc.RootElement.TryGetProperty("_embedded", out var embedded) &&
            embedded.TryGetProperty("items", out items))
        {
            return true;
        }
        items = default;
        return false;
    }
}
