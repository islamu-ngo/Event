namespace Event.Architecture.Tests;

using System.Text.Json;

public sealed class ConfigurationManifestSchemaArtifactTests
{
    [Test]
    [Arguments("configuration-manifest-v1alpha2.schema.json",
        "https://schemas.islamu.org/event/configuration-manifest/v1alpha2/schema.json",
        "ConfigurationManifest")]
    [Arguments("tenant-configuration-package-v1alpha2.schema.json",
        "https://schemas.islamu.org/event/tenant-configuration-package/v1alpha2/schema.json",
        "TenantConfigurationPackage")]
    public async Task PublishedSchema_RequiresCurrentContractIdentity(
        string artifact, string schemaId, string kind)
    {
        using JsonDocument schema = JsonDocument.Parse(await File.ReadAllBytesAsync(
            ContextSystemHelpers.RepoPath("schemas", artifact)));
        JsonElement root = schema.RootElement;
        JsonElement properties = root.GetProperty("properties");

        await Assert.That(root.GetProperty("$schema").GetString())
            .IsEqualTo("https://json-schema.org/draft/2020-12/schema");
        await Assert.That(root.GetProperty("$id").GetString()).IsEqualTo(schemaId);
        await Assert.That(root.GetProperty("type").GetString()).IsEqualTo("object");
        await Assert.That(root.GetProperty("additionalProperties").GetBoolean()).IsFalse();
        await Assert.That(root.GetProperty("required").EnumerateArray()
            .Select(value => value.GetString()!).ToArray())
            .IsEquivalentTo(["$schema", "apiVersion", "kind", "metadata", "spec"]);
        await Assert.That(properties.GetProperty("$schema").GetProperty("const").GetString())
            .IsEqualTo(schemaId);
        await Assert.That(properties.GetProperty("apiVersion").GetProperty("const").GetString())
            .IsEqualTo("configuration.islamu.org/v1alpha2");
        await Assert.That(properties.GetProperty("kind").GetProperty("const").GetString())
            .IsEqualTo(kind);
    }
}
