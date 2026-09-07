namespace Event.Application.UnitTests.Features.ConfigurationManifest;

using System.Text.Json;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public sealed class ConfigurationManifestSerializationTests
{
    [Test]
    public async Task Serialize_UsesCanonicalEnvelopeAndFlatSettingKeys()
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["events.require_approval"] = ConfigurationManifestTestData.Json("true")
        };
        ConfigurationManifestV1Alpha2 manifest = ConfigurationManifestTestData.Valid(settings: settings);

        string json = JsonSerializer.Serialize(
            manifest,
            ConfigurationPortabilityJsonContext.Default.ConfigurationManifestV1Alpha2);

        await Assert.That(json.Contains("\"$schema\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(json.Contains("\"apiVersion\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(json.Contains("\"events.require_approval\"", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Deserialize_UnknownRootMember_ThrowsJsonException()
    {
        const string json =
            """
            {
              "$schema": "https://schemas.islamu.org/event/configuration-manifest/v1alpha2/schema.json",
              "apiVersion": "configuration.islamu.org/v1alpha2",
              "kind": "ConfigurationManifest",
              "metadata": { "name": "primary-deployment" },
              "spec": {
                "instance": {
                  "settings": {},
                  "documents": {},
                  "legalDocuments": {}
                },
                "tenants": []
              },
              "unexpected": true
            }
            """;
        JsonException? exception = null;

        try
        {
            _ = JsonSerializer.Deserialize(
                json,
                ConfigurationPortabilityJsonContext.Default.ConfigurationManifestV1Alpha2);
        }
        catch (JsonException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
    }

    [Test]
    public async Task RoundTrip_PreservesCanonicalManifestIdentity()
    {
        ConfigurationManifestV1Alpha2 expected = ConfigurationManifestTestData.Valid();
        string json = JsonSerializer.Serialize(
            expected,
            ConfigurationPortabilityJsonContext.Default.ConfigurationManifestV1Alpha2);

        ConfigurationManifestV1Alpha2? actual = JsonSerializer.Deserialize(
            json,
            ConfigurationPortabilityJsonContext.Default.ConfigurationManifestV1Alpha2);

        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.Schema).IsEqualTo(expected.Schema);
        await Assert.That(actual.ApiVersion).IsEqualTo(expected.ApiVersion);
        await Assert.That(actual.Kind).IsEqualTo(expected.Kind);
        await Assert.That(actual.Spec.Tenants[0].Metadata.Name).IsEqualTo("default");
    }
}
