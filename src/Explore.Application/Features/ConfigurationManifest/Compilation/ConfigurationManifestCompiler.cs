namespace Explore.Application.Features.ConfigurationManifest.Compilation;

using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Explore.Application.Features.ConfigurationManifest.Validation;
using Explore.Domain;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;

public static class ConfigurationManifestCompiler
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public static ConfigurationManifestApplyPlan Compile(
        ConfigurationManifestReadResult source,
        Guid operationId,
        DateTime occurredAt)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Mode == ConfigurationManifestMode.Off)
        {
            throw ConfigurationManifestCompilationException.ModeInvalid();
        }

        if (operationId == Guid.Empty || operationId.Version != 7)
        {
            throw new ArgumentException("Manifest operation identity must be UUIDv7.", nameof(operationId));
        }

        if (occurredAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Manifest compilation timestamp must use UTC kind.", nameof(occurredAt));
        }

        ConfigurationManifestValidationResult validation =
            ConfigurationManifestValidator.Validate(source.Manifest);
        if (!validation.IsValid)
        {
            throw new ConfigurationManifestCompilationException(validation.Errors);
        }

        ValidateSourceIdentity(source);
        ConfigurationManifestPaidEventPolicyPayloadV1Alpha2?
            proposedInstancePaidEventPolicy =
            CompilePaidEventPolicy(
                source.Manifest.Spec.Instance.Documents,
                ConfigurationManifestDocumentKeys.InstancePaidEventPolicy);
        ConfigurationManifestSettingWrite[] instanceSettings =
            source.Manifest.Spec.Instance.Settings
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ConfigurationManifestSettingWrite(
                    pair.Key,
                    pair.Value.GetRawText()))
                .ToArray();
        ImmutableArray<ConfigurationManifestSettingWrite>
            guardedInstanceSettings = instanceSettings
                .Where(setting =>
                    ConfigurationManifestCatalog.InstanceSettings[setting.Key]
                        .Definition.RequiresCoordinatedMutation)
                .ToImmutableArray();
        ImmutableArray<ConfigurationManifestSettingWrite>
            unguardedInstanceSettings = instanceSettings
                .Where(setting =>
                    !ConfigurationManifestCatalog.InstanceSettings[setting.Key]
                        .Definition.RequiresCoordinatedMutation)
                .ToImmutableArray();
        ImmutableArray<ConfigurationManifestTenantPlan> tenants = source.Manifest.Spec.Tenants
            .Select((tenant, index) => CompileTenant(tenant, index))
            .OrderBy(tenant => tenant.Slug, StringComparer.Ordinal)
            .ToImmutableArray();
        ConfigurationManifestInstancePaidEventPolicyPlan?
            instancePaidEventPolicy =
                proposedInstancePaidEventPolicy is not null
                || tenants.Any(tenant => tenant.PaidEventPolicy is not null)
                    ? new(
                        proposedInstancePaidEventPolicy,
                        ExpectedActivePolicyVersion: null)
                    : null;
        var instance = new ConfigurationManifestInstancePlan(
            guardedInstanceSettings,
            unguardedInstanceSettings,
            instancePaidEventPolicy,
            instanceSettings
                .Select(setting => setting.Key)
                .ToImmutableArray(),
            proposedInstancePaidEventPolicy is null
                ? []
                :
                [
                    ConfigurationManifestDocumentKeys.InstancePaidEventPolicy
                ]);

        return new ConfigurationManifestApplyPlan(
            operationId,
            Guid.CreateVersion7(),
            source.Mode,
            source.Manifest.ApiVersion,
            source.Manifest.Kind,
            source.Manifest.Metadata.Name.Trim(),
            source.Sha256Digest,
            ConfigurationManifestInstanceSectionDigest.Compute(
                source.Manifest.Spec.Instance),
            BootstrapState: null,
            occurredAt,
            instance,
            tenants);
    }

    private static ConfigurationManifestTenantPlan CompileTenant(
        ConfigurationManifestTenantV1Alpha2 tenant,
        int manifestIndex)
    {
        ConfigurationManifestSettingWrite[] settings = tenant.Spec.Settings
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ConfigurationManifestSettingWrite(
                pair.Key,
                pair.Value.GetRawText()))
            .ToArray();
        ImmutableArray<ConfigurationManifestSettingWrite> guarded = settings
            .Where(setting => ConfigurationManifestCatalog.TenantSettings[setting.Key]
                .Definition.RequiresCoordinatedMutation)
            .ToImmutableArray();
        ImmutableArray<ConfigurationManifestSettingWrite> unguarded = settings
            .Where(setting => !ConfigurationManifestCatalog.TenantSettings[setting.Key]
                .Definition.RequiresCoordinatedMutation)
            .ToImmutableArray();

        ConfigurationManifestDocumentWrite branding = CompileBranding(tenant.Spec);
        ConfigurationManifestPaidEventPolicyPayloadV1Alpha2? paidEventPolicy =
            CompilePaidEventPolicy(
                tenant.Spec.Documents,
                ConfigurationManifestDocumentKeys.TenantPaidEventPolicy);
        return new ConfigurationManifestTenantPlan(
            manifestIndex,
            Guid.CreateVersion7(),
            tenant.Metadata.Name.Trim(),
            tenant.Spec.DisplayName.Trim(),
            guarded,
            unguarded,
            branding,
            paidEventPolicy,
            settings.Select(setting => setting.Key).ToImmutableArray(),
            paidEventPolicy is null
                ? [branding.DocumentKey]
                :
                [
                    branding.DocumentKey,
                    ConfigurationManifestDocumentKeys.TenantPaidEventPolicy
                ]);
    }

    private static ConfigurationManifestPaidEventPolicyPayloadV1Alpha2?
        CompilePaidEventPolicy(
        IReadOnlyDictionary<string, ConfigurationManifestDocumentV1Alpha2> documents,
        string documentKey)
    {
        if (!documents.TryGetValue(
                documentKey,
                out ConfigurationManifestDocumentV1Alpha2? document))
        {
            return null;
        }

        return document.Payload.Deserialize(
                ConfigurationPortabilityJsonContext.Default
                    .ConfigurationManifestPaidEventPolicyPayloadV1Alpha2)
            ?? throw ConfigurationManifestCompilationException.ContractInvalid();
    }

    private static ConfigurationManifestDocumentWrite CompileBranding(
        ConfigurationManifestTenantSpecV1Alpha2 spec)
    {
        var payload = new BrandingSettings
        {
            DisplayName = spec.DisplayName.Trim()
        };
        if (spec.Documents.TryGetValue(
                SettingsDocumentKeys.Tenant.Branding,
                out ConfigurationManifestDocumentV1Alpha2? supplied))
        {
            JsonElement source = supplied.Payload;
            payload = payload with
            {
                DisplayName = ReadOverlay(source, "displayName", payload.DisplayName),
                LogoUrl = ReadOverlay(source, "logoUrl", payload.LogoUrl),
                FaviconUrl = ReadOverlay(source, "faviconUrl", payload.FaviconUrl),
                CustomCssUrl = ReadOverlay(source, "customCssUrl", payload.CustomCssUrl)
            };
        }

        return new ConfigurationManifestDocumentWrite(
            Guid.CreateVersion7(),
            SettingsDocumentKeys.Tenant.Branding,
            TenantBrandingSettingsDocumentDefaults.SchemaVersion,
            TenantBrandingSettingsDocumentDefaults.DefaultsVersion,
            JsonSerializer.Serialize(payload, SerializerOptions));
    }

    private static string? ReadOverlay(JsonElement source, string propertyName, string? fallback)
    {
        if (!source.TryGetProperty(propertyName, out JsonElement property))
        {
            return fallback;
        }

        return property.ValueKind == JsonValueKind.Null
            ? null
            : Normalize(property.GetString());
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateSourceIdentity(ConfigurationManifestReadResult source)
    {
        if (source.ByteLength <= 0
            || source.Sha256Digest.Length != ConfigurationManifestOperation.DigestLength
            || source.Sha256Digest.Any(character =>
                character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw ConfigurationManifestCompilationException.ContractInvalid();
        }
    }
}

public sealed class ConfigurationManifestCompilationException
    : Exception
{
    public ConfigurationManifestCompilationException(
        IReadOnlyList<ConfigurationManifestValidationError> errors)
        : base("The configuration manifest could not be compiled.")
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0)
        {
            throw new ArgumentException("At least one safe compilation error is required.", nameof(errors));
        }

        Errors = errors.ToArray();
        FailureCode = errors[0].Code;
    }

    private ConfigurationManifestCompilationException(string failureCode, string message)
        : base(message)
    {
        FailureCode = failureCode;
        Errors = [];
    }

    public string FailureCode { get; }
    public IReadOnlyList<ConfigurationManifestValidationError> Errors { get; }

    public static ConfigurationManifestCompilationException ModeInvalid() =>
        new(
            ConfigurationManifestIngestionFailureCodes.ModeInvalid,
            "Configuration manifest mode must be ValidateOnly or Bootstrap.");

    public static ConfigurationManifestCompilationException ContractInvalid() =>
        new(
            ConfigurationManifestFailureCodes.ContractInvalid,
            "The configuration manifest source identity is invalid.");
}
