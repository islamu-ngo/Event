namespace Event.Architecture.Tests;

using System.Text.Json;

public sealed class OperatorIdentityManifestSchemaTests
{
    [Test]
    public async Task PublishedManifestSchemaRequiresTheClosedWireEnvelopeAndDocument()
    {
        await using Stream input = GeneratedContractInputs.OpenSchema();
        using JsonDocument document = await JsonDocument.ParseAsync(input);
        JsonElement schemas = document.RootElement.GetProperty("components")
            .GetProperty("schemas");
        JsonElement manifest = schemas.GetProperty("OperatorIdentityManifest");

        await Assert.That(manifest.GetProperty("type").GetString()).IsEqualTo("object");
        await Assert.That(manifest.GetProperty("additionalProperties").GetBoolean()).IsFalse();
        await Assert.That(manifest.GetProperty("required").EnumerateArray()
            .Select(item => item.GetString()!).ToArray())
            .IsEquivalentTo(new[]
            {
                "apiVersion", "kind", "settingKey", "contentDigest",
                "revisionHash", "document"
            });

        JsonElement properties = manifest.GetProperty("properties");
        await Assert.That(properties.GetProperty("apiVersion").GetProperty("enum")[0].GetString())
            .IsEqualTo("islamu.org/operator-identity/v1");
        await Assert.That(properties.GetProperty("kind").GetProperty("enum")[0].GetString())
            .IsEqualTo("InstanceOperatorIdentity");
        await Assert.That(properties.GetProperty("settingKey").GetProperty("enum")[0].GetString())
            .IsEqualTo("instance.operator_identity");
        await Assert.That(properties.GetProperty("contentDigest").GetProperty("pattern").GetString())
            .IsEqualTo("^[0-9a-f]{64}$");
        await Assert.That(properties.GetProperty("revisionHash").GetProperty("pattern").GetString())
            .IsEqualTo("^[0-9a-f]{64}$");

        JsonElement payload = properties.GetProperty("document");
        await Assert.That(payload.GetProperty("additionalProperties").GetBoolean()).IsFalse();
        await Assert.That(payload.GetProperty("required").GetArrayLength())
            .IsEqualTo(14);
        await Assert.That(payload.GetProperty("properties").GetProperty("isOfficialInstance")
            .GetProperty("type").GetString()).IsEqualTo("boolean");
        foreach (string id in new[] { "operatorId", "revision" })
        {
            await Assert.That(payload.GetProperty("properties").GetProperty(id)
                .GetProperty("format").GetString()).IsEqualTo("uuid");
        }
        await Assert.That(schemas.GetProperty("ImportInstanceOperatorIdentityCommand")
            .GetProperty("properties").GetProperty("expectedRevisionHash")
            .GetProperty("pattern").GetString()).IsEqualTo("^[0-9a-f]{64}$");
    }
}
