using System.Text.Json;

namespace Event.Architecture.Tests;

public sealed class PromotionContractPrivacyTests
{
    private static readonly string[] ManagementPaths =
    [
        "/api/events/{eventId}/promotions",
        "/api/events/{eventId}/promotions/{promotionDefinitionId}",
        "/api/events/{eventId}/promotions/{promotionDefinitionId}/publish",
        "/api/events/{eventId}/promotions/{promotionDefinitionId}/revoke",
        "/api/events/{eventId}/promotions/{promotionDefinitionId}/code:rotate"
    ];

    private static readonly string[] ApplyRemovePaths =
    [
        "/api/events/{eventId}/registration-orders/{orderId}/promotion",
        "/api/events/{eventId}/registration-orders/guest/{orderId}/promotion"
    ];

    private static readonly (string Path, string Method, string SuccessStatus, string ExpectedSchema)[] PromotionCommandResponses =
    [
        ("/api/events/{eventId}/promotions", "post", "201", "PromotionCodeIssuedCommandResponseDto"),
        ("/api/events/{eventId}/promotions/{promotionDefinitionId}", "put", "200", "PromotionManagementCommandResponseDto"),
        ("/api/events/{eventId}/promotions/{promotionDefinitionId}/publish", "post", "200", "PromotionManagementCommandResponseDto"),
        ("/api/events/{eventId}/promotions/{promotionDefinitionId}/revoke", "post", "200", "PromotionManagementCommandResponseDto"),
        ("/api/events/{eventId}/promotions/{promotionDefinitionId}/code:rotate", "post", "200", "PromotionCodeIssuedCommandResponseDto")
    ];

    private static readonly (string Path, string Method)[] PromotionWriteOperations =
    [
        ("/api/events/{eventId}/promotions", "post"),
        ("/api/events/{eventId}/promotions/{promotionDefinitionId}", "put"),
        ("/api/events/{eventId}/promotions/{promotionDefinitionId}/publish", "post"),
        ("/api/events/{eventId}/promotions/{promotionDefinitionId}/revoke", "post"),
        ("/api/events/{eventId}/promotions/{promotionDefinitionId}/code:rotate", "post"),
        ("/api/events/{eventId}/registration-orders/{orderId}/promotion", "post"),
        ("/api/events/{eventId}/registration-orders/{orderId}/promotion", "delete"),
        ("/api/events/{eventId}/registration-orders/guest/{orderId}/promotion", "post"),
        ("/api/events/{eventId}/registration-orders/guest/{orderId}/promotion", "delete")
    ];

    private static readonly string[] ForbiddenPromotionProperties =
    [
        "codeDigest",
        "digest",
        "lookupDigest",
        "keyVersion",
        "lookupKeyVersion",
        "secretBindingId",
        "reservationId",
        "promotionReservationId",
        "tenantId",
        "actorId",
        "organizerActorId",
        "organizerUserId",
        "capability",
        "canApplyPromotion",
        "canRemovePromotion"
    ];

    [Test]
    public async Task PromotionOpenApi_ExposesManagementAndApplyRemoveRoutesWithCapabilityHeaderOnly()
    {
        using JsonDocument document = await ReadOpenApiAsync();
        JsonElement paths = document.RootElement.GetProperty("paths");

        foreach (var path in ManagementPaths)
        {
            await Assert.That(paths.TryGetProperty(path, out _)).IsTrue();
        }

        foreach (var path in ApplyRemovePaths)
        {
            JsonElement operations = paths.GetProperty(path);
            await Assert.That(operations.TryGetProperty("post", out JsonElement apply)).IsTrue();
            await Assert.That(operations.TryGetProperty("delete", out JsonElement remove)).IsTrue();
            await AssertOperationHasExpectedHeaders(path, apply);
            await AssertOperationHasExpectedHeaders(path, remove);
        }

        foreach ((string path, string method) in PromotionWriteOperations)
        {
            string[] headers = GetBusinessHeaders(paths.GetProperty(path).GetProperty(method));
            await Assert.That(headers).Contains("Idempotency-Key")
                .Because($"{method.ToUpperInvariant()} {path} requires idempotency middleware transport.");
        }
    }

    [Test]
    public async Task PromotionSchemas_ExposeOnlySafeLabelsTotalsAndOperationScopedOneTimeCodeField()
    {
        using JsonDocument document = await ReadOpenApiAsync();
        JsonElement paths = document.RootElement.GetProperty("paths");
        JsonElement schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        JsonElement promotion = schemas.GetProperty("PromotionManagementDto").GetProperty("properties");
        JsonElement redemption = schemas.GetProperty("PromotionRedemptionResponseDto").GetProperty("properties");
        JsonElement safeCommand = schemas.GetProperty("PromotionManagementCommandResponseDto").GetProperty("properties");
        JsonElement issuedCodeCommand = schemas.GetProperty("PromotionCodeIssuedCommandResponseDto").GetProperty("properties");

        await Assert.That(promotion.TryGetProperty("displayLabel", out _)).IsTrue();
        await Assert.That(promotion.TryGetProperty("promotionCodeDisplayLabel", out _)).IsTrue();
        await Assert.That(redemption.TryGetProperty("appliedPromotionDisplayLabel", out _)).IsTrue();
        await Assert.That(redemption.TryGetProperty("promotionDiscountTotalMinor", out _)).IsTrue();
        await Assert.That(redemption.TryGetProperty("platformFeeTotalMinor", out _)).IsTrue();
        await Assert.That(redemption.TryGetProperty("platformContributionTotalMinor", out _)).IsTrue();
        await Assert.That(redemption.TryGetProperty("totalDueMinor", out _)).IsTrue();
        await Assert.That(issuedCodeCommand.TryGetProperty("issuedCode", out _)).IsTrue();
        await Assert.That(safeCommand.TryGetProperty("issuedCode", out _)).IsFalse();

        foreach (var response in PromotionCommandResponses)
        {
            var responseRef = GetOperationSuccessResponseRef(paths, response.Path, response.Method, response.SuccessStatus);
            await Assert.That(responseRef).IsEqualTo($"#/components/schemas/{response.ExpectedSchema}");
        }

        await AssertNoForbiddenPromotionProperties("PromotionManagementDto", promotion);
        await AssertNoForbiddenPromotionProperties("PromotionRedemptionResponseDto", redemption);
    }

    [Test]
    public async Task QuotaExceededDetails_OpenApiAndGeneratedClientDoNotExposeTenantId()
    {
        using JsonDocument document = await ReadOpenApiAsync();
        JsonElement schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        JsonElement quotaExceeded = schemas.GetProperty("QuotaExceededDetails").GetProperty("properties");
        JsonElement safeCommand = schemas.GetProperty("PromotionManagementCommandResponseDto").GetProperty("properties");
        JsonElement issuedCodeCommand = schemas.GetProperty("PromotionCodeIssuedCommandResponseDto").GetProperty("properties");

        await Assert.That(quotaExceeded.TryGetProperty("tenantId", out _)).IsFalse();
        await AssertNullableQuotaExceededReference(safeCommand.GetProperty("quotaExceeded"));
        await AssertNullableQuotaExceededReference(issuedCodeCommand.GetProperty("quotaExceeded"));

        var generated = await File.ReadAllTextAsync(Path.Combine(
            ResolveRepositoryRoot(),
            "src",
            "Explore.Blazor.Client",
            "Clients",
            "EventApiTagClients.g.cs"));

        await Assert.That(ExtractGeneratedType(generated, "QuotaExceededDetails")).DoesNotContain("TenantId");
        await Assert.That(ExtractGeneratedType(generated, "PromotionManagementCommandResponseDto"))
            .Contains("public QuotaExceededDetails? QuotaExceeded");
        await Assert.That(ExtractGeneratedType(generated, "PromotionManagementCommandResponseDto")).DoesNotContain("TenantId");
        await Assert.That(ExtractGeneratedType(generated, "PromotionCodeIssuedCommandResponseDto"))
            .Contains("public QuotaExceededDetails? QuotaExceeded");
        await Assert.That(ExtractGeneratedType(generated, "PromotionCodeIssuedCommandResponseDto")).DoesNotContain("TenantId");
    }

    [Test]
    public async Task GeneratedClient_ExposesPromotionMethodsWithoutSecretOrCapabilityStorageProperties()
    {
        var generated = await File.ReadAllTextAsync(Path.Combine(
            ResolveRepositoryRoot(),
            "src",
            "Explore.Blazor.Client",
            "Clients",
            "EventApiTagClients.g.cs"));

        foreach (var method in new[]
                 {
                     "GetEventPromotionsAsync",
                     "GetEventPromotionAsync",
                     "CreateEventPromotionDraftAsync",
                     "ReviseEventPromotionAsync",
                     "PublishEventPromotionAsync",
                     "RevokeEventPromotionAsync",
                     "RotateEventPromotionCodeAsync",
                     "ApplyAuthenticatedRegistrationOrderPromotionAsync",
                     "RemoveAuthenticatedRegistrationOrderPromotionAsync",
                     "ApplyGuestRegistrationOrderPromotionAsync",
                     "RemoveGuestRegistrationOrderPromotionAsync"
                 })
        {
            await Assert.That(generated).Contains(method);
        }

        foreach (var method in new[]
                 {
                     "CreateEventPromotionDraftAsync",
                     "ReviseEventPromotionAsync",
                     "PublishEventPromotionAsync",
                     "RevokeEventPromotionAsync",
                     "RotateEventPromotionCodeAsync",
                     "ApplyAuthenticatedRegistrationOrderPromotionAsync",
                     "RemoveAuthenticatedRegistrationOrderPromotionAsync",
                     "ApplyGuestRegistrationOrderPromotionAsync",
                     "RemoveGuestRegistrationOrderPromotionAsync"
                 })
        {
            await Assert.That(ExtractGeneratedMethodSignature(generated, method)).Contains("idempotency_Key");
        }

        foreach (var typeName in new[]
                  {
                      "PromotionManagementDto",
                      "PromotionRedemptionResponseDto",
                      "PromotionManagementCommandResponseDto"
                  })
        {
            var generatedType = ExtractGeneratedType(generated, typeName);
            foreach (var forbidden in ForbiddenPromotionProperties)
            {
                await Assert.That(generatedType).DoesNotContain(ToPascalCase(forbidden));
            }
        }

        await Assert.That(ExtractGeneratedType(generated, "PromotionCodeIssuedCommandResponseDto")).Contains("IssuedCode");
        await Assert.That(ExtractGeneratedType(generated, "PromotionManagementCommandResponseDto")).DoesNotContain("IssuedCode");
    }

    private static string GetOperationSuccessResponseRef(JsonElement paths, string path, string method, string statusCode)
    {
        JsonElement content = paths
            .GetProperty(path)
            .GetProperty(method)
            .GetProperty("responses")
            .GetProperty(statusCode)
            .GetProperty("content");

        foreach (var mediaType in new[] { "application/json; v=0.1", "application/hal+json; v=0.1" })
        {
            if (content.TryGetProperty(mediaType, out JsonElement response) &&
                response.TryGetProperty("schema", out JsonElement schema) &&
                schema.TryGetProperty("$ref", out JsonElement reference))
            {
                return reference.GetString()!;
            }
        }

        return string.Empty;
    }

    private static async Task AssertNullableQuotaExceededReference(JsonElement schema)
    {
        JsonElement[] alternatives = schema.GetProperty("oneOf").EnumerateArray().ToArray();
        string[] references = alternatives
            .Select(option => option.TryGetProperty("$ref", out JsonElement reference) ? reference.GetString() : null)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Select(reference => reference!)
            .ToArray();
        int nullAlternatives = alternatives.Count(option =>
            option.TryGetProperty("type", out JsonElement type) && type.GetString() == "null");

        await Assert.That(references).IsEquivalentTo(["#/components/schemas/QuotaExceededDetails"]);
        await Assert.That(nullAlternatives).IsEqualTo(1);
    }

    private static async Task AssertOperationHasExpectedHeaders(string path, JsonElement operation)
    {
        string[] headers = GetBusinessHeaders(operation);

        if (path.Contains("/guest/", StringComparison.Ordinal))
        {
            await Assert.That(headers).IsEquivalentTo(["X-Registration-Order-Capability", "Idempotency-Key"]);
        }
        else
        {
            await Assert.That(headers).IsEquivalentTo(["Idempotency-Key"]);
        }
    }

    private static string[] GetBusinessHeaders(JsonElement operation) => operation.GetProperty("parameters")
        .EnumerateArray()
        .Where(parameter => parameter.GetProperty("in").GetString() == "header")
        .Select(parameter => parameter.GetProperty("name").GetString()!)
        .Where(name => name != "X-Api-Version")
        .ToArray();

    private static async Task AssertNoForbiddenPromotionProperties(string schemaName, JsonElement properties)
    {
        foreach (var property in ForbiddenPromotionProperties)
        {
            await Assert.That(properties.TryGetProperty(property, out _)).IsFalse()
                .Because($"{schemaName} must not expose {property}.");
        }
    }

    private static string ExtractGeneratedType(string generated, string typeName)
    {
        int typeStart = FindGeneratedTypeStart(generated, typeName);
        if (typeStart < 0)
        {
            return string.Empty;
        }

        var typeEnd = generated.IndexOf("\n    [System.CodeDom.Compiler.GeneratedCode", typeStart + 1, StringComparison.Ordinal);
        return typeEnd < 0 ? generated[typeStart..] : generated[typeStart..typeEnd];
    }

    private static int FindGeneratedTypeStart(string generated, string typeName)
    {
        int recordStart = generated.IndexOf(
            $"partial record class {typeName}",
            StringComparison.Ordinal);
        return recordStart >= 0
            ? recordStart
            : generated.IndexOf(
                $"partial class {typeName}",
                StringComparison.Ordinal);
    }

    private static string ExtractGeneratedMethodSignature(string generated, string methodName)
    {
        int methodStart = generated.IndexOf($" {methodName}(", StringComparison.Ordinal);
        if (methodStart < 0)
        {
            return string.Empty;
        }

        int methodEnd = generated.IndexOf(';', methodStart);
        return methodEnd < 0 ? generated[methodStart..] : generated[methodStart..(methodEnd + 1)];
    }

    private static async Task<JsonDocument> ReadOpenApiAsync()
    {
        FileStream stream = File.OpenRead(Path.Combine(
            ResolveRepositoryRoot(), "schemas", "openapi_islamu-event.json"));
        await using (stream)
        {
            return await JsonDocument.ParseAsync(stream);
        }
    }

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Explore.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root from architecture test output directory.");
    }

    private static string ToPascalCase(string value) => string.Concat(
        value.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
