namespace Explore.Application.Features.ConfigurationManifest.Application;

using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.PaidEventPolicies;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Compilation;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Moderation;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Application.Features.ConfigurationManifest.Validation;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

public sealed class ConfigurationManifestCurrentStateReader(
    ITenantRepository tenants,
    ISystemSettingRepository systemSettings,
    ITenantSettingRepository tenantSettings,
    ITenantSettingsDocumentRepository tenantDocuments,
    IHierarchicalSettingsResolver settingsResolver,
    IPaidEventPolicyRepository paidEventPolicies,
    IConfigurationManifestOperationRepository operations,
    ModerationConfigurationExportService moderation,
    ILegalDocumentRepository legalDocuments,
    IUnitOfWork unitOfWork,
    IConfigurationManifestTenantReadScopeFactory tenantReadScopes,
    IConfigurationImportLegalSourceReader legalSources,
    IConfigurationManifestPackageAuthority packageAuthority,
    IConfigurationManifestPackageModerationReader packageModeration)
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

    public Task<ConfigurationManifestExportResult> ReadInCurrentTransactionAsync(
        ConfigurationManifestExportView view,
        ConfigurationManifestExportScope scope,
        CancellationToken cancellationToken) =>
        ReadCoreAsync(view, scope, publicExport: true, cancellationToken);

    /// <summary>Reads the import owner's snapshot, including its uncommitted authoritative mutations.</summary>
    public Task<ConfigurationManifestExportResult> ReadImportSnapshotAsync(
        ConfigurationManifestExportView view, ConfigurationManifestExportScope scope,
        CancellationToken cancellationToken) =>
        ReadCoreAsync(view, scope, publicExport: false, cancellationToken);

    private async Task<ConfigurationManifestExportResult> ReadCoreAsync(
        ConfigurationManifestExportView view, ConfigurationManifestExportScope scope, bool publicExport,
        CancellationToken cancellationToken)
    {
        await new ExportConfigurationManifestQueryValidator()
            .ValidateAndThrowAsync(new ExportConfigurationManifestQuery(view, scope), cancellationToken);

        // Bound the tenant set before any per-tenant source read so an oversized instance fails fast.
        IReadOnlyList<Tenant> activeTenants = scope == ConfigurationManifestExportScope.Instance
            ? []
            : await tenants.GetAllActiveForConfigurationManifestExportAsync(
                ConfigurationManifestValidator.MaximumTenantCount + 1,
                cancellationToken);
        if (activeTenants.Count > ConfigurationManifestValidator.MaximumTenantCount)
        {
            throw new ConfigurationManifestExportTooLargeException();
        }

        ModerationConfigurationExport definitions = publicExport
            ? await moderation.ReadPublicManifestInCurrentTransactionAsync(scope, cancellationToken)
            : await moderation.ReadInCurrentTransactionAsync(
                ConfigurationImportTarget.ForInstance(), scope, cancellationToken);

        var instanceLegal = await ReadLegalSourcesAsync(
            LegalDocumentScope.Instance, null, cancellationToken);
        PaidEventPolicyVersion instancePolicy = await ReadInstancePolicyAsync(cancellationToken);
        IReadOnlyDictionary<string, JsonElement> instanceValues = view switch
        {
            ConfigurationManifestExportView.Overrides =>
                await ReadInstanceOverridesAsync(cancellationToken),
            ConfigurationManifestExportView.Portable =>
                await ReadResolvedSettingsAsync(
                    InstanceSettingKeys,
                    new SettingContext(RequireAuthoritativeReads: true),
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
            if (!publicExport)
            {
                exportedTenants.Add(await ExportTenantAsync(tenant, instancePolicy, view,
                    definitions.Tenants.GetValueOrDefault(tenant.Id),
                    await ReadLegalSourcesAsync(LegalDocumentScope.Tenant, tenant.Id, cancellationToken),
                    cancellationToken));
                continue;
            }
            await using var readerScope = await tenantReadScopes.CreateAsync(tenant.Id, cancellationToken);
            var reader = readerScope.Services.GetRequiredService<ConfigurationManifestCurrentStateReader>();
            exportedTenants.Add(await reader.ExportTenantAsync(
                tenant,
                instancePolicy,
                view,
                definitions.Tenants.GetValueOrDefault(tenant.Id),
                await reader.ReadLegalSourcesAsync(LegalDocumentScope.Tenant, tenant.Id, cancellationToken),
                cancellationToken));
        }

        ConfigurationManifestOperation? latest =
            publicExport ? null : await operations.GetLatestAppliedBootstrapAsync(cancellationToken);
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
                    View = view == ConfigurationManifestExportView.Portable
                        ? ConfigurationManifestExportMetadataValues.PortableView
                        : ConfigurationManifestExportMetadataValues.OverridesView,
                    EffectiveValuesFlattened =
                        view == ConfigurationManifestExportView.Portable,
                    SensitiveValuesOmitted = true,
                    AuthorityScope = scope == ConfigurationManifestExportScope.Instance
                        ? ConfigurationManifestExportMetadataValues.InstanceAuthorityScope
                        : ConfigurationManifestExportMetadataValues.InstanceAndTenantsAuthorityScope,
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
                    Documents = instanceDocuments,
                    LegalDocuments = instanceLegal,
                    Moderation = definitions.Instance
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
            View = view,
            Scope = scope,
            FileName = view == ConfigurationManifestExportView.Portable
                ? "configuration-manifest-portable.json"
                : "configuration-manifest-overrides.json",
            Utf8Json = ConfigurationPortabilityJsonCodec.SerializeConfigurationManifest(manifest)
        };
    }

    /// <summary>
    /// Reads only the authorized tenant's package. It does not enumerate other tenants or borrow
    /// instance-export authority to trim a complete manifest after disclosure has already occurred.
    /// </summary>
    public async Task<TenantConfigurationPackageExportResult> ReadTenantAsync(
        Guid tenantId, ConfigurationManifestExportView view, CancellationToken cancellationToken)
    {
        TenantConfigurationPackageExportResult result;
        await using (var scope = await tenantReadScopes.CreateAsync(tenantId, cancellationToken))
        {
            var reader = scope.Services.GetRequiredService<ConfigurationManifestCurrentStateReader>();
            result = await scope.Services.GetRequiredService<IUnitOfWork>().ExecuteSerializableAsync(
                token => reader.ReadTenantInCurrentTransactionAsync(tenantId, view, token), cancellationToken);
        }

        // Runtime scope disposal can suspend; recheck authority and credential before disclosure.
        await packageAuthority.RequireAsync(tenantId, cancellationToken);
        return result;
    }

    public async Task<TenantConfigurationPackageExportResult> ReadTenantInCurrentTransactionAsync(
        Guid tenantId, ConfigurationManifestExportView view, CancellationToken cancellationToken)
    {
        await new ExportConfigurationManifestQueryValidator()
            .ValidateAndThrowAsync(new ExportConfigurationManifestQuery(view), cancellationToken);
        // Runtime holds no role-grant or moderation-source permissions. Authority and the moderation
        // definition are fresh Global reads that bracket this Runtime snapshot; see the final check.
        ModerationConfigurationExport definitions = await packageModeration.ReadAsync(tenantId, cancellationToken);
        Tenant tenant = await tenants.GetByIdAsNoTrackingAsync(tenantId, cancellationToken)
            ?? throw new ConfigurationImportSessionException(ConfigurationImportFailureCodes.ArtifactMissing);
        IReadOnlyDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2> legal = await ReadLegalSourcesAsync(
            LegalDocumentScope.Tenant, tenantId, cancellationToken);
        var policy = await ReadInstancePolicyAsync(cancellationToken);
        var exportedTenant = await ExportTenantAsync(
            tenant, policy, view, definitions.Tenants.GetValueOrDefault(tenant.Id),
            legal,
            cancellationToken);
        var metadata = new ConfigurationManifestMetadataV1Alpha2
        {
            Name = "current-instance",
            Export = new ConfigurationManifestExportMetadataV1Alpha2
            {
                View = view == ConfigurationManifestExportView.Portable
                    ? ConfigurationManifestExportMetadataValues.PortableView
                    : ConfigurationManifestExportMetadataValues.OverridesView,
                EffectiveValuesFlattened = view == ConfigurationManifestExportView.Portable,
                SensitiveValuesOmitted = true,
                AuthorityScope = ConfigurationManifestExportMetadataValues.TenantAuthorityScope,
                SovereignValuesOmitted = true,
                SovereignLockedFields = PaidEventPolicyAuthorityMetadata.SovereignLockedFields
            }
        };
        var package = TenantConfigurationPackageSerializer.Create(metadata, exportedTenant);
        // Reuse the existing disclosure guard with only this tenant, never a globally read manifest.
        ConfigurationManifestExportBrandingPolicy.EnsureSafeForExport(new ConfigurationManifestV1Alpha2
        {
            Schema = ConfigurationManifestContractMetadata.SchemaId,
            ApiVersion = ConfigurationManifestContractMetadata.ApiVersion,
            Kind = ConfigurationManifestContractMetadata.Kind,
            Metadata = metadata,
            Spec = new ConfigurationManifestSpecV1Alpha2
            {
                Instance = new ConfigurationManifestInstanceV1Alpha2
                {
                    Settings = new Dictionary<string, JsonElement>(),
                    Documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>()
                },
                Tenants = [exportedTenant]
            }
        });
        if (!ConfigurationManifestValidator.Validate(package).IsValid)
            throw new InvalidOperationException(
                "Current tenant configuration could not be represented by the configuration package contract.");
        ReadOnlyMemory<byte> bytes = TenantConfigurationPackageSerializer.Serialize(package);
        // Unchanged source publications on both sides of the Runtime snapshot mean the package equals
        // the state at that snapshot; republishing always mints a new publication id.
        ModerationConfigurationExport settled = await packageModeration.ReadAsync(tenantId, cancellationToken);
        if (!settled.PublicationIds.SequenceEqual(definitions.PublicationIds))
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
        return new(view, $"tenant-configuration-package-{tenant.Slug}.json",
            bytes, ConfigurationImportDigest.ComputeBytes(bytes.Span));
    }

    private async Task<ConfigurationManifestTenantV1Alpha2> ExportTenantAsync(
        Tenant tenant,
        PaidEventPolicyVersion instancePolicy,
        ConfigurationManifestExportView view,
        ConfigurationModerationDocumentV1? definition,
        IReadOnlyDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2> legal,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, JsonElement> values = view switch
        {
            ConfigurationManifestExportView.Overrides =>
                await ReadTenantOverridesAsync(tenant.Id, cancellationToken),
            ConfigurationManifestExportView.Portable =>
                await ReadResolvedSettingsAsync(
                    TenantSettingKeys,
                    new SettingContext(TenantId: tenant.Id, RequireAuthoritativeReads: true),
                    cancellationToken),
            _ => throw new ValidationException("The configuration manifest export view is invalid.")
        };

        SortedDictionary<string, ConfigurationManifestDocumentV1Alpha2> documents =
            view == ConfigurationManifestExportView.Portable
                ? await ReadPortableTenantDocumentsAsync(tenant.Id, cancellationToken)
                : await ReadTenantDocumentOverridesAsync(tenant.Id, cancellationToken);

        PaidEventPolicyVersion? tenantPolicy =
            await paidEventPolicies.GetActiveForConfigurationExportAsync(tenant.Id, cancellationToken);
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
                Documents = documents,
                LegalDocuments = legal,
                Moderation = definition
            }
        };
    }

    private async Task<IReadOnlyDictionary<string, JsonElement>>
        ReadInstanceOverridesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SystemSetting> stored = await systemSettings.GetByKeysForConfigurationExportAsync(
            InstanceSettingKeys, cancellationToken);
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
        IReadOnlyList<TenantSetting> stored = await tenantSettings.GetByTenantAndKeysForConfigurationExportAsync(
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

    private async Task<IReadOnlyDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2>> ReadLegalSourcesAsync(
        LegalDocumentScope scope, Guid? tenantId, CancellationToken cancellationToken)
    {
        var documents = await legalSources.ReadAsync(scope, tenantId, cancellationToken);
        return documents.OrderBy(document => document.Kind).ToDictionary(
            document => document.Kind.ToString(),
            document => new ConfigurationManifestLegalDocumentV1Alpha2
            {
                Kind = document.Kind.ToString(),
                Audience = document.Audience.ToString(),
                LifecycleIntent = "ReviewRequired",
                RequiresFreshAcceptance = document.RequiresFreshAcceptance,
                JurisdictionAssumptions = [],
                TemplateProvenance = document.TemplateProvenance is { } template
                    ? new ConfigurationManifestLegalTemplateProvenanceV1Alpha2
                    {
                        TemplateId = template.TemplateId,
                        TemplateVersion = template.TemplateVersion,
                        SourceKind = template.SourceKind.ToString(),
                        LicenseExpression = template.LicenseExpression,
                        ReviewReference = template.ReviewReference
                    } : null,
                Localizations = document.Localizations.Select(source =>
                    new ConfigurationManifestLegalDocumentLocalizedSourceV1Alpha2
                    {
                        LanguageTag = source.LanguageTag,
                        Title = source.Title,
                        Summary = source.Summary,
                        Markdown = source.Markdown
                    }).ToArray()
            }, StringComparer.Ordinal);
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
        IReadOnlyList<TenantSettingsDocument> stored = await tenantDocuments.GetManyForConfigurationExportAsync(
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
            // Same export-only stored read as Overrides; Portable re-projects it through the
            // typed payload so the artifact carries the normalized branding shape.
            IReadOnlyList<TenantSettingsDocument> stored = await tenantDocuments.GetManyForConfigurationExportAsync(
                tenantId, [SettingsDocumentKeys.Tenant.Branding], cancellationToken);
            TenantSettingsDocument? branding = stored.SingleOrDefault(
                document => document.DocumentKey == SettingsDocumentKeys.Tenant.Branding);
            if (branding is not null)
            {
                if (branding.TenantId != tenantId)
                    throw new InvalidOperationException(
                        "The tenant branding document does not belong to the exported tenant.");
                EnsureDocumentVersion(branding, catalogEntry);
                BrandingSettings payload = JsonSerializer.Deserialize<BrandingSettings>(branding.PayloadJson, WebJson)
                    ?? throw new InvalidOperationException("The tenant branding document payload could not be read.");
                documents.Add(
                    SettingsDocumentKeys.Tenant.Branding,
                    new ConfigurationManifestDocumentV1Alpha2
                    {
                        SchemaVersion = branding.SchemaVersion,
                        Payload = JsonSerializer.SerializeToElement(payload, WebJson)
                    });
            }
        }

        return documents;
    }

    private async Task<PaidEventPolicyVersion> ReadInstancePolicyAsync(
        CancellationToken cancellationToken)
    {
        PaidEventPolicyVersion? instancePolicy =
            await paidEventPolicies.GetActiveForConfigurationExportAsync(null, cancellationToken);
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
