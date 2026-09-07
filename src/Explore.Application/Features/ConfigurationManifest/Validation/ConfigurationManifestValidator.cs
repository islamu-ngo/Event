namespace Explore.Application.Features.ConfigurationManifest.Validation;

using System.Globalization;
using System.Text.Json;
using Explore.Application.DTOs.PaidEventPolicies;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Compilation;
using Explore.Application.Features.ConfigurationManifest.Preflight;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Domain.Settings.Documents;

public static class ConfigurationManifestValidator
{
    public const int MaximumTenantCount = 256;

    private static readonly IReadOnlySet<string> ExplicitSensitiveKeys =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "analytics.api_key",
            "auth.google_client_secret",
            "auth.keycloak_client_secret",
            "email.smtp_password",
            "localization.tms_api_key",
            "management.control_plane_registration_credentials"
        };

    private static readonly IReadOnlySet<string> BrandingProperties =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "displayName",
            "logoUrl",
            "faviconUrl",
            "customCssUrl"
        };

    public static ConfigurationManifestValidationResult Validate(
        ConfigurationManifestV1Alpha2 manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var errors = new List<ConfigurationManifestValidationError>();
        ValidateEnvelope(manifest, errors);
        if (manifest.Spec is null)
        {
            AddContractError(errors, "$.spec");
            return new ConfigurationManifestValidationResult(errors.AsReadOnly());
        }

        ConfigurationManifestPaidEventPolicyPayloadV1Alpha2? proposedInstancePolicy = null;
        if (manifest.Spec.Instance is null)
        {
            AddContractError(errors, "$.spec.instance");
        }
        else
        {
            proposedInstancePolicy = ValidateInstance(manifest.Spec.Instance, errors);
        }

        if (manifest.Spec.Tenants is null)
        {
            AddContractError(errors, "$.spec.tenants");
        }
        else
        {
            ValidateTenants(manifest.Spec.Tenants, proposedInstancePolicy, errors);
        }

        return new ConfigurationManifestValidationResult(errors.AsReadOnly());
    }

    public static ConfigurationManifestValidationResult Validate(
        TenantConfigurationPackageV1Alpha2 package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var errors = new List<ConfigurationManifestValidationError>();
        if (!string.Equals(
                package.Schema,
                TenantConfigurationPackageContractMetadata.SchemaId,
                StringComparison.Ordinal))
        {
            AddContractError(errors, "$.$schema");
        }
        if (!string.Equals(
                package.ApiVersion,
                TenantConfigurationPackageContractMetadata.ApiVersion,
                StringComparison.Ordinal))
        {
            AddContractError(errors, "$.apiVersion");
        }
        if (!string.Equals(
                package.Kind,
                TenantConfigurationPackageContractMetadata.Kind,
                StringComparison.Ordinal))
        {
            AddContractError(errors, "$.kind");
        }
        if (package.Metadata is null
            || !IsMachineName(package.Metadata.Name, 100, allowDot: true)
            || package.Metadata.Source is null
            || !IsMachineName(
                package.Metadata.Source.TenantName,
                100,
                allowDot: false)
            || package.Metadata.Source.InstanceName is { } instanceName
                && !IsMachineName(instanceName, 100, allowDot: true))
        {
            AddContractError(errors, "$.metadata");
        }
        else
        {
            ValidateTenantPackageExport(package.Metadata.Export, errors);
        }

        if (package.Spec is null)
        {
            AddContractError(errors, "$.spec");
            return new ConfigurationManifestValidationResult(errors.AsReadOnly());
        }

        if (string.IsNullOrWhiteSpace(package.Spec.DisplayName)
            || package.Spec.DisplayName.Length > 500)
        {
            AddContractError(errors, "$.spec.displayName");
        }
        if (package.Spec.Settings is null)
        {
            AddContractError(errors, "$.spec.settings");
        }
        else
        {
            ValidateTenantSettings(package.Spec.Settings, "$.spec.settings", errors);
            ValidatePublicationPolicy(package.Spec.Settings, "$.spec.settings", errors);
        }
        if (package.Spec.Documents is null)
        {
            AddContractError(errors, "$.spec.documents");
        }
        else
        {
            ValidateDocuments(package.Spec.Documents, "$.spec.documents", errors);
        }
        if (package.Spec.LegalDocuments is null)
        {
            AddContractError(errors, "$.spec.legalDocuments");
        }
        else
        {
            ValidateLegalDocuments(
                package.Spec.LegalDocuments,
                LegalDocumentScope.Tenant,
                "$.spec.legalDocuments",
                errors);
        }

        return new ConfigurationManifestValidationResult(errors.AsReadOnly());
    }

    private static void ValidateTenantPackageExport(
        ConfigurationManifestExportMetadataV1Alpha2? export,
        List<ConfigurationManifestValidationError> errors)
    {
        if (export is null)
            return;

        bool isOverrides = string.Equals(
            export.View,
            ConfigurationManifestExportMetadataValues.OverridesView,
            StringComparison.Ordinal);
        bool isPortable = string.Equals(
            export.View,
            ConfigurationManifestExportMetadataValues.PortableView,
            StringComparison.Ordinal);
        if ((!isOverrides && !isPortable)
            || export.EffectiveValuesFlattened != isPortable
            || !export.SensitiveValuesOmitted
            || !string.Equals(
                export.AuthorityScope,
                ConfigurationManifestExportMetadataValues.TenantAuthorityScope,
                StringComparison.Ordinal)
            || !export.SovereignValuesOmitted
            || export.SovereignLockedFields is null
            || !export.SovereignLockedFields.SequenceEqual(
                PaidEventPolicyAuthorityMetadata.SovereignLockedFields,
                StringComparer.Ordinal))
        {
            AddContractError(errors, "$.metadata.export");
        }
    }

    private static void ValidateEnvelope(
        ConfigurationManifestV1Alpha2 manifest,
        List<ConfigurationManifestValidationError> errors)
    {
        if (!string.Equals(
                manifest.Schema,
                ConfigurationManifestContractMetadata.SchemaId,
                StringComparison.Ordinal))
        {
            AddContractError(errors, "$.$schema");
        }

        if (!string.Equals(
                manifest.ApiVersion,
                ConfigurationManifestContractMetadata.ApiVersion,
                StringComparison.Ordinal))
        {
            AddContractError(errors, "$.apiVersion");
        }

        if (!string.Equals(
                manifest.Kind,
                ConfigurationManifestContractMetadata.Kind,
                StringComparison.Ordinal))
        {
            AddContractError(errors, "$.kind");
        }

        if (manifest.Metadata is null)
        {
            AddContractError(errors, "$.metadata");
            return;
        }

        if (!IsMachineName(manifest.Metadata.Name, 100, allowDot: true))
            AddContractError(errors, "$.metadata.name");

        ConfigurationManifestExportMetadataV1Alpha2? export = manifest.Metadata.Export;
        if (export is null)
            return;

        bool isOverrides = string.Equals(
            export.View,
            ConfigurationManifestExportMetadataValues.OverridesView,
            StringComparison.Ordinal);
        bool isPortable = string.Equals(
            export.View,
            ConfigurationManifestExportMetadataValues.PortableView,
            StringComparison.Ordinal);
        if ((!isOverrides && !isPortable)
            || export.EffectiveValuesFlattened != isPortable
            || !export.SensitiveValuesOmitted
            || !string.Equals(
                    export.AuthorityScope,
                    ConfigurationManifestExportMetadataValues
                        .InstanceAndTenantsAuthorityScope,
                    StringComparison.Ordinal)
            || !export.SovereignValuesOmitted
            || export.SovereignLockedFields is null
            || !export.SovereignLockedFields.SequenceEqual(
                PaidEventPolicyAuthorityMetadata.SovereignLockedFields,
                StringComparer.Ordinal))
        {
            AddContractError(errors, "$.metadata.export");
        }
    }

    private static ConfigurationManifestPaidEventPolicyPayloadV1Alpha2?
        ValidateInstance(
        ConfigurationManifestInstanceV1Alpha2 instance,
        List<ConfigurationManifestValidationError> errors)
    {
        if (instance.Settings is null)
        {
            AddContractError(errors, "$.spec.instance.settings");
        }
        else
        {
            ValidateInstanceSettings(
                instance.Settings,
                "$.spec.instance.settings",
                errors);
        }

        if (instance.Documents is null)
        {
            AddContractError(errors, "$.spec.instance.documents");
            return null;
        }

        if (instance.LegalDocuments is null)
        {
            AddContractError(errors, "$.spec.instance.legalDocuments");
        }
        else
        {
            ValidateLegalDocuments(
                instance.LegalDocuments,
                LegalDocumentScope.Instance,
                "$.spec.instance.legalDocuments",
                errors);
        }

        return ValidateInstanceDocuments(
            instance.Documents,
            "$.spec.instance.documents",
            errors);
    }

    private static void ValidateTenants(
        IReadOnlyList<ConfigurationManifestTenantV1Alpha2> tenants,
        ConfigurationManifestPaidEventPolicyPayloadV1Alpha2? proposedInstancePolicy,
        List<ConfigurationManifestValidationError> errors)
    {
        if (tenants.Count is < 1 or > MaximumTenantCount)
        {
            AddContractError(errors, "$.spec.tenants");
            return;
        }

        var slugs = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < tenants.Count; index++)
        {
            ConfigurationManifestTenantV1Alpha2? tenant = tenants[index];
            string tenantPath = $"$.spec.tenants[{index}]";
            if (tenant is null)
            {
                AddContractError(errors, tenantPath);
                continue;
            }

            if (tenant.Metadata is null)
            {
                AddContractError(errors, $"{tenantPath}.metadata");
            }
            else if (!IsMachineName(tenant.Metadata.Name, 100, allowDot: false))
            {
                AddContractError(errors, $"{tenantPath}.metadata.name");
            }
            else if (!slugs.Add(tenant.Metadata.Name))
            {
                errors.Add(new ConfigurationManifestValidationError(
                    ConfigurationManifestFailureCodes.TenantDuplicate,
                    $"{tenantPath}.metadata.name",
                    "Configuration-manifest tenant names must be unique."));
            }

            if (tenant.Spec is null)
            {
                AddContractError(errors, $"{tenantPath}.spec");
                continue;
            }

            if (string.IsNullOrWhiteSpace(tenant.Spec.DisplayName)
                || tenant.Spec.DisplayName.Length > 500)
            {
                AddContractError(errors, $"{tenantPath}.spec.displayName");
            }

            if (tenant.Spec.Settings is null)
            {
                AddContractError(errors, $"{tenantPath}.spec.settings");
            }
            else
            {
                ValidateTenantSettings(
                    tenant.Spec.Settings,
                    $"{tenantPath}.spec.settings",
                    errors);
                ValidatePublicationPolicy(
                    tenant.Spec.Settings,
                    $"{tenantPath}.spec.settings",
                    errors);
            }

            if (tenant.Spec.Documents is null)
            {
                AddContractError(errors, $"{tenantPath}.spec.documents");
            }
            else
            {
                ValidateDocuments(tenant.Spec.Documents, $"{tenantPath}.spec.documents", errors);
                ValidateTenantPaidEventPolicyNarrowing(
                    tenant,
                    proposedInstancePolicy,
                    $"{tenantPath}.spec.documents",
                    errors);
            }

            if (tenant.Spec.LegalDocuments is null)
            {
                AddContractError(errors, $"{tenantPath}.spec.legalDocuments");
            }
            else
            {
                ValidateLegalDocuments(
                    tenant.Spec.LegalDocuments,
                    LegalDocumentScope.Tenant,
                    $"{tenantPath}.spec.legalDocuments",
                    errors);
            }
        }
    }

    private static void ValidateLegalDocuments(
        IReadOnlyDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2>
            documents,
        LegalDocumentScope scope,
        string path,
        List<ConfigurationManifestValidationError> errors)
    {
        if (documents.Count > ISLAMU.Wire.Contracts.ConfigurationPortability.LegalMarkdownContentLimits.MaximumDocumentsPerScope)
        {
            AddLegalDocumentError(errors, path);
            return;
        }

        foreach ((
                     string key,
                     ConfigurationManifestLegalDocumentV1Alpha2? document)
                 in documents)
        {
            string documentPath = $"{path}.{key}";
            if (document is null
                || !Enum.TryParse(
                    document.Kind,
                    ignoreCase: false,
                    out LegalDocumentKind kind)
                || !Enum.IsDefined(kind)
                || !string.Equals(key, document.Kind, StringComparison.Ordinal)
                || LegalDocumentKindCatalog.Get(kind).Scope != scope
                || !Enum.TryParse(
                    document.Audience,
                    ignoreCase: false,
                    out LegalDocumentAudience _)
                || document.LifecycleIntent is not (
                    "Draft" or "ReviewRequired" or "ProposedPublication")
                || document.ProposedEffectiveAt is { Kind: not DateTimeKind.Utc }
                || document.AccountableIdentityReference?.Length > 200
                || document.ChangeSummary?.Length > 500
                || document.JurisdictionAssumptions is null
                || document.JurisdictionAssumptions.Count > 16
                || document.JurisdictionAssumptions.Any(value =>
                    string.IsNullOrWhiteSpace(value) || value.Length > 100)
                || document.Localizations is null
                || document.Localizations.Count is < 1 or >
                    ISLAMU.Wire.Contracts.ConfigurationPortability.LegalMarkdownContentLimits.MaximumLocalesPerDocument
                || document.Localizations.Any(localization =>
                    localization is null))
            {
                AddLegalDocumentError(errors, documentPath);
                continue;
            }

            try
            {
                string[] languages = document.Localizations
                    .Select(localization => LegalDocumentLocalizedSource.Create(
                        localization.LanguageTag,
                        localization.Title,
                        localization.Summary,
                        localization.Markdown).LanguageTag)
                    .ToArray();
                if (languages.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                    != languages.Length)
                {
                    throw new ArgumentException(
                        "Legal document locales must be unique.");
                }

                if (document.TemplateProvenance is { } provenance)
                {
                    if (!Enum.TryParse(
                            provenance.SourceKind,
                            ignoreCase: false,
                            out LegalDocumentTemplateSourceKind sourceKind))
                    {
                        throw new ArgumentException(
                            "Legal template source kind is invalid.");
                    }

                    _ = LegalDocumentTemplateProvenance.Create(
                        provenance.TemplateId,
                        provenance.TemplateVersion,
                        sourceKind,
                        provenance.LicenseExpression,
                        provenance.ReviewReference);
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or InvalidOperationException)
            {
                AddLegalDocumentError(errors, documentPath);
            }
        }
    }

    private static void AddLegalDocumentError(
        List<ConfigurationManifestValidationError> errors,
        string path) =>
        errors.Add(new ConfigurationManifestValidationError(
            ConfigurationManifestFailureCodes.LegalDocumentInvalid,
            path,
            "The portable legal document source is invalid."));

    private static ConfigurationManifestPaidEventPolicyPayloadV1Alpha2?
        ValidateInstanceDocuments(
        IReadOnlyDictionary<string, ConfigurationManifestDocumentV1Alpha2> documents,
        string path,
        List<ConfigurationManifestValidationError> errors)
    {
        if (documents.Count > ConfigurationManifestCatalog.InstanceDocuments.Count)
        {
            AddContractError(errors, path);
            return null;
        }

        ConfigurationManifestPaidEventPolicyPayloadV1Alpha2? paidEventPolicy = null;
        foreach ((string key, ConfigurationManifestDocumentV1Alpha2 document) in documents)
        {
            string documentPath = $"{path}.{key}";
            if (!ConfigurationManifestCatalog.TryGetInstanceDocument(
                    key,
                    out ConfigurationManifestDocumentCatalogEntry? catalogEntry)
                || catalogEntry is null)
            {
                errors.Add(new ConfigurationManifestValidationError(
                    ConfigurationManifestFailureCodes.DocumentInvalid,
                    documentPath,
                    "The instance document key is not allowed in this manifest version."));
                continue;
            }

            if (document is null
                || document.SchemaVersion != catalogEntry.SchemaVersion
                || !IsValidPaidEventPolicyPayload(document.Payload))
            {
                errors.Add(new ConfigurationManifestValidationError(
                    ConfigurationManifestFailureCodes.DocumentInvalid,
                    documentPath,
                    "The typed instance document is invalid."));
                continue;
            }

            paidEventPolicy = DeserializePaidEventPolicyPayload(document.Payload);
        }

        return paidEventPolicy;
    }

    private static void ValidateTenantPaidEventPolicyNarrowing(
        ConfigurationManifestTenantV1Alpha2 tenant,
        ConfigurationManifestPaidEventPolicyPayloadV1Alpha2? proposedInstancePolicy,
        string documentsPath,
        List<ConfigurationManifestValidationError> errors)
    {
        if (proposedInstancePolicy is null
            || !tenant.Spec.Documents.TryGetValue(
                ConfigurationManifestDocumentKeys.TenantPaidEventPolicy,
                out ConfigurationManifestDocumentV1Alpha2? document)
            || document is null
            || !IsValidPaidEventPolicyPayload(document.Payload))
        {
            return;
        }

        try
        {
            PaidEventPolicyVersion instanceCandidate =
                ConfigurationManifestPaidEventPolicyMapper.CreateInstanceCandidate(
                    proposedInstancePolicy);
            ConfigurationManifestPaidEventPolicyPayloadV1Alpha2 tenantPolicy =
                DeserializePaidEventPolicyPayload(document.Payload);
            PaidEventPolicyVersion tenantCandidate =
                ConfigurationManifestPaidEventPolicyMapper.CreateTenantCandidate(
                    Guid.CreateVersion7(),
                    tenantPolicy);
            PaidEventPolicyRules.ValidateTenantPolicy(
                instanceCandidate,
                tenantCandidate);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            errors.Add(new ConfigurationManifestValidationError(
                ConfigurationManifestFailureCodes.CrossReferenceInvalid,
                $"{documentsPath}.{ConfigurationManifestDocumentKeys.TenantPaidEventPolicy}",
                "The tenant paid-event policy exceeds the proposed instance policy.",
                ConfigurationManifestApplicationFailureCodes
                    .PaidPolicyBroadening));
        }
    }

    private static void ValidateInstanceSettings(
        IReadOnlyDictionary<string, JsonElement> settings,
        string path,
        List<ConfigurationManifestValidationError> errors)
    {
        if (settings.Count > ConfigurationManifestCatalog.InstanceSettings.Count)
        {
            AddContractError(errors, path);
            return;
        }

        foreach ((string key, JsonElement value) in settings)
        {
            string settingPath = $"{path}.{key}";
            SettingDefinition? registered = SettingRegistry.Get(key);
            if (ExplicitSensitiveKeys.Contains(key) || registered?.IsSensitive == true)
            {
                errors.Add(new ConfigurationManifestValidationError(
                    ConfigurationManifestFailureCodes.SensitiveKeyForbidden,
                    settingPath,
                    "Sensitive settings are not accepted in configuration manifests."));
                continue;
            }

            if (!ConfigurationManifestCatalog.TryGetInstanceSetting(
                    key,
                    out ConfigurationManifestSettingCatalogEntry? catalogEntry)
                || catalogEntry is null)
            {
                string? reasonCode = ConfigurationManifestCatalog.TryGetTenantSetting(
                    key,
                    out _)
                    ? ConfigurationManifestValidationReasonCodes
                        .SettingScopeInvalid
                    : null;
                errors.Add(new ConfigurationManifestValidationError(
                    ConfigurationManifestFailureCodes.KeyNotAllowed,
                    settingPath,
                    "The setting key is not allowed in this manifest scope and version.",
                    reasonCode));
                continue;
            }

            if (!IsValidValue(value, catalogEntry))
            {
                errors.Add(new ConfigurationManifestValidationError(
                    ConfigurationManifestFailureCodes.ValueInvalid,
                    settingPath,
                    "The setting value does not satisfy its declared type and constraints."));
            }
        }
    }

    private static void ValidateTenantSettings(
        IReadOnlyDictionary<string, JsonElement> settings,
        string path,
        List<ConfigurationManifestValidationError> errors)
    {
        if (settings.Count > ConfigurationManifestCatalog.TenantSettings.Count)
        {
            AddContractError(errors, path);
            return;
        }

        foreach ((string key, JsonElement value) in settings)
        {
            string settingPath = $"{path}.{key}";
            SettingDefinition? registered = SettingRegistry.Get(key);
            if (ExplicitSensitiveKeys.Contains(key) || registered?.IsSensitive == true)
            {
                errors.Add(new ConfigurationManifestValidationError(
                    ConfigurationManifestFailureCodes.SensitiveKeyForbidden,
                    settingPath,
                    "Sensitive settings are not accepted in configuration manifests."));
                continue;
            }

            if (!ConfigurationManifestCatalog.TryGetTenantSetting(
                    key,
                    out ConfigurationManifestSettingCatalogEntry? catalogEntry)
                || catalogEntry is null)
            {
                string? reasonCode = ConfigurationManifestCatalog.TryGetInstanceSetting(
                    key,
                    out _)
                    ? ConfigurationManifestValidationReasonCodes
                        .SettingScopeInvalid
                    : null;
                errors.Add(new ConfigurationManifestValidationError(
                    ConfigurationManifestFailureCodes.KeyNotAllowed,
                    settingPath,
                    "The setting key is not allowed in this manifest scope and version.",
                    reasonCode));
                continue;
            }

            if (!IsValidValue(value, catalogEntry))
            {
                errors.Add(new ConfigurationManifestValidationError(
                    ConfigurationManifestFailureCodes.ValueInvalid,
                    settingPath,
                    "The setting value does not satisfy its declared type and constraints."));
            }
        }
    }

    private static bool IsValidValue(
        JsonElement value,
        ConfigurationManifestSettingCatalogEntry entry)
    {
        SettingDefinition definition = entry.Definition;
        bool typeAndConstraintValid = definition.ValueType switch
        {
            SettingValueType.String => IsValidString(value, definition, entry.MaximumStringLength),
            SettingValueType.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
            SettingValueType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            SettingValueType.Decimal => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
            SettingValueType.Json => value.ValueKind is JsonValueKind.Object or JsonValueKind.Array,
            SettingValueType.DateTime => value.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(
                    value.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out _),
            SettingValueType.Long => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            _ => false
        };
        if (!typeAndConstraintValid)
            return false;

        bool isBrandingUrl =
            string.Equals(
                definition.Key,
                BrandingSettingDefinitions.LogoUrl.Key,
                StringComparison.Ordinal)
            || string.Equals(
                definition.Key,
                BrandingSettingDefinitions.FaviconUrl.Key,
                StringComparison.Ordinal)
            || string.Equals(
                definition.Key,
                BrandingSettingDefinitions.CustomCssUrl.Key,
                StringComparison.Ordinal);
        return !isBrandingUrl || IsOptionalHttpsUrl(value.GetString()!);
    }

    private static bool IsValidString(
        JsonElement value,
        SettingDefinition definition,
        int? maximumLength)
    {
        if (value.ValueKind != JsonValueKind.String)
            return false;

        string text = value.GetString()!;
        if (maximumLength.HasValue && text.Length > maximumLength.Value)
            return false;

        return definition.AllowedValues is null
            || definition.AllowedValues.Contains(text, StringComparer.Ordinal);
    }

    private static void ValidateDocuments(
        IReadOnlyDictionary<string, ConfigurationManifestDocumentV1Alpha2> documents,
        string path,
        List<ConfigurationManifestValidationError> errors)
    {
        if (documents.Count > ConfigurationManifestCatalog.TenantDocuments.Count)
        {
            AddContractError(errors, path);
            return;
        }

        foreach ((string key, ConfigurationManifestDocumentV1Alpha2 document) in documents)
        {
            string documentPath = $"{path}['{key}']";
            if (!ConfigurationManifestCatalog.TryGetTenantDocument(
                    key,
                    out ConfigurationManifestDocumentCatalogEntry? catalogEntry)
                || catalogEntry is null)
            {
                errors.Add(new ConfigurationManifestValidationError(
                    ConfigurationManifestFailureCodes.KeyNotAllowed,
                    documentPath,
                    "The document key is not allowed in this manifest version."));
                continue;
            }

            if (document is null
                || document.SchemaVersion != catalogEntry.SchemaVersion
                || !IsValidDocumentPayload(key, document.Payload))
            {
                errors.Add(new ConfigurationManifestValidationError(
                    ConfigurationManifestFailureCodes.DocumentInvalid,
                    documentPath,
                    "The typed settings document is invalid."));
            }
        }
    }

    private static bool IsValidDocumentPayload(string key, JsonElement payload) =>
        key switch
        {
            SettingsDocumentKeys.Tenant.Branding => IsValidBrandingPayload(payload),
            ConfigurationManifestDocumentKeys.TenantPaidEventPolicy =>
                IsValidPaidEventPolicyPayload(payload),
            _ => false
        };

    private static bool IsValidBrandingPayload(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return false;

        foreach (JsonProperty property in payload.EnumerateObject())
        {
            if (!BrandingProperties.Contains(property.Name)
                || property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                return false;
            }

            if (property.Value.ValueKind == JsonValueKind.Null)
                continue;

            string value = property.Value.GetString()!;
            if (property.Name == "displayName")
            {
                if (value.Length > 200)
                    return false;
                continue;
            }

            if (value.Length > 2048 || !IsOptionalHttpsUrl(value))
                return false;
        }

        return true;
    }

    private static bool IsOptionalHttpsUrl(string value)
    {
        if (string.IsNullOrEmpty(value))
            return true;

        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }

    private static bool IsValidPaidEventPolicyPayload(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return false;

        try
        {
            ConfigurationManifestPaidEventPolicyPayloadV1Alpha2? policy =
                DeserializePaidEventPolicyPayload(payload);
            if (policy is null
                || policy.AllowedOrganizerKindIds is null
                || policy.AllowedCurrencyCodes is null
                || policy.RefundProtectionIds is null
                || policy.CurrencyRiskLimits is null
                || policy.CurrencyRiskLimits.Any(limit => limit is null)
                || policy.AllowedOrganizerKindIds.Count is < 1 or > 3
                || policy.AllowedOrganizerKindIds.Distinct().Count()
                    != policy.AllowedOrganizerKindIds.Count
                || policy.AllowedCurrencyCodes.Count is < 1 or > 64
                || policy.AllowedCurrencyCodes.Distinct(StringComparer.Ordinal).Count()
                    != policy.AllowedCurrencyCodes.Count
                || policy.RefundProtectionIds.Count is < 1 or > 16
                || policy.RefundProtectionIds.Distinct().Count()
                    != policy.RefundProtectionIds.Count
                || policy.CurrencyRiskLimits.Count > 64)
            {
                return false;
            }

            ActorTypeEnum[] organizerKinds = policy.AllowedOrganizerKindIds
                .Select(id => (ActorTypeEnum)id)
                .ToArray();
            PaidEventRefundProtection[] refundProtections = policy.RefundProtectionIds
                .Select(id => (PaidEventRefundProtection)id)
                .ToArray();
            PaidEventPolicyCurrencyRiskLimit[] riskLimits = policy.CurrencyRiskLimits
                .Select(limit => PaidEventPolicyCurrencyRiskLimit.Create(
                    limit.CurrencyCode,
                    limit.PerEventSalesCeilingMinor,
                    limit.PerEventSalesCountCeiling,
                    limit.RollingOrganizerSalesCeilingMinor,
                    limit.RollingOrganizerSalesCountCeiling,
                    limit.RollingOrganizerWindowDays,
                    limit.HighValueReviewThresholdMinor))
                .ToArray();
            PaidEventPolicyVersion normalized = PaidEventPolicyVersion.CreateTenant(
                Guid.CreateVersion7(),
                policy.IsPaymentsEnabled,
                organizerKinds,
                policy.RequiresLocalVerification,
                policy.AllowedCurrencyCodes,
                policy.DefaultCurrencyCode,
                refundProtections,
                riskLimits,
                policy.RequiresFirstPaidEventReview,
                policy.FarFutureReviewThresholdDays);

            return normalized.AllowedOrganizerKinds.SequenceEqual(organizerKinds)
                && normalized.AllowedCurrencyCodes.SequenceEqual(
                    policy.AllowedCurrencyCodes,
                    StringComparer.Ordinal)
                && string.Equals(
                    normalized.DefaultCurrencyCode,
                    policy.DefaultCurrencyCode,
                    StringComparison.Ordinal)
                && normalized.RefundProtections.SequenceEqual(refundProtections)
                && normalized.CurrencyRiskLimits.Select(limit => limit.CurrencyCode)
                    .SequenceEqual(
                        policy.CurrencyRiskLimits.Select(limit => limit.CurrencyCode),
                        StringComparer.Ordinal);
        }
        catch (Exception exception) when (
            exception is JsonException
                or ArgumentException
                or InvalidOperationException
                or OverflowException)
        {
            return false;
        }
    }

    private static ConfigurationManifestPaidEventPolicyPayloadV1Alpha2
        DeserializePaidEventPolicyPayload(JsonElement payload) =>
        payload.Deserialize(
            ConfigurationPortabilityJsonContext.Default
                .ConfigurationManifestPaidEventPolicyPayloadV1Alpha2)
        ?? throw new JsonException("The paid-event policy payload is required.");

    private static void ValidatePublicationPolicy(
        IReadOnlyDictionary<string, JsonElement> settings,
        string path,
        List<ConfigurationManifestValidationError> errors)
    {
        if (!TryBoolean(
                settings,
                PublicationPolicySettingKeys.All[0],
                out bool intakeEnabled)
            || !TryBoolean(settings, PublicationPolicySettingKeys.All[1], out bool requireApproval)
            || !TryBoolean(settings, PublicationPolicySettingKeys.All[2], out bool userSubmissionEnabled)
            || !TryBoolean(settings, PublicationPolicySettingKeys.All[3], out bool organizationSubmissionEnabled)
            || !TryBoolean(settings, PublicationPolicySettingKeys.All[4], out bool groupSubmissionEnabled))
        {
            return;
        }

        ReportingIntakePolicyEvaluation evaluation = ReportingIntakePolicyEvaluator.Evaluate(
            new ReportingIntakePolicyState(
                intakeEnabled,
                requireApproval,
                userSubmissionEnabled,
                organizationSubmissionEnabled,
                groupSubmissionEnabled));
        if (evaluation.Allowed)
            return;

        errors.Add(new ConfigurationManifestValidationError(
            ConfigurationManifestFailureCodes.CrossReferenceInvalid,
            path,
            evaluation.Message,
            evaluation.ReasonCode));
    }

    private static bool TryBoolean(
        IReadOnlyDictionary<string, JsonElement> settings,
        string key,
        out bool value)
    {
        if (settings.TryGetValue(key, out JsonElement supplied))
        {
            if (supplied.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                value = supplied.GetBoolean();
                return true;
            }

            value = default;
            return false;
        }

        SettingDefinition definition = SettingRegistry.Get(key)
            ?? throw new InvalidOperationException("A guarded publication-policy definition is missing.");
        using JsonDocument defaultValue = JsonDocument.Parse(definition.DefaultValue);
        value = defaultValue.RootElement.GetBoolean();
        return true;
    }

    private static bool IsMachineName(string value, int maximumLength, bool allowDot)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > maximumLength
            || !IsAsciiLowerOrDigit(value[0])
            || !IsAsciiLowerOrDigit(value[^1]))
        {
            return false;
        }

        bool previousWasSeparator = false;
        foreach (char character in value)
        {
            if (IsAsciiLowerOrDigit(character))
            {
                previousWasSeparator = false;
                continue;
            }

            bool isSeparator = character == '-' || (allowDot && character == '.');
            if (!isSeparator || previousWasSeparator)
                return false;

            previousWasSeparator = true;
        }

        return true;
    }

    private static bool IsAsciiLowerOrDigit(char character) =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static void AddContractError(
        List<ConfigurationManifestValidationError> errors,
        string path) =>
        errors.Add(new ConfigurationManifestValidationError(
            ConfigurationManifestFailureCodes.ContractInvalid,
            path,
            "The configuration manifest contract is invalid."));
}
