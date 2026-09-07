using System.Text.Json;

namespace Event.Architecture.Tests;

public sealed class PaidEventPolicyOpenApiContractTests
{
    [Test]
    public async Task TenantPaidPolicyHalSchema_ExposesTypedAuthorityFacts()
    {
        string path = ContextSystemHelpers.RepoPath(
            "schemas",
            "openapi_islamu-event.json");
        using JsonDocument document = JsonDocument.Parse(
            await File.ReadAllTextAsync(path));
        JsonElement schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas");
        JsonElement authority = schemas.GetProperty("PaidEventPolicyAuthorityDto")
            .GetProperty("properties");
        JsonElement hal = schemas
            .GetProperty("HalResourceOfTenantPaidEventPolicyConfigurationDto")
            .GetProperty("properties");

        await Assert.That(authority.TryGetProperty(
            "instancePolicyVersion",
            out _)).IsTrue();
        await Assert.That(authority.TryGetProperty(
            "effectiveValuesInherited",
            out _)).IsTrue();
        await Assert.That(authority.TryGetProperty(
            "hasTenantNarrowing",
            out _)).IsTrue();
        await Assert.That(authority.GetProperty("manifestOwnedFields")
            .GetProperty("items")
            .GetProperty("type")
            .GetString()).IsEqualTo("string");
        await Assert.That(authority.GetProperty("sovereignLockedFields")
            .GetProperty("items")
            .GetProperty("type")
            .GetString()).IsEqualTo("string");
        await Assert.That(hal.GetProperty("authority")
            .GetProperty("$ref")
            .GetString()).IsEqualTo(
                "#/components/schemas/PaidEventPolicyAuthorityDto");
        await Assert.That(hal.TryGetProperty("_links", out _)).IsTrue();
    }
}
