using System.Text.Json;
using Explore.API.Hateoas;

namespace Event.Architecture.Tests;

public sealed class EventResourceAudienceOpenApiContractTests
{
    [Test]
    public async Task AudienceSchemasExposeOnlySafeTypedMetadataAndContinuation()
    {
        await using var stream = GeneratedContractInputs.OpenSchema();
        using var document = await JsonDocument.ParseAsync(stream);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var fields = schemas.GetProperty("EventResourceAudienceDetailDto").GetProperty("properties").EnumerateObject()
            .Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        await Assert.That(fields.SetEquals(["id", "eventId", "title", "kind", "isTeaser", "availability", "requirements",
            "description", "languageCode", "accessibilityNote", "accessibleAlternativeEventResourceId", "file"])).IsTrue();
        var page = schemas.GetProperty("EventResourceAudiencePageResource").GetProperty("properties");
        await Assert.That(page.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal)
            .SetEquals(["nextCursor", "_links", "_embedded"])).IsTrue();
        var item = schemas.GetProperty("HalCollectionEmbeddedOfEventResourceAudienceDetailDto")
            .GetProperty("properties").GetProperty("items").GetProperty("items");
        await Assert.That(item.GetProperty("$ref").GetString()).IsEqualTo("#/components/schemas/HalResourceOfEventResourceAudienceDetailDto");
        var detail = schemas.GetProperty("HalResourceOfEventResourceAudienceDetailDto").GetProperty("properties");
        await Assert.That(detail.GetProperty("id").GetProperty("format").GetString()).IsEqualTo("uuid");
        await Assert.That(detail.TryGetProperty("_links", out _)).IsTrue();
    }

    [Test]
    public async Task AudienceReadsAreNamedPrivateNonCachedContracts()
    {
        await using var stream = GeneratedContractInputs.OpenSchema();
        using var document = await JsonDocument.ParseAsync(stream);
        var paths = document.RootElement.GetProperty("paths");
        foreach (var (path, operationId) in new[]
        {
            ("/api/event/{eventId}/resources", RouteNames.ListEventResources),
            ("/api/eventresource/{id}", RouteNames.GetEventResourceAudienceDetail)
        })
        {
            var operation = paths.GetProperty(path).GetProperty("get");
            await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo(operationId);
            await Assert.That(operation.TryGetProperty("x-output-cache-policy", out _)).IsFalse();
            var responses = operation.GetProperty("responses");
            foreach (var status in new[] { "200", "400", "401", "403", "404", "503" })
                await Assert.That(responses.TryGetProperty(status, out _)).IsTrue();
        }
    }
}
