namespace Event.Application.UnitTests.Features.ConfigurationManifest;

using System.Text.Json;
using Explore.Application.Features.ConfigurationManifest.Validation;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Domain.ValueObjects;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public sealed class ConfigurationManifestResourceArrayTests
{
    [Test]
    public async Task ResourceDefaults_AreValidForBothScopesAndNativePolicy()
    {
        var values = EventResourceSettingDefinitions.All.ToDictionary(
            definition => definition.Key, definition => definition.DefaultValue);
        var instance = values.ToDictionary(pair => pair.Key,
            pair => ConfigurationManifestTestData.Json(pair.Value));
        var tenant = EventResourceSettingDefinitions.All
            .Where(definition => definition.MaxScope >= SettingScope.Tenant)
            .ToDictionary(definition => definition.Key,
                definition => ConfigurationManifestTestData.Json(definition.DefaultValue));

        await Assert.That(ConfigurationManifestValidator.Validate(
            ConfigurationManifestTestData.Valid(settings: tenant, instanceSettings: instance)).IsValid).IsTrue();
        await Assert.That(EventResourceGovernancePolicyValues.Parse(values, long.MaxValue))
            .IsEqualTo(EventResourceGovernancePolicy.Default(long.MaxValue));
    }

    [Test]
    [MethodDataSource(nameof(ResourceArrayCases))]
    public async Task TypedArrays_ValidateInManifestAndTenantPackage(string key, string json, bool accepted)
    {
        var settings = new Dictionary<string, JsonElement>
        {
            [key] = ConfigurationManifestTestData.Json(json)
        };
        var manifest = ConfigurationManifestTestData.Valid(settings: settings, instanceSettings: settings);
        var result = ConfigurationManifestValidator.Validate(manifest);
        await Assert.That(result.IsValid).IsEqualTo(accepted);
        if (!accepted)
        {
            await Assert.That(result.Errors.Count).IsEqualTo(2);
            await Assert.That(result.Errors.All(error =>
                error.Code == ConfigurationManifestFailureCodes.ValueInvalid)).IsTrue();
        }

        var package = new TenantConfigurationPackageV1Alpha2
        {
            Schema = TenantConfigurationPackageContractMetadata.SchemaId,
            ApiVersion = TenantConfigurationPackageContractMetadata.ApiVersion,
            Kind = TenantConfigurationPackageContractMetadata.Kind,
            Metadata = new TenantConfigurationPackageMetadataV1Alpha2
            {
                Name = "resource-policy",
                Source = new TenantConfigurationPackageSourceV1Alpha2 { TenantName = "default" }
            },
            Spec = new TenantConfigurationPackageSpecV1Alpha2
            {
                DisplayName = "Resource policy",
                Settings = settings,
                Documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>()
            }
        };
        await Assert.That(ConfigurationManifestValidator.Validate(package).IsValid).IsEqualTo(accepted);
    }

    public static IEnumerable<(string Key, string Json, bool Accepted)> ResourceArrayCases()
    {
        foreach (var definition in EventResourceSettingDefinitions.All.Where(value => value.ValueType == SettingValueType.Json))
        {
            yield return (definition.Key, definition.DefaultValue, true);
            yield return (definition.Key, "[]", true);
            foreach (string invalid in new[] { "null", "{}", "true", "42", "\"[]\"", "[null]", "[1]", "[{}]", "[[]]", "[true]" })
                yield return (definition.Key, invalid, false);
        }
        foreach (var (key, json) in InvalidEnumArrays())
            yield return (key, json, false);
        yield return (EventResourceSettingDefinitions.EnabledDeliveryTypes.Key, "[\"StoredFile\",\"StoredFile\"]", true);
        yield return (EventResourceSettingDefinitions.EnabledAudiences.Key, "[\"Public\",\"Public\"]", true);
        yield return (EventResourceSettingDefinitions.PermittedFileTypes.Key, "[\"application/pdf\",\"application/pdf\"]", true);
        yield return (EventResourceSettingDefinitions.PermittedFileTypes.Key, "[\"text/html\"]", false);
        yield return (EventResourceSettingDefinitions.PermittedFileTypes.Key, "[\"Application/Pdf\"]", false);
        yield return (EventResourceSettingDefinitions.ExternalOrigins.Key, "[\"https://resources.example.org\",\"https://resources.example.org\"]", true);
        foreach (string origin in new[] { "", "http://example.org", "https://localhost", "https://127.0.0.1", "https://example.org/path", "https://example.org/", "https://example.org?token=private", "https://example.org#fragment", "https://user@example.org", "https://*.example.org" })
            yield return (EventResourceSettingDefinitions.ExternalOrigins.Key, JsonSerializer.Serialize(new[] { origin }), false);
    }

    [Test]
    [MethodDataSource(nameof(InvalidEnumArrays))]
    public async Task NativeParser_RejectsNonExactEnumTokens(string key, string json)
    {
        var values = new Dictionary<string, string> { [key] = json };
        await Assert.That(() => EventResourceGovernancePolicyValues.Parse(values, long.MaxValue))
            .Throws<JsonException>();
    }

    public static IEnumerable<(string Key, string Json)> InvalidEnumArrays()
    {
        foreach (var (definition, name, other) in new[]
        {
            (EventResourceSettingDefinitions.EnabledDeliveryTypes, "StoredFile", "ExternalLink"),
            (EventResourceSettingDefinitions.EnabledAudiences, "Public", "Organizer")
        })
        {
            foreach (string token in new[] { name.ToLowerInvariant(), $" {name}", $"{name} ", $"{name}, {other}", $"{name},{name}", "1", "0", "999", "Unknown", "" })
                yield return (definition.Key, JsonSerializer.Serialize(new[] { token }));
            yield return (definition.Key, "[1]");
            yield return (definition.Key, "[null]");
        }
    }

    [Test]
    public async Task NativeParser_PreservesEmptyArraysAndCollapsesDuplicatesToSets()
    {
        var empty = EventResourceSettingDefinitions.All
            .Where(definition => definition.ValueType == SettingValueType.Json)
            .ToDictionary(definition => definition.Key, _ => "[]");
        var emptyPolicy = EventResourceGovernancePolicyValues.Parse(empty, long.MaxValue);
        await Assert.That(emptyPolicy.EnabledDeliveryTypes).IsEmpty();
        await Assert.That(emptyPolicy.EnabledAudiences).IsEmpty();
        await Assert.That(emptyPolicy.PermittedFileTypes).IsEmpty();
        await Assert.That(emptyPolicy.ExternalOrigins).IsEmpty();

        var duplicated = new Dictionary<string, string>
        {
            [EventResourceSettingDefinitions.EnabledDeliveryTypes.Key] = "[\"StoredFile\",\"StoredFile\"]",
            [EventResourceSettingDefinitions.EnabledAudiences.Key] = "[\"Public\",\"Public\"]",
            [EventResourceSettingDefinitions.PermittedFileTypes.Key] = "[\"application/pdf\",\"application/pdf\"]",
            [EventResourceSettingDefinitions.ExternalOrigins.Key] = "[\"https://resources.example.org\",\"https://resources.example.org\"]"
        };
        var policy = EventResourceGovernancePolicyValues.Parse(duplicated, long.MaxValue);
        await Assert.That(policy.EnabledDeliveryTypes.Count).IsEqualTo(1);
        await Assert.That(policy.EnabledAudiences.Count).IsEqualTo(1);
        await Assert.That(policy.PermittedFileTypes.Count).IsEqualTo(1);
        await Assert.That(policy.ExternalOrigins.Count).IsEqualTo(1);
    }
}
