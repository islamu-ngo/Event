using System.Reflection;
using System.Text.Json;
using Explore.Application.Features.AiAssistant.Actions;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Domain.Ai;

using TUnit.Assertions;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.AiAssistant.Actions;

public sealed class CreateEventDraftLocationWriteContractTests
{
    [Test]
    public async Task AiDraftLocationPayloadDoesNotExposeRawCoordinateMembers()
    {
        string[] forbiddenMembers = typeof(CreateEventDraftLocationPayload)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.Name is "Latitude" or "Longitude")
            .Select(property => $"{nameof(CreateEventDraftLocationPayload)}.{property.Name}")
            .ToArray();

        await Assert.That(forbiddenMembers).IsEmpty();
    }

    [Test]
    public async Task ToolSchemaNestedLocationPropertiesExcludeRawCoordinates()
    {
        using var schema = JsonDocument.Parse(CreateEventDraftAiToolDefinition.JsonSchema);
        string[] forbiddenSchemaProperties = schema.RootElement
            .GetProperty("properties")
            .GetProperty("location")
            .GetProperty("properties")
            .EnumerateObject()
            .Where(property => property.Name is "latitude" or "longitude")
            .Select(property => $"location.{property.Name}")
            .ToArray();

        await Assert.That(forbiddenSchemaProperties).IsEmpty();
    }

    [Test]
    public async Task RegistryValidationDirectAndAliasCoordinatesAreRejectedOrStrippedBeforeMapping()
    {
        var registry = AiToolContractRegistry.CreateDefault();
        (string Shape, string SafePayloadJson, string CoordinatePayloadJson)[] payloadShapes =
        [
            (
                "direct",
                DirectPayload(includeCoordinates: false),
                DirectPayload(includeCoordinates: true)),
            (
                "alias-normalized",
                AliasPayload(includeCoordinates: false),
                AliasPayload(includeCoordinates: true))
        ];
        var violations = new List<string>();

        foreach (var payloadShape in payloadShapes)
        {
            AiToolValidationResult safeValidation = registry.ValidatePayload(
                AiProposedActionKind.CreateEventDraft,
                payloadShape.SafePayloadJson,
                allowProviderNormalization: true);
            if (!safeValidation.Succeeded)
            {
                violations.Add($"{payloadShape.Shape} coordinate-free control failed validation ({safeValidation.FailureCode})");
                continue;
            }

            string safeNormalizedJson = safeValidation.NormalizedPayloadJson ?? payloadShape.SafePayloadJson;
            AssertSafeManualLocation(payloadShape.Shape, safeNormalizedJson, violations);

            AiToolValidationResult coordinateValidation = registry.ValidatePayload(
                AiProposedActionKind.CreateEventDraft,
                payloadShape.CoordinatePayloadJson,
                allowProviderNormalization: true);
            if (!coordinateValidation.Succeeded)
            {
                if (coordinateValidation.FailureCode != "unsupported_tool_argument")
                {
                    violations.Add($"{payloadShape.Shape} coordinates caused unrelated rejection ({coordinateValidation.FailureCode})");
                }
                continue;
            }

            string coordinateNormalizedJson = coordinateValidation.NormalizedPayloadJson
                ?? payloadShape.CoordinatePayloadJson;
            using var coordinateNormalized = JsonDocument.Parse(coordinateNormalizedJson);
            if (ContainsCoordinateProperty(coordinateNormalized.RootElement))
            {
                violations.Add($"{payloadShape.Shape} validation retained latitude/longitude");
                continue;
            }

            AssertSafeManualLocation(payloadShape.Shape, coordinateNormalizedJson, violations);
        }

        await Assert.That(violations).IsEmpty();
    }

    private static void AssertSafeManualLocation(string shape, string payloadJson, List<string> violations)
    {
        using var normalized = JsonDocument.Parse(payloadJson);
        if (!normalized.RootElement.TryGetProperty("location", out var location)
            || !HasExactStringProperty(location, "fullName", "Islamic Centre Brussels")
            || !HasExactStringProperty(location, "address", "Rue Example 10")
            || !HasExactStringProperty(location, "postcode", "1000")
            || !HasExactStringProperty(location, "country", "Belgium")
            || !HasExactStringProperty(location, "city", "Brussels"))
        {
            violations.Add($"{shape} normalization did not preserve the exact safe manual Location fields");
            return;
        }

        var mapping = new CreateEventDraftAiActionMapper().Map(payloadJson);
        if (!mapping.Succeeded || mapping.Draft?.Locations.Count != 1)
        {
            violations.Add($"{shape} safe manual Location fields did not map into the draft");
            return;
        }

        var mapped = mapping.Draft.Locations.Single();
        if (mapped.FullName != "Islamic Centre Brussels"
            || mapped.Address != "Rue Example 10"
            || mapped.Postcode != "1000"
            || mapped.Country != "Belgium"
            || mapped.City != "Brussels")
        {
            violations.Add($"{shape} mapping did not preserve the exact safe manual Location fields");
        }
        foreach (string coordinateName in new[] { "Latitude", "Longitude" })
        {
            PropertyInfo? coordinate = mapped.GetType().GetProperty(coordinateName);
            if (coordinate?.GetValue(mapped) is not null)
            {
                violations.Add($"{shape} mapping manufactured trusted {coordinateName} write data");
            }
        }
    }

    private static bool HasExactStringProperty(JsonElement element, string propertyName, string expected)
        => element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && property.GetString() == expected;

    private static string DirectPayload(bool includeCoordinates) => $$"""
        {
          "title": "Direct payload",
          "participationConfiguration": {
            "participationHandlingModeId": 1,
            "advanceRegistrationObligationId": 1
          },
          "location": {
            "fullName": "Islamic Centre Brussels",
            "address": "Rue Example 10",
            "postcode": "1000",
            "country": "Belgium",
            "city": "Brussels"{{(includeCoordinates ? ",\n    \"latitude\": 50.8503,\n    \"longitude\": 4.3517" : string.Empty)}}
          }
        }
        """;

    private static string AliasPayload(bool includeCoordinates) => $$"""
        {
          "eventName": "Alias-normalized payload",
          "participationConfiguration": {
            "participationHandlingModeId": 1,
            "advanceRegistrationObligationId": 1
          },
          "venueName": "Islamic Centre Brussels",
          "streetAddress": "Rue Example 10",
          "postalCode": "1000",
          "country": "Belgium",
          "city": "Brussels"{{(includeCoordinates ? ",\n  \"latitude\": 50.8503,\n  \"longitude\": 4.3517" : string.Empty)}}
        }
        """;

    private static bool ContainsCoordinateProperty(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Name is "latitude" or "longitude" || ContainsCoordinateProperty(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(ContainsCoordinateProperty);
        }

        return false;
    }
}
