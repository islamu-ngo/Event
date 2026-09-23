using System.Text.Json;
using Explore.API.Extensions;
using Explore.API.Hateoas;

namespace Event.Architecture.Tests;

public sealed class EventResourceManagementOpenApiContractTests
{
    [Test]
    public async Task ManagementHalPreservesTypedMetadataAndEmbeddedItems()
    {
        await using var schema = GeneratedContractInputs.OpenSchema();
        using var document = await JsonDocument.ParseAsync(schema);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var detail = schemas.GetProperty("HalResourceOfEventResourceManagementDto").GetProperty("properties");
        await Assert.That(detail.GetProperty("version").GetProperty("format").GetString()).IsEqualTo("uuid");
        await Assert.That(detail.GetProperty("draft").GetProperty("$ref").GetString()).IsEqualTo("#/components/schemas/EventResourceDraftDto");
        await Assert.That(detail.TryGetProperty("_links", out _)).IsTrue();
        var item = schemas.GetProperty("HalCollectionEmbeddedOfEventResourceManagementDto")
            .GetProperty("properties").GetProperty("items").GetProperty("items");
        await Assert.That(item.GetProperty("$ref").GetString()).IsEqualTo("#/components/schemas/HalResourceOfEventResourceManagementDto");
    }

    [Test]
    public async Task ManagementWritesRequireReplayKeysWithoutAddingThemToReads()
    {
        await using var schema = GeneratedContractInputs.OpenSchema();
        using var document = await JsonDocument.ParseAsync(schema);
        var paths = document.RootElement.GetProperty("paths");
        (string Path, string Method, bool Write, string Operation)[] contracts =
        [
            ("/api/eventresource/{id}/management", "get", false, RouteNames.GetEventResourceManagementDetail),
            ("/api/event/{eventId}/resources/management", "get", false, RouteNames.ListEventResourceManagement),
            ("/api/eventresource/{id}/audit", "get", false, RouteNames.GetEventResourceAudit),
            ("/api/event/{eventId}/resources", "post", true, RouteNames.CreateEventResource),
            ("/api/eventresource/{id}", "put", true, RouteNames.UpdateEventResource),
            ("/api/eventresource/{id}", "delete", true, RouteNames.DeleteEventResource),
            ("/api/eventresource/{id}/publish", "post", true, RouteNames.PublishEventResource),
            ("/api/eventresource/{id}/unpublish", "post", true, RouteNames.UnpublishEventResource),
            ("/api/eventresource/{id}/archive", "post", true, RouteNames.ArchiveEventResource),
            ("/api/eventresource/{id}/moderate", "post", true, RouteNames.ModerateEventResource)
        ];
        foreach (var contract in contracts)
        {
            var operation = paths.GetProperty(contract.Path).GetProperty(contract.Method);
            await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo(contract.Operation);
            var keys = operation.GetProperty("parameters").EnumerateArray()
                .Where(parameter => parameter.GetProperty("name").GetString() == "Idempotency-Key").ToArray();
            await Assert.That(keys.Length).IsEqualTo(contract.Write ? 1 : 0);
            await Assert.That(operation.TryGetProperty("x-output-cache-policy", out _)).IsFalse();
            if (contract.Write)
            {
                await Assert.That(keys[0].GetProperty("required").GetBoolean()).IsTrue();
                await Assert.That(operation.GetProperty("x-rate-limit-policy").GetString()).IsEqualTo(RateLimitingExtensions.WritePolicy);
            }
        }
    }

    [Test]
    public async Task AuthoringAndAuditWireShapesExcludeDeliveryAndCallerAuthority()
    {
        await using var schema = GeneratedContractInputs.OpenSchema();
        using var document = await JsonDocument.ParseAsync(schema);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        (string Schema, string[] Fields)[] contracts =
        [
            ("CreateEventResourceRequestDto", ["resourceId", "draft"]),
            ("UpdateEventResourceRequestDto", ["expectedVersion", "draft"]),
            ("EventResourceVersionRequestDto", ["expectedVersion"]),
            ("EventResourceManagementDto", ["id", "eventId", "version", "publicationState", "draft", "createdAt", "updatedAt"]),
            ("EventResourceAuditDto", ["id", "action", "outcome", "reason", "timestamp", "responsibleManagerUserId"]),
            ("EventResourceDraftDto", ["title", "publicTitle", "description", "sensitiveNotes", "kind", "disclosureMode",
                "deliveryType", "eventSessionId", "languageCode", "accessibilityNote", "sortOrder",
                "accessibleAlternativeEventResourceId", "availability", "audienceRules"])
        ];
        foreach (var contract in contracts)
        {
            var fields = schemas.GetProperty(contract.Schema).GetProperty("properties").EnumerateObject()
                .Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
            await Assert.That(fields.SetEquals(contract.Fields)).IsTrue().Because(contract.Schema);
        }
    }
}
