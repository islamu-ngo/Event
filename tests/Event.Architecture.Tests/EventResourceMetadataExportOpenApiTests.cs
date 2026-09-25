using System.Text.Json;
using Explore.API.Hateoas;

namespace Event.Architecture.Tests;

public sealed class EventResourceMetadataExportOpenApiTests
{
    [Test]
    public async Task ExportWireContractContainsOnlyPortableSemanticFields()
    {
        await using var stream = GeneratedContractInputs.OpenSchema();
        using var document = await JsonDocument.ParseAsync(stream);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var fields = schemas.GetProperty("EventResourceMetadataExportDto").GetProperty("properties").EnumerateObject()
            .Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        await Assert.That(fields.SetEquals(["id", "eventSessionId", "publicationState", "title", "publicTitle",
            "description", "sensitiveNotes", "kind", "disclosureMode", "deliveryType", "languageCode",
            "accessibilityNote", "sortOrder", "accessibleAlternativeEventResourceId", "availability", "audienceRules"])).IsTrue();
        var page = schemas.GetProperty("EventResourceMetadataExportPageDto").GetProperty("properties");
        await Assert.That(page.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal)
            .SetEquals(["eventId", "page", "pageSize", "items"])).IsTrue();
        await Assert.That(page.GetProperty("items").GetProperty("items").GetProperty("$ref").GetString())
            .IsEqualTo("#/components/schemas/EventResourceMetadataExportDto");
        var operation = document.RootElement.GetProperty("paths")
            .GetProperty("/api/event/{eventId}/resources/export").GetProperty("get");
        await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo(RouteNames.ExportEventResourceMetadata);
        await Assert.That(operation.TryGetProperty("x-output-cache-policy", out _)).IsFalse();
        foreach (var status in new[] { "200", "400", "401", "403", "404", "503" })
            await Assert.That(operation.GetProperty("responses").TryGetProperty(status, out _)).IsTrue();
    }
}
