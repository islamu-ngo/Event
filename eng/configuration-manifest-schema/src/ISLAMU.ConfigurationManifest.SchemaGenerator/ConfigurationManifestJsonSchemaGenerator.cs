namespace ISLAMU.ConfigurationManifest.SchemaGenerator;

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.DTOs.PaidEventPolicies;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Validation;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;

public static class ConfigurationManifestJsonSchemaGenerator
{
    public const string SchemaDialect = "https://json-schema.org/draft/2020-12/schema";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static byte[] GenerateConfigurationManifest() =>
        GenerateConfigurationManifest(
            ConfigurationManifestCatalog.InstanceSettings.Values,
            ConfigurationManifestCatalog.InstanceDocuments.Values,
            ConfigurationManifestCatalog.TenantSettings.Values,
            ConfigurationManifestCatalog.TenantDocuments.Values);

    public static byte[] GenerateConfigurationManifest(
        IEnumerable<ConfigurationManifestSettingCatalogEntry> tenantSettingEntries,
        IEnumerable<ConfigurationManifestDocumentCatalogEntry> tenantDocumentEntries) =>
        GenerateConfigurationManifest(
            ConfigurationManifestCatalog.InstanceSettings.Values,
            ConfigurationManifestCatalog.InstanceDocuments.Values,
            tenantSettingEntries,
            tenantDocumentEntries);

    public static byte[] GenerateConfigurationManifest(
        IEnumerable<ConfigurationManifestSettingCatalogEntry> instanceSettingEntries,
        IEnumerable<ConfigurationManifestDocumentCatalogEntry> instanceDocumentEntries,
        IEnumerable<ConfigurationManifestSettingCatalogEntry> tenantSettingEntries,
        IEnumerable<ConfigurationManifestDocumentCatalogEntry> tenantDocumentEntries)
    {
        ArgumentNullException.ThrowIfNull(instanceSettingEntries);
        ArgumentNullException.ThrowIfNull(instanceDocumentEntries);
        ArgumentNullException.ThrowIfNull(tenantSettingEntries);
        ArgumentNullException.ThrowIfNull(tenantDocumentEntries);

        ConfigurationManifestSettingCatalogEntry[] instanceSettings =
            ValidateSettings(instanceSettingEntries, ConfigurationManifestScope.Instance);
        ConfigurationManifestDocumentCatalogEntry[] instanceDocuments =
            ValidateDocuments(instanceDocumentEntries, ConfigurationManifestScope.Instance);
        ConfigurationManifestSettingCatalogEntry[] tenantSettings =
            ValidateSettings(tenantSettingEntries, ConfigurationManifestScope.Tenant);
        ConfigurationManifestDocumentCatalogEntry[] tenantDocuments =
            ValidateDocuments(tenantDocumentEntries, ConfigurationManifestScope.Tenant);
        JsonObject root = BuildSchema(
            instanceSettings,
            instanceDocuments,
            tenantSettings,
            tenantDocuments);
        string json = root.ToJsonString(SerializerOptions)
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        return Encoding.UTF8.GetBytes($"{json}\n");
    }

    public static byte[] GenerateTenantConfigurationPackage() =>
        GenerateTenantConfigurationPackage(
            ConfigurationManifestCatalog.TenantSettings.Values,
            ConfigurationManifestCatalog.TenantDocuments.Values);

    public static byte[] GenerateTenantConfigurationPackage(
        IEnumerable<ConfigurationManifestSettingCatalogEntry> tenantSettingEntries,
        IEnumerable<ConfigurationManifestDocumentCatalogEntry> tenantDocumentEntries)
    {
        ArgumentNullException.ThrowIfNull(tenantSettingEntries);
        ArgumentNullException.ThrowIfNull(tenantDocumentEntries);

        ConfigurationManifestSettingCatalogEntry[] tenantSettings =
            ValidateSettings(tenantSettingEntries, ConfigurationManifestScope.Tenant);
        ConfigurationManifestDocumentCatalogEntry[] tenantDocuments =
            ValidateDocuments(tenantDocumentEntries, ConfigurationManifestScope.Tenant);
        string json = BuildTenantPackageSchema(tenantSettings, tenantDocuments)
            .ToJsonString(SerializerOptions)
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        return Encoding.UTF8.GetBytes($"{json}\n");
    }

    private static ConfigurationManifestSettingCatalogEntry[] ValidateSettings(
        IEnumerable<ConfigurationManifestSettingCatalogEntry> entries,
        ConfigurationManifestScope scope)
    {
        ConfigurationManifestSettingCatalogEntry[] ordered = entries
            .OrderBy(entry => entry.Definition.Key, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Select(entry => entry.Definition.Key)
            .Distinct(StringComparer.Ordinal)
            .Count() != ordered.Length)
        {
            throw new InvalidOperationException(
                "The ConfigurationManifest tenant-section setting catalog contains duplicate keys.");
        }

        foreach (ConfigurationManifestSettingCatalogEntry entry in ordered)
        {
            SettingDefinition definition = entry.Definition;
            SettingScope settingScope = scope == ConfigurationManifestScope.Instance
                ? SettingScope.Instance
                : SettingScope.Tenant;
            if (entry.Scope != scope
                || !ReferenceEquals(SettingRegistry.Get(definition.Key), definition)
                || definition.MinScope > settingScope
                || definition.MaxScope < settingScope
                || definition.IsSensitive)
            {
                throw new InvalidOperationException(
                    "The ConfigurationManifest tenant-section setting catalog is not safe.");
            }

            if (definition.ValueType == SettingValueType.Json)
            {
                throw new InvalidOperationException(
                    "JSON settings require an explicit typed schema descriptor.");
            }
        }

        return ordered;
    }

    private static ConfigurationManifestDocumentCatalogEntry[] ValidateDocuments(
        IEnumerable<ConfigurationManifestDocumentCatalogEntry> entries,
        ConfigurationManifestScope scope)
    {
        ConfigurationManifestDocumentCatalogEntry[] ordered = entries
            .OrderBy(entry => entry.DocumentKey, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Select(entry => entry.DocumentKey)
            .Distinct(StringComparer.Ordinal)
            .Count() != ordered.Length)
        {
            throw new InvalidOperationException(
                "The ConfigurationManifest tenant-section document catalog contains duplicate keys.");
        }

        foreach (ConfigurationManifestDocumentCatalogEntry entry in ordered)
        {
            bool isBranding = scope == ConfigurationManifestScope.Tenant
                && entry.Scope == scope
                && string.Equals(
                    entry.DocumentKey,
                    SettingsDocumentKeys.Tenant.Branding,
                    StringComparison.Ordinal)
                && entry.SchemaVersion
                    == TenantBrandingSettingsDocumentDefaults.SchemaVersion
                && entry.DefaultsVersion
                    == TenantBrandingSettingsDocumentDefaults.DefaultsVersion
                && entry.PayloadType == typeof(BrandingSettings)
                && entry.Storage
                    == ConfigurationManifestDocumentStorage.TenantSettingsDocument;
            string paidPolicyKey = scope == ConfigurationManifestScope.Instance
                ? ConfigurationManifestDocumentKeys.InstancePaidEventPolicy
                : ConfigurationManifestDocumentKeys.TenantPaidEventPolicy;
            bool isPaidPolicy = entry.Scope == scope
                && string.Equals(entry.DocumentKey, paidPolicyKey, StringComparison.Ordinal)
                && entry.SchemaVersion == 1
                && entry.DefaultsVersion is null
                && entry.PayloadType
                    == typeof(ConfigurationManifestPaidEventPolicyPayloadV1Alpha2)
                && entry.Storage
                    == ConfigurationManifestDocumentStorage.PaidEventPolicy;
            if (!isBranding && !isPaidPolicy)
            {
                throw new InvalidOperationException(
                    "The ConfigurationManifest tenant-section document catalog is not governed.");
            }
        }

        return ordered;
    }

    private static JsonObject BuildSchema(
        IReadOnlyList<ConfigurationManifestSettingCatalogEntry> instanceSettings,
        IReadOnlyList<ConfigurationManifestDocumentCatalogEntry> instanceDocuments,
        IReadOnlyList<ConfigurationManifestSettingCatalogEntry> tenantSettings,
        IReadOnlyList<ConfigurationManifestDocumentCatalogEntry> tenantDocuments)
    {
        var rootProperties = new JsonObject
        {
            ["$schema"] = new JsonObject
            {
                ["const"] = ConfigurationManifestContractMetadata.SchemaId
            },
            ["apiVersion"] = new JsonObject
            {
                ["const"] = ConfigurationManifestContractMetadata.ApiVersion
            },
            ["kind"] = new JsonObject
            {
                ["const"] = ConfigurationManifestContractMetadata.Kind
            },
            ["metadata"] = Ref("manifestMetadata"),
            ["spec"] = Ref("manifestSpec")
        };

        return new JsonObject
        {
            ["$comment"] = "ABOUTME: Governed JSON Schema for ConfigurationManifest files.",
            ["$comment2"] = "ABOUTME: Generated deterministically; edit the catalog or generator, never this artifact.",
            ["$schema"] = SchemaDialect,
            ["$id"] = ConfigurationManifestContractMetadata.SchemaId,
            ["title"] = "ISLAMU Event Configuration Manifest",
            ["description"] = "Declarative non-secret instance and tenant bootstrap configuration.",
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = Strings(["$schema", "apiVersion", "kind", "metadata", "spec"]),
            ["properties"] = rootProperties,
            ["$defs"] = BuildDefinitions(
                instanceSettings,
                instanceDocuments,
                tenantSettings,
                tenantDocuments)
        };
    }

    private static JsonObject BuildTenantPackageSchema(
        IReadOnlyList<ConfigurationManifestSettingCatalogEntry> tenantSettings,
        IReadOnlyList<ConfigurationManifestDocumentCatalogEntry> tenantDocuments)
    {
        var definitions = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal)
        {
            ["brandingPayload"] = BrandingPayloadSchema(),
            ["legalDocumentLocalizedSource"] = LegalDocumentLocalizedSourceSchema(),
            ["legalTemplateProvenance"] = LegalTemplateProvenanceSchema(),
            ["manifestExportMetadata"] = ManifestExportMetadataSchema(),
            ["paidEventPolicyCurrencyRiskLimit"] = PaidEventPolicyCurrencyRiskLimitSchema(),
            ["paidEventPolicyPayload"] = PaidEventPolicyPayloadSchema(),
            ["tenantBrandingDocument"] = BrandingDocumentSchema(),
            ["tenantDocuments"] = TenantDocumentsSchema(tenantDocuments),
            ["tenantLegalDocuments"] =
                LegalDocumentsSchema(LegalDocumentScope.Tenant),
            ["tenantPackageMetadata"] = TenantPackageMetadataSchema(),
            ["tenantPackageSource"] = TenantPackageSourceSchema(),
            ["tenantPackageSpec"] = TenantPackageSpecSchema(),
            ["tenantPaidEventPolicyDocument"] = PaidEventPolicyDocumentSchema(),
            ["tenantSettings"] = SettingsSchema(tenantSettings)
        };
        var schemaDefinitions = new JsonObject();
        foreach ((string key, JsonNode? value) in definitions)
            schemaDefinitions[key] = value;

        return new JsonObject
        {
            ["$comment"] = "ABOUTME: Governed JSON Schema for TenantConfigurationPackage files.",
            ["$comment2"] = "ABOUTME: Generated deterministically; edit the catalog or generator, never this artifact.",
            ["$schema"] = SchemaDialect,
            ["$id"] = TenantConfigurationPackageContractMetadata.SchemaId,
            ["title"] = "ISLAMU Event Tenant Configuration Package",
            ["description"] = "Portable non-secret tenant configuration whose target is selected by trusted route authority.",
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = Strings(["$schema", "apiVersion", "kind", "metadata", "spec"]),
            ["properties"] = new JsonObject
            {
                ["$schema"] = new JsonObject
                {
                    ["const"] = TenantConfigurationPackageContractMetadata.SchemaId
                },
                ["apiVersion"] = new JsonObject
                {
                    ["const"] = TenantConfigurationPackageContractMetadata.ApiVersion
                },
                ["kind"] = new JsonObject
                {
                    ["const"] = TenantConfigurationPackageContractMetadata.Kind
                },
                ["metadata"] = Ref("tenantPackageMetadata"),
                ["spec"] = Ref("tenantPackageSpec")
            },
            ["$defs"] = schemaDefinitions
        };
    }

    private static JsonObject BuildDefinitions(
        IReadOnlyList<ConfigurationManifestSettingCatalogEntry> instanceSettings,
        IReadOnlyList<ConfigurationManifestDocumentCatalogEntry> instanceDocuments,
        IReadOnlyList<ConfigurationManifestSettingCatalogEntry> tenantSettings,
        IReadOnlyList<ConfigurationManifestDocumentCatalogEntry> tenantDocuments)
    {
        var definitions = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal)
        {
            ["brandingPayload"] = BrandingPayloadSchema(),
            ["instanceDocuments"] = InstanceDocumentsSchema(instanceDocuments),
            ["instanceLegalDocuments"] =
                LegalDocumentsSchema(LegalDocumentScope.Instance),
            ["instancePaidEventPolicyDocument"] = PaidEventPolicyDocumentSchema(),
            ["instanceSettings"] = SettingsSchema(instanceSettings),
            ["legalDocumentLocalizedSource"] = LegalDocumentLocalizedSourceSchema(),
            ["legalTemplateProvenance"] = LegalTemplateProvenanceSchema(),
            ["manifestExportMetadata"] = ManifestExportMetadataSchema(),
            ["manifestInstance"] = ManifestInstanceSchema(),
            ["manifestMetadata"] = ManifestMetadataSchema(),
            ["manifestSpec"] = ManifestSpecSchema(),
            ["manifestTenant"] = ManifestTenantSchema(),
            ["paidEventPolicyCurrencyRiskLimit"] = PaidEventPolicyCurrencyRiskLimitSchema(),
            ["paidEventPolicyPayload"] = PaidEventPolicyPayloadSchema(),
            ["tenantBrandingDocument"] = BrandingDocumentSchema(),
            ["tenantDocuments"] = TenantDocumentsSchema(tenantDocuments),
            ["tenantLegalDocuments"] =
                LegalDocumentsSchema(LegalDocumentScope.Tenant),
            ["tenantMetadata"] = TenantMetadataSchema(),
            ["tenantPaidEventPolicyDocument"] = PaidEventPolicyDocumentSchema(),
            ["tenantSettings"] = SettingsSchema(tenantSettings),
            ["tenantSpec"] = TenantSpecSchema()
        };
        var result = new JsonObject();
        foreach ((string key, JsonNode? value) in definitions)
            result[key] = value;
        return result;
    }

    private static JsonObject ManifestMetadataSchema() =>
        ClosedObject(
            new JsonObject
            {
                ["export"] = Ref("manifestExportMetadata"),
                ["name"] = BoundedString(
                    maximumLength: 100,
                    pattern: "^[a-z0-9]+(?:[.-][a-z0-9]+)*$")
            },
            ["name"]);

    private static JsonObject TenantPackageMetadataSchema() =>
        ClosedObject(
            new JsonObject
            {
                ["export"] = Ref("manifestExportMetadata"),
                ["name"] = BoundedString(
                    maximumLength: 100,
                    pattern: "^[a-z0-9]+(?:[.-][a-z0-9]+)*$"),
                ["source"] = Ref("tenantPackageSource")
            },
            ["name", "source"]);

    private static JsonObject TenantPackageSourceSchema() =>
        ClosedObject(
            new JsonObject
            {
                ["instanceName"] = new JsonObject
                {
                    ["type"] = Strings(["null", "string"]),
                    ["minLength"] = 1,
                    ["maxLength"] = 100,
                    ["pattern"] = "^[a-z0-9]+(?:[.-][a-z0-9]+)*$"
                },
                ["tenantName"] = BoundedString(
                    maximumLength: 100,
                    pattern: "^[a-z0-9]+(?:-[a-z0-9]+)*$")
            },
            ["tenantName"]);

    private static JsonObject TenantPackageSpecSchema() =>
        ClosedObject(
            new JsonObject
            {
                ["displayName"] = BoundedString(maximumLength: 500),
                ["documents"] = Ref("tenantDocuments"),
                ["legalDocuments"] = Ref("tenantLegalDocuments"),
                ["settings"] = Ref("tenantSettings")
            },
            ["displayName", "documents", "legalDocuments", "settings"]);

    private static JsonObject ManifestExportMetadataSchema() =>
        ClosedObject(
            new JsonObject
            {
                ["authorityScope"] = new JsonObject
                {
                    ["const"] = ConfigurationManifestExportMetadataValues
                        .InstanceAndTenantsAuthorityScope
                },
                ["effectiveValuesFlattened"] = new JsonObject
                {
                    ["type"] = "boolean"
                },
                ["sensitiveValuesOmitted"] = new JsonObject
                {
                    ["const"] = true
                },
                ["sovereignLockedFields"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] =
                        PaidEventPolicyAuthorityMetadata.SovereignLockedFields.Count,
                    ["maxItems"] =
                        PaidEventPolicyAuthorityMetadata.SovereignLockedFields.Count,
                    ["uniqueItems"] = true,
                    ["items"] = new JsonObject
                    {
                        ["enum"] = Strings(
                            PaidEventPolicyAuthorityMetadata.SovereignLockedFields)
                    }
                },
                ["sovereignValuesOmitted"] = new JsonObject
                {
                    ["const"] = true
                },
                ["view"] = new JsonObject
                {
                    ["enum"] = Strings(
                    [
                        ConfigurationManifestExportMetadataValues.OverridesView,
                        ConfigurationManifestExportMetadataValues.PortableView
                    ])
                }
            },
            [
                "authorityScope",
                "effectiveValuesFlattened",
                "sensitiveValuesOmitted",
                "sovereignLockedFields",
                "sovereignValuesOmitted",
                "view"
            ]);

    private static JsonObject ManifestSpecSchema() =>
        ClosedObject(
            new JsonObject
            {
                ["instance"] = Ref("manifestInstance"),
                ["tenants"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = 1,
                    ["maxItems"] = ConfigurationManifestValidator.MaximumTenantCount,
                    ["items"] = Ref("manifestTenant")
                }
            },
            ["instance", "tenants"]);

    private static JsonObject ManifestInstanceSchema() =>
        ClosedObject(
            new JsonObject
            {
                ["documents"] = Ref("instanceDocuments"),
                ["legalDocuments"] = Ref("instanceLegalDocuments"),
                ["settings"] = Ref("instanceSettings")
            },
            ["documents", "legalDocuments", "settings"]);

    private static JsonObject InstanceDocumentsSchema(
        IReadOnlyList<ConfigurationManifestDocumentCatalogEntry> documents)
    {
        var properties = new JsonObject();
        foreach (ConfigurationManifestDocumentCatalogEntry entry in documents)
        {
            properties[entry.DocumentKey] = entry.DocumentKey switch
            {
                ConfigurationManifestDocumentKeys.InstancePaidEventPolicy =>
                    Ref("instancePaidEventPolicyDocument"),
                _ => throw new InvalidOperationException(
                    "The configuration-manifest instance document schema is unsupported.")
            };
        }
        return ClosedObject(properties, []);
    }

    private static JsonObject ManifestTenantSchema() =>
        ClosedObject(
            new JsonObject
            {
                ["metadata"] = Ref("tenantMetadata"),
                ["spec"] = Ref("tenantSpec")
            },
            ["metadata", "spec"]);

    private static JsonObject TenantMetadataSchema() =>
        ClosedObject(
            new JsonObject
            {
                ["name"] = BoundedString(
                    maximumLength: 100,
                    pattern: "^[a-z0-9]+(?:-[a-z0-9]+)*$")
            },
            ["name"]);

    private static JsonObject TenantSpecSchema() =>
        ClosedObject(
            new JsonObject
            {
                ["displayName"] = BoundedString(maximumLength: 500),
                ["documents"] = Ref("tenantDocuments"),
                ["legalDocuments"] = Ref("tenantLegalDocuments"),
                ["settings"] = Ref("tenantSettings")
            },
            ["displayName", "documents", "legalDocuments", "settings"]);

    private static JsonObject LegalDocumentsSchema(LegalDocumentScope scope)
    {
        var properties = new JsonObject();
        foreach (LegalDocumentKindDescriptor descriptor in
                 LegalDocumentKindCatalog.Entries.Values
                     .Where(descriptor => descriptor.Scope == scope)
                     .OrderBy(descriptor => descriptor.Kind))
        {
            properties[descriptor.Kind.ToString()] =
                LegalDocumentSchema(descriptor.Kind);
        }

        return ClosedObject(properties, []);
    }

    private static JsonObject LegalDocumentSchema(LegalDocumentKind kind) =>
        ClosedObject(
            new JsonObject
            {
                ["accountableIdentityReference"] =
                    BoundedString(maximumLength: 200),
                ["audience"] = new JsonObject
                {
                    ["enum"] = Strings(Enum.GetNames<LegalDocumentAudience>())
                },
                ["changeSummary"] = BoundedString(maximumLength: 500),
                ["jurisdictionAssumptions"] = new JsonObject
                {
                    ["type"] = "array",
                    ["maxItems"] = 16,
                    ["uniqueItems"] = true,
                    ["items"] = BoundedString(maximumLength: 100)
                },
                ["kind"] = new JsonObject
                {
                    ["const"] = kind.ToString()
                },
                ["lifecycleIntent"] = new JsonObject
                {
                    ["enum"] = Strings(
                    [
                        "Draft",
                        "ReviewRequired",
                        "ProposedPublication"
                    ])
                },
                ["localizations"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = 1,
                    ["maxItems"] =
                        ISLAMU.Wire.Contracts.ConfigurationPortability.LegalMarkdownContentLimits.MaximumLocalesPerDocument,
                    ["items"] = Ref("legalDocumentLocalizedSource")
                },
                ["proposedEffectiveAt"] = new JsonObject
                {
                    ["type"] = "string",
                    ["format"] = "date-time"
                },
                ["requiresFreshAcceptance"] = new JsonObject
                {
                    ["type"] = "boolean"
                },
                ["templateProvenance"] = Ref("legalTemplateProvenance")
            },
            [
                "audience",
                "jurisdictionAssumptions",
                "kind",
                "lifecycleIntent",
                "localizations",
                "requiresFreshAcceptance"
            ]);

    private static JsonObject LegalDocumentLocalizedSourceSchema() =>
        ClosedObject(
            new JsonObject
            {
                ["languageTag"] = BoundedString(
                    ISLAMU.Wire.Contracts.ConfigurationPortability.LegalMarkdownContentLimits.MaximumLanguageTagLength,
                    "^(?:[A-Za-z]{2,3}(?:-[A-Za-z0-9]{1,8})*|x(?:-[A-Za-z0-9]{1,8})+)$"),
                ["markdown"] = BoundedString(
                    ISLAMU.Wire.Contracts.ConfigurationPortability.LegalMarkdownContentLimits.MaximumMarkdownUtf8BytesPerLocale),
                ["summary"] = BoundedString(
                    ISLAMU.Wire.Contracts.ConfigurationPortability.LegalMarkdownContentLimits.MaximumSummaryLength),
                ["title"] = BoundedString(
                    ISLAMU.Wire.Contracts.ConfigurationPortability.LegalMarkdownContentLimits.MaximumTitleLength)
            },
            ["languageTag", "markdown", "summary", "title"]);

    private static JsonObject LegalTemplateProvenanceSchema() =>
        ClosedObject(
            new JsonObject
            {
                ["licenseExpression"] = BoundedString(maximumLength: 100),
                ["reviewReference"] = BoundedString(maximumLength: 200),
                ["sourceKind"] = new JsonObject
                {
                    ["enum"] = Strings(
                        Enum.GetNames<LegalDocumentTemplateSourceKind>())
                },
                ["templateId"] = BoundedString(maximumLength: 100),
                ["templateVersion"] = BoundedString(maximumLength: 50)
            },
            [
                "licenseExpression",
                "reviewReference",
                "sourceKind",
                "templateId",
                "templateVersion"
            ]);

    private static JsonObject SettingsSchema(
        IReadOnlyList<ConfigurationManifestSettingCatalogEntry> settings)
    {
        var properties = new JsonObject();
        foreach (ConfigurationManifestSettingCatalogEntry entry in settings)
            properties[entry.Definition.Key] = SettingSchema(entry);
        return ClosedObject(properties, []);
    }

    private static JsonObject SettingSchema(
        ConfigurationManifestSettingCatalogEntry entry)
    {
        SettingDefinition definition = entry.Definition;
        JsonObject schema = definition.ValueType switch
        {
            SettingValueType.String => new JsonObject { ["type"] = "string" },
            SettingValueType.Integer or SettingValueType.Long =>
                new JsonObject { ["type"] = "integer" },
            SettingValueType.Boolean => new JsonObject { ["type"] = "boolean" },
            SettingValueType.Decimal => new JsonObject { ["type"] = "number" },
            SettingValueType.DateTime => new JsonObject
            {
                ["type"] = "string",
                ["format"] = "date-time"
            },
            _ => throw new InvalidOperationException(
                "The configuration-manifest schema setting type is unsupported.")
        };

        if (entry.MaximumStringLength.HasValue)
            schema["maxLength"] = entry.MaximumStringLength.Value;
        if (definition.AllowedValues is not null)
        {
            schema["enum"] = Strings(
                definition.AllowedValues.Order(StringComparer.Ordinal));
        }

        return schema;
    }

    private static JsonObject TenantDocumentsSchema(
        IReadOnlyList<ConfigurationManifestDocumentCatalogEntry> documents)
    {
        var properties = new JsonObject();
        foreach (ConfigurationManifestDocumentCatalogEntry entry in documents)
        {
            properties[entry.DocumentKey] = entry.DocumentKey switch
            {
                SettingsDocumentKeys.Tenant.Branding => Ref("tenantBrandingDocument"),
                ConfigurationManifestDocumentKeys.TenantPaidEventPolicy =>
                    Ref("tenantPaidEventPolicyDocument"),
                _ => throw new InvalidOperationException(
                    "The ConfigurationManifest tenant-section document schema is unsupported.")
            };
        }
        return ClosedObject(properties, []);
    }

    private static JsonObject BrandingDocumentSchema() =>
        ClosedObject(
            new JsonObject
            {
                ["payload"] = Ref("brandingPayload"),
                ["schemaVersion"] = new JsonObject
                {
                    ["const"] = TenantBrandingSettingsDocumentDefaults.SchemaVersion
                }
            },
            ["payload", "schemaVersion"]);

    private static JsonObject PaidEventPolicyDocumentSchema() =>
        ClosedObject(
            new JsonObject
            {
                ["payload"] = Ref("paidEventPolicyPayload"),
                ["schemaVersion"] = new JsonObject
                {
                    ["const"] = 1
                }
            },
            ["payload", "schemaVersion"]);

    private static JsonObject PaidEventPolicyPayloadSchema()
    {
        var nullableCurrency = new JsonObject
        {
            ["type"] = Strings(["null", "string"]),
            ["minLength"] = 3,
            ["maxLength"] = 3,
            ["pattern"] = "^[A-Z]{3}$"
        };
        var nullablePositiveInteger = new JsonObject
        {
            ["type"] = Strings(["integer", "null"]),
            ["minimum"] = 1
        };
        return ClosedObject(
            new JsonObject
            {
                ["allowedCurrencyCodes"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = 1,
                    ["maxItems"] = 64,
                    ["uniqueItems"] = true,
                    ["items"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["minLength"] = 3,
                        ["maxLength"] = 3,
                        ["pattern"] = "^[A-Z]{3}$"
                    }
                },
                ["allowedOrganizerKindIds"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = 1,
                    ["maxItems"] = 3,
                    ["uniqueItems"] = true,
                    ["items"] = new JsonObject
                    {
                        ["enum"] = Integers([1, 2, 4])
                    }
                },
                ["currencyRiskLimits"] = new JsonObject
                {
                    ["type"] = "array",
                    ["maxItems"] = 64,
                    ["items"] = Ref("paidEventPolicyCurrencyRiskLimit")
                },
                ["defaultCurrencyCode"] = nullableCurrency,
                ["farFutureReviewThresholdDays"] = nullablePositiveInteger,
                ["isPaymentsEnabled"] = new JsonObject
                {
                    ["type"] = "boolean"
                },
                ["refundProtectionIds"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = 7,
                    ["maxItems"] = 7,
                    ["uniqueItems"] = true,
                    ["items"] = new JsonObject
                    {
                        ["enum"] = Integers([1, 2, 3, 4, 5, 6, 7])
                    }
                },
                ["requiresFirstPaidEventReview"] = new JsonObject
                {
                    ["type"] = "boolean"
                },
                ["requiresLocalVerification"] = new JsonObject
                {
                    ["type"] = "boolean"
                }
            },
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
    }

    private static JsonObject PaidEventPolicyCurrencyRiskLimitSchema()
    {
        JsonObject NullablePositiveInteger() => new()
        {
            ["type"] = Strings(["integer", "null"]),
            ["minimum"] = 1
        };

        return ClosedObject(
            new JsonObject
            {
                ["currencyCode"] = new JsonObject
                {
                    ["type"] = "string",
                    ["minLength"] = 3,
                    ["maxLength"] = 3,
                    ["pattern"] = "^[A-Z]{3}$"
                },
                ["highValueReviewThresholdMinor"] = NullablePositiveInteger(),
                ["perEventSalesCeilingMinor"] = NullablePositiveInteger(),
                ["perEventSalesCountCeiling"] = NullablePositiveInteger(),
                ["rollingOrganizerSalesCeilingMinor"] = NullablePositiveInteger(),
                ["rollingOrganizerSalesCountCeiling"] = NullablePositiveInteger(),
                ["rollingOrganizerWindowDays"] = NullablePositiveInteger()
            },
            ["currencyCode"]);
    }

    private static JsonObject BrandingPayloadSchema()
    {
        JsonObject nullableDisplayName = NullableString();
        nullableDisplayName["maxLength"] = 200;
        JsonObject nullableUrl = NullableString();
        nullableUrl["maxLength"] = 2_048;
        nullableUrl["format"] = "uri";
        nullableUrl["pattern"] = "^https://";

        return ClosedObject(
            new JsonObject
            {
                ["customCssUrl"] = nullableUrl.DeepClone(),
                ["displayName"] = nullableDisplayName,
                ["faviconUrl"] = nullableUrl.DeepClone(),
                ["logoUrl"] = nullableUrl
            },
            []);
    }

    private static JsonObject ClosedObject(
        JsonObject properties,
        IEnumerable<string> required) =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = Strings(required.Order(StringComparer.Ordinal)),
            ["properties"] = properties
        };

    private static JsonObject BoundedString(
        int maximumLength,
        string? pattern = null)
    {
        var schema = new JsonObject
        {
            ["type"] = "string",
            ["minLength"] = 1,
            ["maxLength"] = maximumLength
        };
        if (pattern is not null)
            schema["pattern"] = pattern;
        return schema;
    }

    private static JsonObject NullableString() =>
        new()
        {
            ["type"] = Strings(["null", "string"])
        };

    private static JsonObject Ref(string definition) =>
        new()
        {
            ["$ref"] = $"#/$defs/{definition}"
        };

    private static JsonArray Strings(IEnumerable<string> values)
    {
        var result = new JsonArray();
        foreach (string value in values)
            result.Add(value);
        return result;
    }

    private static JsonArray Integers(IEnumerable<int> values)
    {
        var result = new JsonArray();
        foreach (int value in values)
            result.Add(value);
        return result;
    }
}
