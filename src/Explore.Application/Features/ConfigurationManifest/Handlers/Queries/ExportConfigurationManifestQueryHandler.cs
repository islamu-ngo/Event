namespace Explore.Application.Features.ConfigurationManifest.Handlers.Queries;

using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.PaidEventPolicies;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Compilation;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Application.Features.ConfigurationManifest.Validation;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Services.Registration;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using FluentValidation;
using MediatR;

public sealed class ExportConfigurationManifestQueryHandler(
    ITenantRepository tenants,
    ISystemSettingRepository systemSettings,
    ITenantSettingRepository tenantSettings,
    ITenantSettingsDocumentRepository tenantDocuments,
    IHierarchicalSettingsResolver settingsResolver,
    ITypedSettingsDocumentResolver typedDocuments,
    IPaidEventPolicyRepository paidEventPolicies,
    IConfigurationManifestOperationRepository operations)
    : IRequestHandler<ExportConfigurationManifestQuery, ConfigurationManifestExportResult>
{
    private static readonly string[] InstanceSettingKeys = ConfigurationManifestCatalog
        .InstanceSettings.Keys.Order(StringComparer.Ordinal).ToArray();

    private static readonly string[] TenantSettingKeys = ConfigurationManifestCatalog
        .TenantSettings.Keys.Order(StringComparer.Ordinal).ToArray();

    private static readonly string[] TenantStoredDocumentKeys = ConfigurationManifestCatalog
        .TenantDocuments.Values
        .Where(entry => entry.Storage == ConfigurationManifestDocumentStorage.TenantSettingsDocument)
        .Select(entry => entry.DocumentKey)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public async Task<ConfigurationManifestExportResult> Handle(
        ExportConfigurationManifestQuery request,
        CancellationToken cancellationToken)
    {
        await new ExportConfigurationManifestQueryValidator()
            .ValidateAndThrowAsync(request, cancellationToken);

        IReadOnlyList<Tenant> activeTenants =
            await tenants.GetAllActiveForConfigurationManifestExportAsync(
                ConfigurationManifestValidator.MaximumTenantCount + 1,
                cancellationToken);
        if (activeTenants.Count == 0)
        {
            throw new InvalidOperationException(
                "Configuration manifest export requires at least one active tenant.");
        }
        if (activeTenants.Count > ConfigurationManifestValidator.MaximumTenantCount)
        {
            throw new ConfigurationManifestExportTooLargeException();
        }

        PaidEventPolicyVersion instancePolicy = await ReadInstancePolicyAsync(cancellationToken);
        IReadOnlyDictionary<string, JsonElement> instanceValues = request.View switch
        {
            ConfigurationManifestExportView.Overrides =>
                await ReadInstanceOverridesAsync(cancellationToken),
            ConfigurationManifestExportView.Portable =>
                await ReadResolvedSettingsAsync(
                    InstanceSettingKeys,
                    new SettingContext(),
                    cancellationToken),
            _ => throw new ValidationException("The configuration manifest export view is invalid.")
        };

        var instanceDocuments =
            new SortedDictionary<string, ConfigurationManifestDocumentV1Alpha2>(
                StringComparer.Ordinal)
            {
                [ConfigurationManifestDocumentKeys.InstancePaidEventPolicy] =
                    PaidPolicyDocument(instancePolicy)
            };

        var exportedTenants = new List<ConfigurationManifestTenantV1Alpha2>(
            activeTenants.Count);
        foreach (Tenant tenant in activeTenants.OrderBy(value => value.Slug, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            exportedTenants.Add(await ExportTenantAsync(
                tenant,
                instancePolicy,
                request.View,
                cancellationToken));
        }

        ConfigurationManifestOperation? latest =
            await operations.GetLatestAppliedBootstrapAsync(cancellationToken);
        string manifestName = string.IsNullOrWhiteSpace(latest?.ManifestName)
            ? "current-instance"
            : latest.ManifestName;

        var manifest = new ConfigurationManifestV1Alpha2
        {
            Schema = ConfigurationManifestContractMetadata.SchemaId,
            ApiVersion = ConfigurationManifestContractMetadata.ApiVersion,
            Kind = ConfigurationManifestContractMetadata.Kind,
            Metadata = new ConfigurationManifestMetadataV1Alpha2
            {
                Name = manifestName,
                Export = new ConfigurationManifestExportMetadataV1Alpha2
                {
                    View = request.View == ConfigurationManifestExportView.Portable
                        ? ConfigurationManifestExportMetadataValues.PortableView
                        : ConfigurationManifestExportMetadataValues.OverridesView,
                    EffectiveValuesFlattened =
                        request.View == ConfigurationManifestExportView.Portable,
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
                    Settings = instanceValues,
                    Documents = instanceDocuments
                },
                Tenants = exportedTenants
            }
        };

        ConfigurationManifestExportBrandingPolicy.EnsureSafeForExport(manifest);
        if (!ConfigurationManifestValidator.Validate(manifest).IsValid)
        {
            throw new InvalidOperationException(
                "Current configuration could not be represented by the configuration manifest contract.");
        }

        return new ConfigurationManifestExportResult
        {
            View = request.View,
            FileName = request.View == ConfigurationManifestExportView.Portable
                ? "configuration-manifest-portable.json"
                : "configuration-manifest-overrides.json",
            Utf8Json = ConfigurationManifestExportJsonSerializer.Serialize(manifest)
        };
    }

    private async Task<ConfigurationManifestTenantV1Alpha2> ExportTenantAsync(
        Tenant tenant,
        PaidEventPolicyVersion instancePolicy,
        ConfigurationManifestExportView view,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, JsonElement> values = view switch
        {
            ConfigurationManifestExportView.Overrides =>
                await ReadTenantOverridesAsync(tenant.Id, cancellationToken),
            ConfigurationManifestExportView.Portable =>
                await ReadResolvedSettingsAsync(
                    TenantSettingKeys,
                    new SettingContext(TenantId: tenant.Id),
                    cancellationToken),
            _ => throw new ValidationException("The configuration manifest export view is invalid.")
        };

        SortedDictionary<string, ConfigurationManifestDocumentV1Alpha2> documents =
            view == ConfigurationManifestExportView.Portable
                ? await ReadPortableTenantDocumentsAsync(tenant.Id, cancellationToken)
                : await ReadTenantDocumentOverridesAsync(tenant.Id, cancellationToken);

        PaidEventPolicyVersion? tenantPolicy =
            await paidEventPolicies.GetActiveTenantAsync(tenant.Id, cancellationToken);
        if (tenantPolicy is not null && tenantPolicy.TenantId != tenant.Id)
        {
            throw new InvalidOperationException(
                "The paid-event policy repository returned a policy for a different tenant.");
        }

        if (view == ConfigurationManifestExportView.Portable || tenantPolicy is not null)
        {
            PaidEventPolicyVersion selected = tenantPolicy ?? instancePolicy;
            ConfigurationManifestPaidEventPolicyPayloadV1Alpha2 payload =
                ConfigurationManifestPaidEventPolicyMapper.ToManifestPayload(selected);
            PaidEventPolicyVersion candidate =
                ConfigurationManifestPaidEventPolicyMapper.CreateTenantCandidate(
                    tenant.Id,
                    payload);
            PaidEventPolicyRules.ValidateTenantPolicy(instancePolicy, candidate);
            documents.Add(
                ConfigurationManifestDocumentKeys.TenantPaidEventPolicy,
                PaidPolicyDocument(payload));
        }

        return new ConfigurationManifestTenantV1Alpha2
        {
            Metadata = new ConfigurationManifestTenantMetadataV1Alpha2
            {
                Name = tenant.Slug
            },
            Spec = new ConfigurationManifestTenantSpecV1Alpha2
            {
                DisplayName = tenant.FullName,
                Settings = values,
                Documents = documents
            }
        };
    }

    private async Task<IReadOnlyDictionary<string, JsonElement>>
        ReadInstanceOverridesAsync(CancellationToken cancellationToken)
    {
        List<SystemSetting> stored = await systemSettings.GetAllSettings(
            cancellationToken: cancellationToken);
        var values = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (SystemSetting setting in stored.OrderBy(
                     value => value.SettingKey,
                     StringComparer.Ordinal))
        {
            if (ConfigurationManifestCatalog.TryGetInstanceSetting(setting.SettingKey, out _))
                AddUnique(values, setting.SettingKey, ParseJson(setting.Value));
        }

        return values;
    }

    private async Task<IReadOnlyDictionary<string, JsonElement>> ReadTenantOverridesAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        List<TenantSetting> stored = await tenantSettings.GetByTenantAndKeys(
            tenantId,
            TenantSettingKeys,
            cancellationToken);
        var values = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (TenantSetting setting in stored.OrderBy(
                     value => value.SettingKey,
                     StringComparer.Ordinal))
        {
            if (setting.TenantId == tenantId
                && ConfigurationManifestCatalog.TryGetTenantSetting(setting.SettingKey, out _))
            {
                AddUnique(values, setting.SettingKey, ParseJson(setting.Value));
            }
        }

        return values;
    }

    private async Task<IReadOnlyDictionary<string, JsonElement>> ReadResolvedSettingsAsync(
        IReadOnlyList<string> expectedKeys,
        SettingContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ResolvedSetting> resolved = await settingsResolver.ResolveBatchAsync(
            expectedKeys,
            context,
            cancellationToken);
        if (resolved.Count != expectedKeys.Count)
        {
            throw new InvalidOperationException(
                "The settings resolver returned an incomplete configuration manifest catalog.");
        }

        var values = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        for (int index = 0; index < expectedKeys.Count; index++)
        {
            if (!string.Equals(resolved[index].Key, expectedKeys[index], StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The settings resolver returned an unexpected configuration manifest key.");
            }

            values.Add(expectedKeys[index], ParseJson(resolved[index].Value));
        }

        return values;
    }

    private async Task<SortedDictionary<string, ConfigurationManifestDocumentV1Alpha2>>
        ReadTenantDocumentOverridesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        IReadOnlyList<TenantSettingsDocument> stored = await tenantDocuments.GetManyForTenant(
            tenantId,
            TenantStoredDocumentKeys,
            cancellationToken);
        var documents =
            new SortedDictionary<string, ConfigurationManifestDocumentV1Alpha2>(
                StringComparer.Ordinal);
        foreach (TenantSettingsDocument document in stored.OrderBy(
                     value => value.DocumentKey,
                     StringComparer.Ordinal))
        {
            if (document.TenantId != tenantId
                || !ConfigurationManifestCatalog.TryGetTenantDocument(
                    document.DocumentKey,
                    out ConfigurationManifestDocumentCatalogEntry? catalogEntry)
                || catalogEntry is null
                || catalogEntry.Storage
                    != ConfigurationManifestDocumentStorage.TenantSettingsDocument)
            {
                continue;
            }

            EnsureDocumentVersion(document, catalogEntry);
            if (!documents.TryAdd(
                    document.DocumentKey,
                    new ConfigurationManifestDocumentV1Alpha2
                    {
                        SchemaVersion = document.SchemaVersion,
                        Payload = ParseJson(document.PayloadJson)
                    }))
            {
                throw new InvalidOperationException(
                    "Duplicate tenant settings documents were returned for configuration manifest export.");
            }
        }

        return documents;
    }

    private async Task<SortedDictionary<string, ConfigurationManifestDocumentV1Alpha2>>
        ReadPortableTenantDocumentsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var documents =
            new SortedDictionary<string, ConfigurationManifestDocumentV1Alpha2>(
                StringComparer.Ordinal);
        if (ConfigurationManifestCatalog.TryGetTenantDocument(
                SettingsDocumentKeys.Tenant.Branding,
                out ConfigurationManifestDocumentCatalogEntry? catalogEntry)
            && catalogEntry is not null)
        {
            ResolvedSettingsDocument<BrandingSettings>? branding =
                await typedDocuments.ResolveTenantDocumentAsync<BrandingSettings>(
                    new SettingsResolutionContext(
                        tenantId,
                        RequestedDocuments: [SettingsDocumentKeys.Tenant.Branding]),
                    SettingsDocumentKeys.Tenant.Branding,
                    cancellationToken);
            if (branding is not null)
            {
                if (branding.SourceScopeId != tenantId
                    || branding.SchemaVersion != catalogEntry.SchemaVersion
                    || !string.Equals(
                        branding.DefaultsVersion,
                        catalogEntry.DefaultsVersion,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The resolved tenant branding document does not match the configuration manifest catalog.");
                }

                documents.Add(
                    SettingsDocumentKeys.Tenant.Branding,
                    new ConfigurationManifestDocumentV1Alpha2
                    {
                        SchemaVersion = branding.SchemaVersion,
                        Payload = JsonSerializer.SerializeToElement(branding.Payload, WebJson)
                    });
            }
        }

        return documents;
    }

    private async Task<PaidEventPolicyVersion> ReadInstancePolicyAsync(
        CancellationToken cancellationToken)
    {
        PaidEventPolicyVersion? instancePolicy =
            await paidEventPolicies.GetActiveInstanceAsync(cancellationToken);
        if (instancePolicy is null
            || !instancePolicy.IsActive
            || instancePolicy.TenantId is not null)
        {
            throw new InvalidOperationException(
                "An active instance paid-event policy is required for configuration manifest export.");
        }

        _ = ConfigurationManifestPaidEventPolicyMapper.CreateInstanceCandidate(
            ConfigurationManifestPaidEventPolicyMapper.ToManifestPayload(instancePolicy));
        return instancePolicy;
    }

    private static ConfigurationManifestDocumentV1Alpha2 PaidPolicyDocument(
        PaidEventPolicyVersion policy) =>
        PaidPolicyDocument(
            ConfigurationManifestPaidEventPolicyMapper.ToManifestPayload(policy));

    private static ConfigurationManifestDocumentV1Alpha2 PaidPolicyDocument(
        ConfigurationManifestPaidEventPolicyPayloadV1Alpha2 payload) =>
        new()
        {
            SchemaVersion = ConfigurationManifestCatalog.InstanceDocuments[
                ConfigurationManifestDocumentKeys.InstancePaidEventPolicy].SchemaVersion,
            Payload = JsonSerializer.SerializeToElement(
                payload,
                ConfigurationPortabilityJsonContext.Default
                    .ConfigurationManifestPaidEventPolicyPayloadV1Alpha2)
        };

    private static void EnsureDocumentVersion(
        TenantSettingsDocument document,
        ConfigurationManifestDocumentCatalogEntry catalogEntry)
    {
        if (document.SchemaVersion != catalogEntry.SchemaVersion
            || !string.Equals(
                document.DefaultsVersion,
                catalogEntry.DefaultsVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A tenant settings document does not match the configuration manifest catalog version.");
        }
    }

    private static void AddUnique(
        IDictionary<string, JsonElement> values,
        string key,
        JsonElement value)
    {
        if (!values.TryAdd(key, value))
        {
            throw new InvalidOperationException(
                "Duplicate settings were returned for configuration manifest export.");
        }
    }

    private static JsonElement ParseJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
