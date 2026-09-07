namespace Explore.Application.Features.ConfigurationManifest.Importing;

using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Application.Features.ConfigurationManifest.Compilation;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.PaidEventPolicies;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings.Documents;

public sealed class ConfigurationImportSectionApplier(
    IConfigurationManifestInstanceSettingMutationBoundary instanceSettings,
    IConfigurationManifestTenantSettingMutationBoundary tenantSettings,
    IConfigurationImportTenantIdentityMutationBoundary tenantIdentity,
    IPublicationPolicyMutationBoundary publicationPolicy,
    IPaidEventPolicyMutationBoundary paidEventPolicy,
    IPaidEventPolicyRepository paidEventPolicies,
    ITenantRepository tenants,
    ITenantSettingsDocumentRepository tenantDocuments,
    ILegalDocumentRepository legalDocuments)
{
    public async Task ApplyAsync(
        ConfigurationImportTarget target,
        ReadOnlyMemory<byte> sourceBytes,
        ConfigurationImportPreviewRequest request,
        Guid actorUserId,
        DateTime occurredAt,
        string artifactDigest,
        ConfigurationImportArtifactParser parser,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(parser);
        var selected = request.SelectedSectionKeys.ToHashSet(StringComparer.Ordinal);
        if (target.Scope == ConfigurationImportScope.Instance)
        {
            ConfigurationManifestV1Alpha2 manifest = parser.Parse(sourceBytes).Manifest;
            await ApplyInstanceAsync(
                manifest.Spec.Instance,
                selected,
                actorUserId,
                occurredAt,
                artifactDigest,
                cancellationToken);
            await ApplyManifestTenantsAsync(
                manifest.Spec.Tenants,
                selected,
                request.Mappings,
                actorUserId,
                occurredAt,
                artifactDigest,
                cancellationToken);
            return;
        }

        TenantConfigurationPackageV1Alpha2 package =
            parser.ParseTenantPackage(sourceBytes).Package;
        await ApplyTenantAsync(
            target.TenantId
                ?? throw new ConfigurationImportSessionException(
                    ConfigurationImportFailureCodes.TargetMismatch),
            package.Spec.DisplayName,
            package.Spec.Settings,
            package.Spec.Documents,
            package.Spec.LegalDocuments,
            selected,
            actorUserId,
            occurredAt,
            artifactDigest,
            cancellationToken);
    }

    public async Task<IReadOnlyList<IReadOnlyList<string>>> CompileLockGroupsAsync(
        ConfigurationImportTarget target,
        ReadOnlyMemory<byte> sourceBytes,
        ConfigurationImportPreviewRequest request,
        ConfigurationImportArtifactParser parser,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        var resources = new HashSet<string>(StringComparer.Ordinal);
        var selected = request.SelectedSectionKeys.ToHashSet(StringComparer.Ordinal);
        if (target.Scope == ConfigurationImportScope.Instance)
        {
            ConfigurationManifestV1Alpha2 manifest = parser.Parse(sourceBytes).Manifest;
            if (selected.Contains("instance.settings"))
                resources.UnionWith(manifest.Spec.Instance.Settings.Keys);
            if (selected.Contains("instance.documents"))
                resources.Add(PaidEventPolicyMutationLockKeys.Instance);
            AddLegalLocks(
                resources,
                "instance",
                manifest.Spec.Instance.LegalDocuments,
                selected.Contains("instance.legal_documents"));
            if (selected.Any(section =>
                    section.StartsWith("tenant.", StringComparison.Ordinal)))
            {
                string[] targetSlugs = manifest.Spec.Tenants
                    .Select(tenant => ResolveTargetSlug(
                        tenant.Metadata.Name,
                        request.Mappings))
                    .ToArray();
                IReadOnlyList<Tenant> targets =
                    await tenants.GetBySlugsAsNoTrackingAsync(
                        targetSlugs,
                        cancellationToken);
                var targetIds = targets.ToDictionary(
                    tenant => tenant.Slug,
                    tenant => tenant.Id,
                    StringComparer.Ordinal);
                foreach (ConfigurationManifestTenantV1Alpha2 tenant in manifest.Spec.Tenants)
                {
                    string targetSlug = ResolveTargetSlug(
                        tenant.Metadata.Name,
                        request.Mappings);
                    if (!targetIds.TryGetValue(targetSlug, out Guid targetTenantId))
                        throw Blocked("configuration_import_mapping_target_missing");
                    AddTenantLocks(
                        resources,
                        targetSlug,
                        tenant.Spec,
                        selected,
                        targetTenantId);
                }
            }
        }
        else
        {
            TenantConfigurationPackageV1Alpha2 package =
                parser.ParseTenantPackage(sourceBytes).Package;
            AddTenantLocks(
                resources,
                target.TenantId!.Value.ToString("N"),
                package.Spec,
                selected,
                target.TenantId!.Value);
        }

        return new IReadOnlyList<string>[]
        {
            [$"!configuration-import:{target.AuthorityKey}"],
            [.. resources.Order(StringComparer.Ordinal)]
        };
    }

    private async Task ApplyInstanceAsync(
        ConfigurationManifestInstanceV1Alpha2 source,
        IReadOnlySet<string> selected,
        Guid actorUserId,
        DateTime occurredAt,
        string artifactDigest,
        CancellationToken cancellationToken)
    {
        if (selected.Contains("instance.settings") && source.Settings.Count > 0)
        {
            ConfigurationManifestInstanceSettingMutationResult result =
                await instanceSettings.ApplyInCurrentTransactionAsync(
                    new ConfigurationManifestInstanceSettingMutationInput(
                        [.. source.Settings.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                            .Select(pair =>
                                new ConfigurationManifestInstanceSettingMutation(
                                    pair.Key,
                                    pair.Value.GetRawText()))],
                        actorUserId,
                        occurredAt),
                    cancellationToken);
            Ensure(result.Success, result.FailureCode, result.Message);
        }

        if (selected.Contains("instance.documents")
            && source.Documents.TryGetValue(
                ConfigurationManifestDocumentKeys.InstancePaidEventPolicy,
                out ConfigurationManifestDocumentV1Alpha2? document))
        {
            PaidEventPolicyVersion current =
                await paidEventPolicies.GetActiveInstanceAsync(cancellationToken)
                ?? throw Blocked("configuration_import_instance_policy_missing");
            ConfigurationManifestPaidEventPolicyPayloadV1Alpha2 payload =
                DeserializePaidPolicy(document);
            PaidEventPolicyMutationResult result =
                await paidEventPolicy.ReviseInstanceInCurrentTransactionAsync(
                    new InstancePaidEventPolicyMutationInput(
                        ConfigurationManifestPaidEventPolicyMapper.ToRevisionDto(payload),
                        current.VersionNumber),
                    cancellationToken);
            Ensure(result.Success, result.FailureCode, result.Message);
        }

        if (selected.Contains("instance.legal_documents"))
        {
            await ApplyLegalAsync(
                LegalDocumentScope.Instance,
                tenantId: null,
                source.LegalDocuments,
                artifactDigest,
                occurredAt,
                cancellationToken);
        }
    }

    private async Task ApplyManifestTenantsAsync(
        IReadOnlyList<ConfigurationManifestTenantV1Alpha2> sourceTenants,
        IReadOnlySet<string> selected,
        IReadOnlyDictionary<string, string> mappings,
        Guid actorUserId,
        DateTime occurredAt,
        string artifactDigest,
        CancellationToken cancellationToken)
    {
        if (!selected.Any(section => section.StartsWith("tenant.", StringComparison.Ordinal)))
            return;
        string[] targetSlugs = sourceTenants
            .Select(tenant => ResolveTargetSlug(tenant.Metadata.Name, mappings))
            .ToArray();
        if (targetSlugs.Distinct(StringComparer.Ordinal).Count() != targetSlugs.Length)
            throw Blocked("configuration_import_mapping_target_duplicate");
        IReadOnlyList<Tenant> existing = await tenants.GetBySlugsAsNoTrackingAsync(
            targetSlugs,
            cancellationToken);
        var bySlug = existing.ToDictionary(tenant => tenant.Slug, StringComparer.Ordinal);
        foreach (ConfigurationManifestTenantV1Alpha2 source in sourceTenants)
        {
            string targetSlug = ResolveTargetSlug(source.Metadata.Name, mappings);
            if (!bySlug.TryGetValue(targetSlug, out Tenant? target))
                throw Blocked("configuration_import_mapping_target_missing");
            await ApplyTenantAsync(
                target.Id,
                source.Spec.DisplayName,
                source.Spec.Settings,
                source.Spec.Documents,
                source.Spec.LegalDocuments,
                selected,
                actorUserId,
                occurredAt,
                artifactDigest,
                cancellationToken);
        }
    }

    private async Task ApplyTenantAsync(
        Guid tenantId,
        string displayName,
        IReadOnlyDictionary<string, JsonElement> settings,
        IReadOnlyDictionary<string, ConfigurationManifestDocumentV1Alpha2> documents,
        IReadOnlyDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2> legal,
        IReadOnlySet<string> selected,
        Guid actorUserId,
        DateTime occurredAt,
        string artifactDigest,
        CancellationToken cancellationToken)
    {
        if (selected.Contains("tenant.settings"))
        {
            await tenantIdentity.ApplyInCurrentTransactionAsync(
                tenantId,
                displayName,
                actorUserId,
                occurredAt,
                cancellationToken);
            ConfigurationManifestTenantSettingMutation[] guarded = settings
                .Where(pair => ConfigurationManifestCatalog.TenantSettings[pair.Key]
                    .Definition.RequiresCoordinatedMutation)
                .Select(Mutation)
                .ToArray();
            if (guarded.Length > 0)
            {
                PublicationPolicyMutationResult result =
                    await publicationPolicy.ApplyTenantInCurrentTransactionAsync(
                        new PublicationPolicyTenantMutationRequest(
                            tenantId,
                            actorUserId,
                            occurredAt,
                            [.. guarded.Select(setting =>
                                new PublicationPolicySettingMutation(
                                    setting.Key,
                                    PublicationPolicyMutationKind.Set,
                                    setting.SerializedValue,
                                    tenantId,
                                    IsLocked: null))],
                            PublicationPolicyLockedSystemBehavior.Reject),
                        cancellationToken);
                Ensure(result.Success, result.FailureCode, result.Message);
            }

            ConfigurationManifestTenantSettingMutation[] ordinary = settings
                .Where(pair => !ConfigurationManifestCatalog.TenantSettings[pair.Key]
                    .Definition.RequiresCoordinatedMutation)
                .Select(Mutation)
                .ToArray();
            await tenantSettings.ApplyInCurrentTransactionAsync(
                new ConfigurationManifestTenantSettingMutationInput(
                    tenantId,
                    ordinary,
                    actorUserId,
                    occurredAt),
                cancellationToken);
        }

        if (selected.Contains("tenant.documents"))
        {
            await ApplyTenantDocumentsAsync(
                tenantId,
                documents,
                actorUserId,
                occurredAt,
                cancellationToken);
        }

        if (selected.Contains("tenant.legal_documents"))
        {
            await ApplyLegalAsync(
                LegalDocumentScope.Tenant,
                tenantId,
                legal,
                artifactDigest,
                occurredAt,
                cancellationToken);
        }
    }

    private async Task ApplyTenantDocumentsAsync(
        Guid tenantId,
        IReadOnlyDictionary<string, ConfigurationManifestDocumentV1Alpha2> documents,
        Guid actorUserId,
        DateTime occurredAt,
        CancellationToken cancellationToken)
    {
        if (documents.TryGetValue(
                SettingsDocumentKeys.Tenant.Branding,
                out ConfigurationManifestDocumentV1Alpha2? branding))
        {
            ConfigurationManifestDocumentCatalogEntry catalog =
                ConfigurationManifestCatalog.TenantDocuments[
                    SettingsDocumentKeys.Tenant.Branding];
            string payload = branding.Payload.GetRawText();
            TenantSettingsDocument? current =
                await tenantDocuments.GetTrackedByTenantAndDocumentKey(
                    tenantId,
                    SettingsDocumentKeys.Tenant.Branding,
                    cancellationToken);
            if (current is null)
            {
                current = TenantSettingsDocument.Create(
                    tenantId,
                    SettingsDocumentKeys.Tenant.Branding,
                    branding.SchemaVersion,
                    catalog.DefaultsVersion!,
                    payload);
                current.Id = Guid.CreateVersion7();
                current.CreatedAt = occurredAt;
                current.CreatedBy = actorUserId;
                await tenantDocuments.Create(current);
            }
            else
            {
                current.UpdatePayload(
                    branding.SchemaVersion,
                    catalog.DefaultsVersion!,
                    payload);
                current.UpdatedAt = occurredAt;
                current.UpdatedBy = actorUserId;
                await tenantDocuments.Update(current);
            }
        }

        if (documents.TryGetValue(
                ConfigurationManifestDocumentKeys.TenantPaidEventPolicy,
                out ConfigurationManifestDocumentV1Alpha2? policyDocument))
        {
            PaidEventPolicyVersion instancePolicy =
                await paidEventPolicies.GetActiveInstanceAsync(cancellationToken)
                ?? throw Blocked("configuration_import_instance_policy_missing");
            PaidEventPolicyMutationResult result =
                await paidEventPolicy.ReviseTenantInCurrentTransactionAsync(
                    new TenantPaidEventPolicyMutationInput(
                        tenantId,
                        ConfigurationManifestPaidEventPolicyMapper.ToRevisionDto(
                            DeserializePaidPolicy(policyDocument)),
                        instancePolicy.VersionNumber),
                    cancellationToken);
            Ensure(result.Success, result.FailureCode, result.Message);
        }
    }

    private async Task ApplyLegalAsync(
        LegalDocumentScope scope,
        Guid? tenantId,
        IReadOnlyDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2> source,
        string artifactDigest,
        DateTime occurredAt,
        CancellationToken cancellationToken)
    {
        foreach ((string code, ConfigurationManifestLegalDocumentV1Alpha2 portable)
                 in source.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!LegalDocumentKindCatalog.TryGet(
                    code,
                    out LegalDocumentKindDescriptor? descriptor)
                || descriptor is null
                || descriptor.Scope != scope
                || !Enum.TryParse(
                    portable.Audience,
                    ignoreCase: false,
                    out LegalDocumentAudience audience))
            {
                throw Blocked("configuration_import_legal_document_invalid");
            }

            LegalDocumentLocalizedSource[] localizations = portable.Localizations
                .Select(localization => LegalDocumentLocalizedSource.Create(
                    localization.LanguageTag,
                    localization.Title,
                    localization.Summary,
                    localization.Markdown))
                .ToArray();
            LegalDocumentTemplateProvenance? provenance = portable.TemplateProvenance
                is null
                ? null
                : LegalDocumentTemplateProvenance.Create(
                    portable.TemplateProvenance.TemplateId,
                    portable.TemplateProvenance.TemplateVersion,
                    Enum.Parse<LegalDocumentTemplateSourceKind>(
                        portable.TemplateProvenance.SourceKind,
                        ignoreCase: false),
                    portable.TemplateProvenance.LicenseExpression,
                    portable.TemplateProvenance.ReviewReference);
            LegalDocument? current = await legalDocuments.GetForUpdateAsync(
                scope,
                tenantId,
                descriptor.Kind,
                cancellationToken);
            string sourceOrigin = $"sha256:{artifactDigest}";
            if (current is null)
            {
                await legalDocuments.AddAsync(
                    LegalDocument.CreateImportedDraft(
                        scope,
                        tenantId,
                        descriptor.Kind,
                        audience,
                        localizations,
                        provenance,
                        sourceOrigin,
                        portable.RequiresFreshAcceptance,
                        occurredAt),
                    cancellationToken);
            }
            else
            {
                current.CreateImportedRevision(
                    audience,
                    localizations,
                    provenance,
                    sourceOrigin,
                    portable.RequiresFreshAcceptance,
                    occurredAt);
            }
        }
    }

    private static ConfigurationManifestTenantSettingMutation Mutation(
        KeyValuePair<string, JsonElement> pair) =>
        new(pair.Key, pair.Value.GetRawText());

    private static ConfigurationManifestPaidEventPolicyPayloadV1Alpha2
        DeserializePaidPolicy(ConfigurationManifestDocumentV1Alpha2 document) =>
        document.Payload.Deserialize(
            ConfigurationPortabilityJsonContext.Default
                .ConfigurationManifestPaidEventPolicyPayloadV1Alpha2)
        ?? throw Blocked("configuration_import_paid_policy_invalid");

    private static string ResolveTargetSlug(
        string sourceSlug,
        IReadOnlyDictionary<string, string> mappings)
    {
        string mapped = mappings.TryGetValue(sourceSlug, out string? direct)
            ? direct
            : mappings.TryGetValue($"tenant:{sourceSlug}", out string? qualified)
                ? qualified
                : sourceSlug;
        return mapped.StartsWith("tenant:", StringComparison.Ordinal)
            ? mapped["tenant:".Length..]
            : mapped;
    }

    private static void AddTenantLocks(
        ISet<string> resources,
        string targetIdentity,
        ConfigurationManifestTenantSpecV1Alpha2 source,
        IReadOnlySet<string> selected,
        Guid? targetTenantId = null)
    {
        if (selected.Contains("tenant.settings"))
            resources.UnionWith(source.Settings.Keys);
        if (selected.Contains("tenant.documents"))
        {
            resources.UnionWith(TenantBrandingGovernanceMutationLockKeys.All);
            if (source.Documents.ContainsKey(
                    ConfigurationManifestDocumentKeys.TenantPaidEventPolicy))
            {
                resources.Add(PaidEventPolicyMutationLockKeys.Instance);
                resources.Add(targetTenantId.HasValue
                    ? PaidEventPolicyMutationLockKeys.ForTenant(targetTenantId.Value)
                    : $"paid-event-policy:tenant-slug:{targetIdentity}");
            }
        }
        AddLegalLocks(
            resources,
            $"tenant:{targetIdentity}",
            source.LegalDocuments,
            selected.Contains("tenant.legal_documents"));
    }

    private static void AddTenantLocks(
        ISet<string> resources,
        string targetIdentity,
        TenantConfigurationPackageSpecV1Alpha2 source,
        IReadOnlySet<string> selected,
        Guid targetTenantId) =>
        AddTenantLocks(
            resources,
            targetIdentity,
            new ConfigurationManifestTenantSpecV1Alpha2
            {
                DisplayName = source.DisplayName,
                Settings = source.Settings,
                Documents = source.Documents,
                LegalDocuments = source.LegalDocuments
            },
            selected,
            targetTenantId);

    private static void AddLegalLocks(
        ISet<string> resources,
        string authority,
        IReadOnlyDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2> legal,
        bool selected)
    {
        if (!selected)
            return;
        foreach (string kind in legal.Keys)
            resources.Add($"legal-document:{authority}:{kind}");
    }

    private static void Ensure(bool success, string? failureCode, string message)
    {
        if (!success)
            throw Blocked(failureCode ?? ConfigurationImportFailureCodes.ApplyBlocked, message);
    }

    private static ConfigurationImportSessionException Blocked(
        string failureCode,
        string? message = null) =>
        new(failureCode);
}
