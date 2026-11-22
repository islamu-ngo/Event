namespace Event.Architecture.Tests;

using System.Globalization;
using System.Text.Json;
using Explore.Application.DTOs.PaidEventPolicies;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using ISLAMU.ConfigurationManifest.SchemaGenerator;

public sealed class ConfigurationManifestSchemaGenerationTests
{
    [Test]
    public async Task GeneratedResourceArrays_AreTypedClosedItemSetsInBothArtifacts()
    {
        var expectedItems = new Dictionary<string, string[]?>
        {
            [EventResourceSettingDefinitions.EnabledDeliveryTypes.Key] = ["ExternalLink", "StoredFile"],
            [EventResourceSettingDefinitions.EnabledAudiences.Key] =
            [
                "AnyEventSessionSpeaker", "AuthenticatedTenantMember", "CheckedInParticipant",
                "EventStaff", "Organizer", "Public", "SessionRegistrant", "SessionSpeaker", "TicketHolder"
            ],
            [EventResourceSettingDefinitions.PermittedFileTypes.Key] =
            [
                "application/pdf",
                "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            ],
            [EventResourceSettingDefinitions.ExternalOrigins.Key] = null
        };
        using JsonDocument manifest = JsonDocument.Parse(
            ConfigurationManifestJsonSchemaGenerator.GenerateConfigurationManifest());
        using JsonDocument package = JsonDocument.Parse(
            ConfigurationManifestJsonSchemaGenerator.GenerateTenantConfigurationPackage());
        foreach (JsonElement settings in new[]
        {
            manifest.RootElement.GetProperty("$defs").GetProperty("instanceSettings"),
            manifest.RootElement.GetProperty("$defs").GetProperty("tenantSettings"),
            package.RootElement.GetProperty("$defs").GetProperty("tenantSettings")
        })
        {
            await Assert.That(settings.GetProperty("additionalProperties").GetBoolean()).IsFalse();
            foreach (var (key, allowed) in expectedItems)
            {
                JsonElement array = settings.GetProperty("properties").GetProperty(key);
                await Assert.That(array.GetProperty("type").GetString()).IsEqualTo("array");
                await Assert.That(array.TryGetProperty("minItems", out _)).IsFalse();
                await Assert.That(array.TryGetProperty("uniqueItems", out _)).IsFalse();
                JsonElement items = array.GetProperty("items");
                await Assert.That(items.GetProperty("type").GetString()).IsEqualTo("string");
                await Assert.That(items.GetProperty("minLength").GetInt32()).IsEqualTo(1);
                if (allowed is null)
                    await Assert.That(items.TryGetProperty("enum", out _)).IsFalse();
                else
                    await Assert.That(items.GetProperty("enum").EnumerateArray()
                        .Select(item => item.GetString()!).SequenceEqual(allowed, StringComparer.Ordinal)).IsTrue();
            }
        }
    }

    [Test]
    public async Task JsonSettingWithoutDescriptor_RemainsRejectedByBothGenerators()
    {
        foreach (SettingDefinition definition in new[]
        {
            EventResourceSettingDefinitions.EnabledDeliveryTypes,
            PublicExperienceSettingDefinitions.AnnouncementBarPublication
        })
        {
            var undescribed = new ConfigurationManifestSettingCatalogEntry(
                ConfigurationManifestScope.Tenant, definition);
            await Assert.That(() => ConfigurationManifestJsonSchemaGenerator.GenerateConfigurationManifest(
                [undescribed], ConfigurationManifestCatalog.TenantDocuments.Values)).Throws<InvalidOperationException>();
            await Assert.That(() => ConfigurationManifestJsonSchemaGenerator.GenerateTenantConfigurationPackage(
                [undescribed], ConfigurationManifestCatalog.TenantDocuments.Values)).Throws<InvalidOperationException>();
        }
    }

    [Test]
    public async Task GeneratedAnnouncementOperations_AreClosedTypedAndScopeBoundInBothArtifacts()
    {
        using JsonDocument manifest = JsonDocument.Parse(
            ConfigurationManifestJsonSchemaGenerator.GenerateConfigurationManifest());
        using JsonDocument package = JsonDocument.Parse(
            ConfigurationManifestJsonSchemaGenerator.GenerateTenantConfigurationPackage());
        foreach (var (settings, tenantScope) in new[]
        {
            (manifest.RootElement.GetProperty("$defs").GetProperty("instanceSettings"), false),
            (manifest.RootElement.GetProperty("$defs").GetProperty("tenantSettings"), true),
            (package.RootElement.GetProperty("$defs").GetProperty("tenantSettings"), true)
        })
        {
            JsonElement schema = settings.GetProperty("properties")
                .GetProperty(PublicExperienceSettingDefinitions.AnnouncementBarPublication.Key);
            JsonElement[] alternatives = schema.GetProperty("oneOf").EnumerateArray().ToArray();
            string[] operations = alternatives.Select(alternative => alternative.GetProperty("properties")
                .GetProperty("operation").GetProperty("const").GetString()!).ToArray();
            string[] expectedOperations = tenantScope
                ? ["publish", "disable", "setLock", "inherit"]
                : ["publish", "disable", "setLock"];
            await Assert.That(operations).IsEquivalentTo(expectedOperations);

            foreach (JsonElement alternative in alternatives)
            {
                JsonElement properties = alternative.GetProperty("properties");
                string operation = properties.GetProperty("operation").GetProperty("const").GetString()!;
                var expectedTypes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["operation"] = "string",
                    ["currentLock"] = "boolean"
                };
                if (operation == "publish")
                {
                    expectedTypes["enabled"] = "boolean";
                    expectedTypes["message"] = "string";
                    expectedTypes["linkText"] = "string";
                    expectedTypes["linkUrl"] = "string";
                    expectedTypes["redisplay"] = "boolean";
                }
                else if (operation == "setLock")
                    expectedTypes["isLocked"] = "boolean";

                await Assert.That(alternative.GetProperty("type").GetString()).IsEqualTo("object");
                await Assert.That(alternative.GetProperty("additionalProperties").GetBoolean()).IsFalse();
                await Assert.That(properties.EnumerateObject().Select(property => property.Name).ToArray())
                    .IsEquivalentTo(expectedTypes.Keys.ToArray());
                await Assert.That(alternative.GetProperty("required").EnumerateArray()
                    .Select(value => value.GetString()!).ToArray())
                    .IsEquivalentTo(expectedTypes.Keys.Where(name => name != "currentLock").ToArray());
                foreach (var (name, type) in expectedTypes)
                {
                    JsonElement property = properties.GetProperty(name);
                    await Assert.That(property.GetProperty("type").GetString()).IsEqualTo(type);
                    if (type == "string" && name != "operation")
                    {
                        await Assert.That(property.TryGetProperty("minLength", out _)).IsFalse();
                        await Assert.That(property.TryGetProperty("format", out _)).IsFalse();
                    }
                }
            }
        }
    }

    [Test]
    public async Task AnnouncementDescriptor_WithWrongScopeDefinitionOrCompetingShape_FailsClosed()
    {
        ConfigurationManifestSettingCatalogEntry announcement =
            ConfigurationManifestCatalog.TenantSettings[PublicExperienceSettingDefinitions.AnnouncementBarPublication.Key];
        foreach (ConfigurationManifestSettingCatalogEntry invalid in new[]
        {
            announcement with
            {
                AnnouncementOperation = new(ConfigurationManifestScope.Instance)
            },
            announcement with { Definition = EventResourceSettingDefinitions.EnabledDeliveryTypes },
            announcement with { Definition = PublicExperienceSettingDefinitions.EventCatalogLabel },
            announcement with { StringArray = new() },
            announcement with { AnnouncementOperation = null, StringArray = new() }
        })
        {
            await Assert.That(() => ConfigurationManifestJsonSchemaGenerator.GenerateConfigurationManifest(
                [], [], [invalid], [])).Throws<InvalidOperationException>();
            await Assert.That(() => ConfigurationManifestJsonSchemaGenerator.GenerateTenantConfigurationPackage(
                [invalid], [])).Throws<InvalidOperationException>();
        }
    }

    [Test]
    public async Task GeneratedSchema_MatchesCheckedInArtifactByteForByte()
    {
        byte[] expected = await File.ReadAllBytesAsync(ContextSystemHelpers.RepoPath(
            "schemas",
            "configuration-manifest-v1alpha2.schema.json"));
        byte[] actual =
            ConfigurationManifestJsonSchemaGenerator.GenerateConfigurationManifest();

        await Assert.That(actual.SequenceEqual(expected)).IsTrue();
    }

    [Test]
    public async Task GeneratedSchema_IsStableAcrossCultureAndCatalogEnumerationOrder()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar-SA");

            byte[] forward =
                ConfigurationManifestJsonSchemaGenerator.GenerateConfigurationManifest(
                ConfigurationManifestCatalog.TenantSettings.Values,
                ConfigurationManifestCatalog.TenantDocuments.Values);
            byte[] reverse =
                ConfigurationManifestJsonSchemaGenerator.GenerateConfigurationManifest(
                ConfigurationManifestCatalog.TenantSettings.Values.Reverse(),
                ConfigurationManifestCatalog.TenantDocuments.Values.Reverse());

            await Assert.That(forward.SequenceEqual(reverse)).IsTrue();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Test]
    public async Task GeneratedSchema_UsesGovernedDraftIdentityAndClosedObjects()
    {
        using JsonDocument schema = JsonDocument.Parse(
            ConfigurationManifestJsonSchemaGenerator.GenerateConfigurationManifest());
        JsonElement root = schema.RootElement;

        await Assert.That(root.GetProperty("$schema").GetString())
            .IsEqualTo("https://json-schema.org/draft/2020-12/schema");
        await Assert.That(root.GetProperty("$id").GetString())
            .IsEqualTo("https://schemas.islamu.org/event/configuration-manifest/v1alpha2/schema.json");
        await Assert.That(root.GetProperty("properties").GetProperty("$schema")
            .GetProperty("const").GetString())
            .IsEqualTo("https://schemas.islamu.org/event/configuration-manifest/v1alpha2/schema.json");
        await Assert.That(root.GetProperty("properties").GetProperty("apiVersion")
            .GetProperty("const").GetString())
            .IsEqualTo("configuration.islamu.org/v1alpha2");
        await Assert.That(root.GetProperty("properties").GetProperty("kind")
            .GetProperty("const").GetString())
            .IsEqualTo("ConfigurationManifest");
        JsonElement instance = root
            .GetProperty("$defs")
            .GetProperty("manifestInstance");
        await Assert.That(instance.GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray()).IsEquivalentTo(
            [
                "documents",
                "legalDocuments",
                "settings"
            ]);
        await Assert.That(AllTypedObjectsAreClosed(root)).IsTrue();
    }

    [Test]
    public async Task PortableExportMetadata_SatisfiesGeneratedSchemaContract()
    {
        var manifest = new ConfigurationManifestV1Alpha2
        {
            Schema = ConfigurationManifestContractMetadata.SchemaId,
            ApiVersion = ConfigurationManifestContractMetadata.ApiVersion,
            Kind = ConfigurationManifestContractMetadata.Kind,
            Metadata = new ConfigurationManifestMetadataV1Alpha2
            {
                Name = "current-instance",
                Export = new ConfigurationManifestExportMetadataV1Alpha2
                {
                    View = ConfigurationManifestExportMetadataValues.OverridesView,
                    EffectiveValuesFlattened = false,
                    SensitiveValuesOmitted = true,
                    AuthorityScope = ConfigurationManifestExportMetadataValues
                        .InstanceAndTenantsAuthorityScope,
                    SovereignValuesOmitted = true,
                    SovereignLockedFields = PaidEventPolicyAuthorityMetadata
                        .SovereignLockedFields
                }
            },
            Spec = new ConfigurationManifestSpecV1Alpha2
            {
                Instance = new ConfigurationManifestInstanceV1Alpha2
                {
                    Settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal),
                    Documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(
                        StringComparer.Ordinal)
                },
                Tenants =
                [
                    new ConfigurationManifestTenantV1Alpha2
                    {
                        Metadata = new ConfigurationManifestTenantMetadataV1Alpha2
                        {
                            Name = "primary"
                        },
                        Spec = new ConfigurationManifestTenantSpecV1Alpha2
                        {
                            DisplayName = "Primary",
                            Settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal),
                            Documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(
                                StringComparer.Ordinal)
                        }
                    }
                ]
            }
        };
        byte[] bytes = ConfigurationManifestExportJsonSerializer.Serialize(manifest);

        using JsonDocument export = JsonDocument.Parse(bytes);
        using JsonDocument schema = JsonDocument.Parse(
            ConfigurationManifestJsonSchemaGenerator.GenerateConfigurationManifest());
        JsonElement exportedMetadata = export.RootElement
            .GetProperty("metadata")
            .GetProperty("export");
        JsonElement metadataSchema = schema.RootElement
            .GetProperty("$defs")
            .GetProperty("manifestExportMetadata");
        string[] required = metadataSchema.GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();

        await Assert.That(metadataSchema.GetProperty("additionalProperties").GetBoolean())
            .IsFalse();
        await Assert.That(exportedMetadata.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal)
                .ToArray())
            .IsEquivalentTo(required.Order(StringComparer.Ordinal).ToArray());
        await Assert.That(exportedMetadata.GetProperty("authorityScope").GetString())
            .IsEqualTo(metadataSchema.GetProperty("properties")
                .GetProperty("authorityScope")
                .GetProperty("const")
                .GetString());
    }

    [Test]
    public async Task GeneratedSchema_CoversOnlyExplicitCatalogEntries()
    {
        using JsonDocument schema = JsonDocument.Parse(
            ConfigurationManifestJsonSchemaGenerator.GenerateConfigurationManifest());
        string[] schemaKeys = schema.RootElement
            .GetProperty("$defs")
            .GetProperty("tenantSettings")
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] catalogKeys = ConfigurationManifestCatalog.TenantSettings.Keys
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(schemaKeys.SequenceEqual(catalogKeys, StringComparer.Ordinal)).IsTrue();

        string[] instanceSchemaKeys = schema.RootElement
            .GetProperty("$defs")
            .GetProperty("instanceSettings")
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] instanceCatalogKeys =
            ConfigurationManifestCatalog.InstanceSettings.Keys
                .Order(StringComparer.Ordinal)
                .ToArray();

        await Assert.That(instanceSchemaKeys.SequenceEqual(
            instanceCatalogKeys,
            StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task GeneratedSchema_PaidPolicyIsVersionedClosedAndSovereignFieldFree()
    {
        using JsonDocument schema = JsonDocument.Parse(
            ConfigurationManifestJsonSchemaGenerator.GenerateConfigurationManifest());
        JsonElement definitions = schema.RootElement.GetProperty("$defs");
        JsonElement documents = definitions
            .GetProperty("tenantDocuments")
            .GetProperty("properties");
        JsonElement instanceDocuments = definitions
            .GetProperty("instanceDocuments")
            .GetProperty("properties");
        JsonElement policyDocument = documents.GetProperty(
            ConfigurationManifestDocumentKeys.TenantPaidEventPolicy);
        JsonElement instancePolicyDocument = instanceDocuments.GetProperty(
            ConfigurationManifestDocumentKeys.InstancePaidEventPolicy);
        JsonElement payload = definitions.GetProperty("paidEventPolicyPayload");
        string[] properties = payload.GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(policyDocument.GetProperty("$ref").GetString())
            .IsEqualTo("#/$defs/tenantPaidEventPolicyDocument");
        await Assert.That(instancePolicyDocument.GetProperty("$ref").GetString())
            .IsEqualTo("#/$defs/instancePaidEventPolicyDocument");
        await Assert.That(payload.GetProperty("additionalProperties").GetBoolean()).IsFalse();
        await Assert.That(properties).IsEquivalentTo(
        [
            "allowedCurrencyCodes",
            "allowedOrganizerKindIds",
            "currencyRiskLimits",
            "defaultCurrencyCode",
            "farFutureReviewThresholdDays",
            "isPaymentsEnabled",
            "refundProtectionIds",
            "requiresFirstPaidEventReview",
            "requiresLocalVerification"
        ]);
        await Assert.That(payload.GetRawText()).DoesNotContain("operator");
        await Assert.That(payload.GetRawText()).DoesNotContain("provider");
        await Assert.That(payload.GetRawText()).DoesNotContain("credential");
        await Assert.That(payload.GetRawText()).DoesNotContain("saleControl");
        await Assert.That(payload.GetRawText()).DoesNotContain("refundExecution");
        await Assert.That(payload.GetRawText()).DoesNotContain("tenantId");
    }

    [Test]
    public async Task Generate_SensitiveOrNonTenantCatalogEntry_FailsClosed()
    {
        var unsafeEntry = new ConfigurationManifestSettingCatalogEntry(
            ConfigurationManifestScope.Tenant,
            new SettingDefinition(
                "test.sensitive",
                SettingValueType.String,
                "\"\"",
                "Test",
                "Synthetic sensitive setting",
                IsSensitive: true));
        InvalidOperationException? exception = null;

        try
        {
            _ = ConfigurationManifestJsonSchemaGenerator.GenerateConfigurationManifest(
                [unsafeEntry],
                ConfigurationManifestCatalog.TenantDocuments.Values);
        }
        catch (InvalidOperationException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
    }

    [Test]
    public async Task TenantPackageSchema_MatchesArtifactAndExcludesTargetAuthority()
    {
        byte[] expected = await File.ReadAllBytesAsync(ContextSystemHelpers.RepoPath(
            "schemas",
            "tenant-configuration-package-v1alpha2.schema.json"));
        byte[] actual =
            ConfigurationManifestJsonSchemaGenerator.GenerateTenantConfigurationPackage();
        using JsonDocument schema = JsonDocument.Parse(actual);
        string text = schema.RootElement.GetRawText();

        await Assert.That(actual.SequenceEqual(expected)).IsTrue();
        await Assert.That(schema.RootElement.GetProperty("$schema").GetString())
            .IsEqualTo("https://json-schema.org/draft/2020-12/schema");
        await Assert.That(schema.RootElement.GetProperty("$id").GetString())
            .IsEqualTo("https://schemas.islamu.org/event/tenant-configuration-package/v1alpha2/schema.json");
        await Assert.That(schema.RootElement.GetProperty("properties")
            .GetProperty("$schema").GetProperty("const").GetString())
            .IsEqualTo("https://schemas.islamu.org/event/tenant-configuration-package/v1alpha2/schema.json");
        await Assert.That(schema.RootElement.GetProperty("properties")
            .GetProperty("apiVersion").GetProperty("const").GetString())
            .IsEqualTo("configuration.islamu.org/v1alpha2");
        await Assert.That(schema.RootElement.GetProperty("properties")
            .GetProperty("kind").GetProperty("const").GetString())
            .IsEqualTo("TenantConfigurationPackage");
        await Assert.That(text).DoesNotContain("targetTenantId");
        await Assert.That(text).DoesNotContain("targetInstanceId");
        await Assert.That(AllTypedObjectsAreClosed(schema.RootElement)).IsTrue();
    }

    private static bool AllTypedObjectsAreClosed(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("type", out JsonElement type)
                && type.ValueKind == JsonValueKind.String
                && string.Equals(type.GetString(), "object", StringComparison.Ordinal)
                && (!element.TryGetProperty("additionalProperties", out JsonElement additional)
                    || additional.ValueKind != JsonValueKind.False))
            {
                return false;
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!AllTypedObjectsAreClosed(property.Value))
                    return false;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (!AllTypedObjectsAreClosed(item))
                    return false;
            }
        }

        return true;
    }
}
