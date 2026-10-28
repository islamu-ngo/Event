using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Api.IntegrationTests.Features;

[ClassDataSource<ContractApiFixture>(Shared = SharedType.PerAssembly)]
public sealed class ModerationPolicyAdministrationOpenApiTests(ContractApiFixture fixture)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CatalogueHalWrapperRetainsCompleteReferenceAndRecipeFields(bool swagger)
    {
        using var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync(swagger ? "/swagger/v0.1/swagger.json" : "/openapi/islamu-event.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var schemas = root.GetProperty("components").GetProperty("schemas");
        var content = root.GetProperty("paths").GetProperty("/api/moderation/reference-catalogue")
            .GetProperty("get").GetProperty("responses").GetProperty("200").GetProperty("content");
        var wrapper = Resolve(schemas, content.EnumerateObject().First(
            item => item.Value.TryGetProperty("schema", out _)).Value.GetProperty("schema"));
        var properties = wrapper.GetProperty("properties");
        await Assert.That(properties.TryGetProperty("_links", out _)).IsTrue();
        await Assert.That(properties.TryGetProperty("revision", out _)).IsTrue();
        var profile = Resolve(schemas, properties.GetProperty("profiles").GetProperty("items")).GetProperty("properties");
        var recipe = Resolve(schemas, profile.GetProperty("consequencePlans").GetProperty("items")).GetProperty("properties");
        await Assert.That(recipe.EnumerateObject().Select(property => property.Name)).IsEquivalentTo(
            ["content", "management", "resources", "newAdmission", "existingAdmission", "privateDisclosure",
                "settlement", "capacity", "credentials", "notifications", "recognition", "pendingWork",
                "restoration", "safeguards"]);
        await Assert.That(profile.TryGetProperty("prerequisites", out _)).IsTrue();
        await Assert.That(profile.TryGetProperty("recognitionPrerequisites", out _)).IsTrue();
        var scopeKinds = Resolve(schemas, profile.GetProperty("scopeKinds").GetProperty("items"));
        await Assert.That(scopeKinds.GetProperty("type").GetString()).IsEqualTo("string");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReferenceGradesAndRecipeModesUseActualNamedWireValues(bool swagger)
    {
        using var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync(swagger ? "/swagger/v0.1/swagger.json" : "/openapi/islamu-event.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (var type in new[]
        {
            typeof(ModerationReferenceProfileId), typeof(ModerationReferenceGrade),
            typeof(ModerationReferenceExecution), typeof(ModerationContentMode),
            typeof(ModerationRestorationMode)
        })
        {
            var schema = schemas.GetProperty(swagger ? type.FullName! : type.Name);
            await Assert.That(schema.GetProperty("type").GetString()).IsEqualTo("string");
            await Assert.That(schema.GetProperty("enum").EnumerateArray().Select(item =>
                item.GetString() ?? throw new InvalidOperationException("Enum names cannot be null.")))
                .IsEquivalentTo(Enum.GetNames(type));
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InvestigatorResponsibilityAndSeparateCustodyUseActualNamedWireValues(bool swagger)
    {
        using var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync(swagger ? "/swagger/v0.1/swagger.json" : "/openapi/islamu-event.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (var type in new[]
        {
            typeof(ModerationResponsibilityBasis), typeof(ModerationCaseTargetKind),
            typeof(ModerationCaseNarrativePurpose), typeof(ResourceSafetyReviewStep),
            typeof(ResourceSafetyAttestation), typeof(ModerationRecognitionDispositionGrounds),
            typeof(ModerationRecognitionPendingState), typeof(ModerationRecognitionPendingFailure),
            typeof(ModerationRecognitionEscalationStage)
        })
        {
            var schema = schemas.GetProperty(swagger ? type.FullName! : type.Name);
            await Assert.That(schema.GetProperty("type").GetString()).IsEqualTo("string");
            await Assert.That(schema.GetProperty("enum").EnumerateArray().Select(value =>
                value.GetString() ?? throw new InvalidOperationException("Enum names cannot be null.")))
                .IsEquivalentTo(Enum.GetNames(type));
        }
    }

    private static JsonElement Resolve(JsonElement schemas, JsonElement schema)
    {
        if (schema.TryGetProperty("oneOf", out var variants))
            schema = variants.EnumerateArray().Single(item => item.TryGetProperty("$ref", out _));
        if (schema.TryGetProperty("allOf", out var combined))
            schema = combined.EnumerateArray().Single(item => item.TryGetProperty("$ref", out _));
        while (schema.TryGetProperty("$ref", out var reference))
            schema = schemas.GetProperty(reference.GetString()!["#/components/schemas/".Length..]);
        return schema;
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CurrentAuthoringWireKeepsRetainedMeaningAndCurrentCoordinatesButNoNativeGrant(bool swagger)
    {
        using var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync(swagger ? "/swagger/v0.1/swagger.json" : "/openapi/islamu-event.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var schemas = root.GetProperty("components").GetProperty("schemas");
        var content = root.GetProperty("paths").GetProperty("/api/moderation/policies/current")
            .GetProperty("get").GetProperty("responses").GetProperty("200").GetProperty("content");
        var wrapper = Resolve(schemas, content.EnumerateObject().First(
            item => item.Value.TryGetProperty("schema", out _)).Value.GetProperty("schema"));
        var properties = wrapper.GetProperty("properties");
        await Assert.That(properties.TryGetProperty("_links", out _)).IsTrue();
        await Assert.That(properties.TryGetProperty("expectedQualificationRevision", out _)).IsTrue();
        await Assert.That(properties.TryGetProperty("expectedRevision", out _)).IsTrue();
        await Assert.That(properties.TryGetProperty("authoringBounds", out _)).IsTrue();
        await Assert.That(properties.TryGetProperty("nativeAuthoringQualified", out _)).IsFalse();
        var authoringSchema = properties.GetProperty("authoring");
        bool nullableAuthoring = authoringSchema.TryGetProperty("oneOf", out var authoringVariants)
            && authoringVariants.EnumerateArray().Any(item =>
                item.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "null");
        // OpenAPI 3.0 Swagger describes the same nullable reference with its nullable keyword.
        nullableAuthoring |= authoringSchema.TryGetProperty("nullable", out var nullable) && nullable.GetBoolean();
        await Assert.That(nullableAuthoring).IsTrue();
        var jurisdiction = Resolve(schemas, properties.GetProperty("jurisdiction")).GetProperty("properties");
        await Assert.That(jurisdiction.EnumerateObject().Select(property => property.Name))
            .IsEquivalentTo(["kind", "id", "parent"]);
        var bounds = Resolve(schemas, properties.GetProperty("authoringBounds")).GetProperty("properties");
        await Assert.That(Resolve(schemas, bounds.GetProperty("jurisdiction")).GetProperty("properties")
            .TryGetProperty("parent", out _)).IsTrue();
        var authoring = Resolve(schemas, properties.GetProperty("authoring")).GetProperty("properties");
        await Assert.That(authoring.TryGetProperty("historicalExpectedRevision", out _)).IsTrue();
        await Assert.That(authoring.TryGetProperty("inventoryRevision", out _)).IsTrue();
        await Assert.That(authoring.TryGetProperty("selectedProfiles", out _)).IsTrue();
        await Assert.That(authoring.TryGetProperty("consumers", out _)).IsFalse();
        var selectedRule = Resolve(schemas, authoring.GetProperty("selectedProfiles").GetProperty("items"))
            .GetProperty("properties");
        await Assert.That(selectedRule.EnumerateObject().Select(property => property.Name))
            .IsEquivalentTo(["ownerId", "code", "revision"]);
        var profile = Resolve(schemas, authoring.GetProperty("profiles").GetProperty("items")).GetProperty("properties");
        await Assert.That(profile.TryGetProperty("definition", out _)).IsTrue();
        var recipe = Resolve(schemas, profile.GetProperty("consequences").GetProperty("items")).GetProperty("properties");
        await Assert.That(recipe.EnumerateObject().Select(property => property.Name)).IsEquivalentTo(
            ["content", "management", "resources", "newAdmission", "existingAdmission", "privateDisclosure",
                "settlement", "capacity", "credentials", "notifications", "recognition", "pendingWork",
                "restoration", "safeguards"]);
    }
}
