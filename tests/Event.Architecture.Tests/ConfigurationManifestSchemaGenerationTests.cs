namespace Event.Architecture.Tests;

using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Explore.Application.DTOs.PaidEventPolicies;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using ISLAMU.ConfigurationManifest.SchemaGenerator;

public sealed class ConfigurationManifestSchemaGenerationTests
{
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
            .IsEqualTo(ConfigurationManifestContractMetadata.SchemaId);
        await Assert.That(root.GetProperty("properties").GetProperty("$schema")
            .GetProperty("const").GetString())
            .IsEqualTo(ConfigurationManifestContractMetadata.SchemaId);
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
    public async Task CanonicalExportMetadata_SatisfiesGeneratedSchemaContract()
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
        Type serializer = typeof(ConfigurationManifestCatalog).Assembly.GetType(
            "Explore.Application.Features.ConfigurationManifest.Application.ConfigurationManifestExportJsonSerializer")
            ?? throw new InvalidOperationException("Missing canonical export serializer.");
        MethodInfo serialize = serializer.GetMethod(
            "Serialize",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Missing canonical export serialization entry point.");
        byte[] bytes = (byte[])serialize.Invoke(null, [manifest])!;

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
    public async Task BuildWorkflow_RoutesGeneratorAndArtifactToArchitectureLane()
    {
        string workflow = await File.ReadAllTextAsync(ContextSystemHelpers.RepoPath(
            ".github",
            "workflows",
            "test.yml"));

        await Assert.That(workflow.Contains(
            "schemas/configuration-manifest-v1alpha2.schema.json|schemas/tenant-configuration-package-v1alpha2.schema.json) return 1 ;;",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(workflow.Contains(
            "schemas/configuration-manifest-v1alpha2.schema.json|schemas/tenant-configuration-package-v1alpha2.schema.json|eng/configuration-manifest-schema/*)",
            StringComparison.Ordinal)).IsTrue();
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
        await Assert.That(schema.RootElement.GetProperty("$id").GetString())
            .IsEqualTo(TenantConfigurationPackageContractMetadata.SchemaId);
        await Assert.That(schema.RootElement.GetProperty("properties")
            .GetProperty("kind").GetProperty("const").GetString())
            .IsEqualTo(TenantConfigurationPackageContractMetadata.Kind);
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
