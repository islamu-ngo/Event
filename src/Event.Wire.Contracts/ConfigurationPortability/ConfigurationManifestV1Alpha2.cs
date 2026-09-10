namespace ISLAMU.Wire.Contracts.ConfigurationPortability;

using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

public static class ConfigurationManifestContractMetadata
{
    public const string SchemaId =
        "https://schemas.islamu.org/event/configuration-manifest/v1alpha2/schema.json";
    public const string ApiVersion = "configuration.islamu.org/v1alpha2";
    public const string Kind = "ConfigurationManifest";
    public const string MediaType =
        "application/vnd.islamu.configuration-manifest.v1alpha2+json";
}

public static class TenantConfigurationPackageContractMetadata
{
    public const string SchemaId =
        "https://schemas.islamu.org/event/tenant-configuration-package/v1alpha2/schema.json";
    public const string ApiVersion = "configuration.islamu.org/v1alpha2";
    public const string Kind = "TenantConfigurationPackage";
    public const string MediaType =
        "application/vnd.islamu.tenant-configuration-package.v1alpha2+json";
}

public static class ConfigurationPortabilityContentLimits
{
    public const int MaximumArtifactUtf8Bytes = 4 * 1024 * 1024;
    public const int MaximumJsonDepth = 32;
    public const int MaximumTenantCount = 256;
    public const int MaximumLegalDocumentsPerScope =
        LegalMarkdownContentLimits.MaximumDocumentsPerScope;
    public const int MaximumLegalLocalesPerDocument =
        LegalMarkdownContentLimits.MaximumLocalesPerDocument;
    public const int MaximumLegalMarkdownUtf8BytesPerLocale =
        LegalMarkdownContentLimits.MaximumMarkdownUtf8BytesPerLocale;
    public const int MaximumLegalLinksPerLocale =
        LegalMarkdownContentLimits.MaximumLinksPerLocale;
    public const int MaximumLegalPlaceholdersPerLocale =
        LegalMarkdownContentLimits.MaximumPlaceholdersPerLocale;
}

public static class ConfigurationManifestExportMetadataValues
{
    public const string OverridesView = "Overrides";
    public const string PortableView = "Portable";
    public const string InstanceAndTenantsAuthorityScope = "InstanceAndTenants";
    public const string TenantAuthorityScope = "Tenant";
}

public static class ConfigurationManifestDocumentKeys
{
    public const string InstancePaidEventPolicy = "instance.paid_event_policy";
    public const string TenantPaidEventPolicy = "tenant.paid_event_policy";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationManifestV1Alpha2
{
    [JsonPropertyName("$schema")]
    public required string Schema { get; init; }

    public required string ApiVersion { get; init; }

    public required string Kind { get; init; }

    public required ConfigurationManifestMetadataV1Alpha2 Metadata { get; init; }

    public required ConfigurationManifestSpecV1Alpha2 Spec { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationManifestMetadataV1Alpha2
{
    public required string Name { get; init; }

    public ConfigurationManifestExportMetadataV1Alpha2? Export { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationManifestExportMetadataV1Alpha2
{
    private IReadOnlyList<string> _sovereignLockedFields = Array.Empty<string>();

    public required string View { get; init; }

    public required bool EffectiveValuesFlattened { get; init; }

    public required bool SensitiveValuesOmitted { get; init; }

    public required string AuthorityScope { get; init; }

    public required bool SovereignValuesOmitted { get; init; }

    public required IReadOnlyList<string> SovereignLockedFields
    {
        get => _sovereignLockedFields;
        init => _sovereignLockedFields = Snapshot.List(value);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationManifestSpecV1Alpha2
{
    private IReadOnlyList<ConfigurationManifestTenantV1Alpha2> _tenants = Array.Empty<ConfigurationManifestTenantV1Alpha2>();

    public required ConfigurationManifestInstanceV1Alpha2 Instance { get; init; }

    public required IReadOnlyList<ConfigurationManifestTenantV1Alpha2> Tenants
    {
        get => _tenants;
        init => _tenants = Snapshot.List(value);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationManifestInstanceV1Alpha2
{
    private IReadOnlyDictionary<string, JsonElement> _settings = Snapshot.Dictionary<string, JsonElement>(null);
    private IReadOnlyDictionary<string, ConfigurationManifestDocumentV1Alpha2> _documents = Snapshot.Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(null);
    private IReadOnlyDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2> _legalDocuments = Snapshot.Dictionary<string, ConfigurationManifestLegalDocumentV1Alpha2>(null);

    public required IReadOnlyDictionary<string, JsonElement> Settings
    {
        get => _settings;
        init => _settings = Snapshot.Dictionary(value);
    }

    public required IReadOnlyDictionary<string, ConfigurationManifestDocumentV1Alpha2> Documents
    {
        get => _documents;
        init => _documents = Snapshot.Dictionary(value);
    }

    [JsonRequired]
    public IReadOnlyDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2> LegalDocuments
    {
        get => _legalDocuments;
        init => _legalDocuments = Snapshot.Dictionary(value);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationManifestTenantV1Alpha2
{
    public required ConfigurationManifestTenantMetadataV1Alpha2 Metadata { get; init; }

    public required ConfigurationManifestTenantSpecV1Alpha2 Spec { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationManifestTenantMetadataV1Alpha2
{
    public required string Name { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationManifestTenantSpecV1Alpha2
{
    private IReadOnlyDictionary<string, JsonElement> _settings = Snapshot.Dictionary<string, JsonElement>(null);
    private IReadOnlyDictionary<string, ConfigurationManifestDocumentV1Alpha2> _documents = Snapshot.Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(null);
    private IReadOnlyDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2> _legalDocuments = Snapshot.Dictionary<string, ConfigurationManifestLegalDocumentV1Alpha2>(null);

    public required string DisplayName { get; init; }

    public required IReadOnlyDictionary<string, JsonElement> Settings
    {
        get => _settings;
        init => _settings = Snapshot.Dictionary(value);
    }

    public required IReadOnlyDictionary<string, ConfigurationManifestDocumentV1Alpha2> Documents
    {
        get => _documents;
        init => _documents = Snapshot.Dictionary(value);
    }

    [JsonRequired]
    public IReadOnlyDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2> LegalDocuments
    {
        get => _legalDocuments;
        init => _legalDocuments = Snapshot.Dictionary(value);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationManifestDocumentV1Alpha2
{
    public required int SchemaVersion { get; init; }

    public required JsonElement Payload { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationManifestBrandingPayloadV1Alpha2
{
    public string? DisplayName { get; init; }

    public string? LogoUrl { get; init; }

    public string? FaviconUrl { get; init; }

    public string? CustomCssUrl { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationManifestPaidEventPolicyPayloadV1Alpha2
{
    private IReadOnlyList<int> _allowedOrganizerKindIds = Array.Empty<int>();
    private IReadOnlyList<string> _allowedCurrencyCodes = Array.Empty<string>();
    private IReadOnlyList<int> _refundProtectionIds = Array.Empty<int>();
    private IReadOnlyList<ConfigurationManifestPaidEventPolicyCurrencyRiskLimitV1Alpha2> _currencyRiskLimits = Array.Empty<ConfigurationManifestPaidEventPolicyCurrencyRiskLimitV1Alpha2>();

    public required bool IsPaymentsEnabled { get; init; }

    public required IReadOnlyList<int> AllowedOrganizerKindIds
    {
        get => _allowedOrganizerKindIds;
        init => _allowedOrganizerKindIds = Snapshot.List(value);
    }

    public required bool RequiresLocalVerification { get; init; }

    public required IReadOnlyList<string> AllowedCurrencyCodes
    {
        get => _allowedCurrencyCodes;
        init => _allowedCurrencyCodes = Snapshot.List(value);
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required string? DefaultCurrencyCode { get; init; }

    public required IReadOnlyList<int> RefundProtectionIds
    {
        get => _refundProtectionIds;
        init => _refundProtectionIds = Snapshot.List(value);
    }

    public required IReadOnlyList<ConfigurationManifestPaidEventPolicyCurrencyRiskLimitV1Alpha2> CurrencyRiskLimits
    {
        get => _currencyRiskLimits;
        init => _currencyRiskLimits = Snapshot.List(value);
    }

    public required bool RequiresFirstPaidEventReview { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required int? FarFutureReviewThresholdDays { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationManifestPaidEventPolicyCurrencyRiskLimitV1Alpha2
{
    public required string CurrencyCode { get; init; }

    public long? PerEventSalesCeilingMinor { get; init; }

    public int? PerEventSalesCountCeiling { get; init; }

    public long? RollingOrganizerSalesCeilingMinor { get; init; }

    public int? RollingOrganizerSalesCountCeiling { get; init; }

    public int? RollingOrganizerWindowDays { get; init; }

    public long? HighValueReviewThresholdMinor { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TenantConfigurationPackageV1Alpha2
{
    [JsonPropertyName("$schema")]
    public required string Schema { get; init; }

    public required string ApiVersion { get; init; }

    public required string Kind { get; init; }

    public required TenantConfigurationPackageMetadataV1Alpha2 Metadata { get; init; }

    public required TenantConfigurationPackageSpecV1Alpha2 Spec { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TenantConfigurationPackageMetadataV1Alpha2
{
    public required string Name { get; init; }

    public required TenantConfigurationPackageSourceV1Alpha2 Source { get; init; }

    public ConfigurationManifestExportMetadataV1Alpha2? Export { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TenantConfigurationPackageSourceV1Alpha2
{
    public required string TenantName { get; init; }

    public string? InstanceName { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TenantConfigurationPackageSpecV1Alpha2
{
    private IReadOnlyDictionary<string, JsonElement> _settings = Snapshot.Dictionary<string, JsonElement>(null);
    private IReadOnlyDictionary<string, ConfigurationManifestDocumentV1Alpha2> _documents = Snapshot.Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(null);
    private IReadOnlyDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2> _legalDocuments = Snapshot.Dictionary<string, ConfigurationManifestLegalDocumentV1Alpha2>(null);

    public required string DisplayName { get; init; }

    public required IReadOnlyDictionary<string, JsonElement> Settings
    {
        get => _settings;
        init => _settings = Snapshot.Dictionary(value);
    }

    public required IReadOnlyDictionary<string, ConfigurationManifestDocumentV1Alpha2> Documents
    {
        get => _documents;
        init => _documents = Snapshot.Dictionary(value);
    }

    [JsonRequired]
    public IReadOnlyDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2> LegalDocuments
    {
        get => _legalDocuments;
        init => _legalDocuments = Snapshot.Dictionary(value);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationManifestLegalDocumentV1Alpha2
{
    private IReadOnlyList<string> _jurisdictionAssumptions = Array.Empty<string>();
    private IReadOnlyList<ConfigurationManifestLegalDocumentLocalizedSourceV1Alpha2> _localizations = Array.Empty<ConfigurationManifestLegalDocumentLocalizedSourceV1Alpha2>();

    public required string Kind { get; init; }

    public required string Audience { get; init; }

    public required string LifecycleIntent { get; init; }

    public DateTime? ProposedEffectiveAt { get; init; }

    public bool RequiresFreshAcceptance { get; init; }

    public string? AccountableIdentityReference { get; init; }

    public string? ChangeSummary { get; init; }

    public ConfigurationManifestLegalTemplateProvenanceV1Alpha2?
        TemplateProvenance
    { get; init; }

    [JsonRequired]
    public IReadOnlyList<string> JurisdictionAssumptions
    {
        get => _jurisdictionAssumptions;
        init => _jurisdictionAssumptions = Snapshot.List(value);
    }

    public required IReadOnlyList<ConfigurationManifestLegalDocumentLocalizedSourceV1Alpha2> Localizations
    {
        get => _localizations;
        init => _localizations = Snapshot.List(value);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationManifestLegalDocumentLocalizedSourceV1Alpha2
{
    public required string LanguageTag { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required string Markdown { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigurationManifestLegalTemplateProvenanceV1Alpha2
{
    public required string TemplateId { get; init; }

    public required string TemplateVersion { get; init; }

    public required string SourceKind { get; init; }

    public required string LicenseExpression { get; init; }

    public required string ReviewReference { get; init; }
}

public enum ConfigurationImportApplyMode
{
    PreviewOnly,
    CreateNew,
    MergeMissing,
    ApplySelected,
    ReplacePortableConfiguration,
    ReconcileManaged
}

internal static class Snapshot
{
    internal static IReadOnlyDictionary<TKey, TValue> Dictionary<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue>? values) where TKey : notnull =>
        new ReadOnlyDictionary<TKey, TValue>(
            values is null
                ? new Dictionary<TKey, TValue>()
                : new Dictionary<TKey, TValue>(values));

    internal static IReadOnlyList<T> List<T>(IEnumerable<T>? values) =>
        values is null ? null! : Array.AsReadOnly(values.ToArray());
}
