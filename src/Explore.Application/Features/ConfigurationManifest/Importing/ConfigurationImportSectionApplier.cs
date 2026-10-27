namespace Explore.Application.Features.ConfigurationManifest.Importing;

using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Application.Features.ConfigurationManifest.Compilation;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.PaidEventPolicies;
using Explore.Application.Notifications;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Documents;

public sealed class ConfigurationImportSectionApplier(
    IConfigurationManifestInstanceSettingMutationBoundary instanceSettings,
    IConfigurationManifestTenantSettingMutationBoundary tenantSettings,
    IConfigurationImportTenantIdentityMutationBoundary tenantIdentity,
    IPublicationPolicyMutationBoundary publicationPolicy,
    IEventResourceSettingsWriter eventResourceSettingsWriter,
    IPaidEventPolicyMutationBoundary paidEventPolicy,
    IPaidEventPolicyRepository paidEventPolicies,
    ITenantRepository tenants,
    ITenantSettingsDocumentRepository tenantDocuments,
    ILegalDocumentRepository legalDocuments)
{
    internal async Task<OriginalSections> CaptureOriginalApplyAsync(
        Guid operationId, ConfigurationImportTarget target, ReadOnlyMemory<byte> sourceBytes,
        ConfigurationImportPreviewRequest request, HumanConfigurationImportAuthority authority,
        DateTime occurredAt, string artifactDigest, ConfigurationImportArtifactParser parser,
        IHumanConfigurationImportPublicationFactory publicationFactory,
        OriginalLockGroups originalLocks,
        CancellationToken cancellationToken)
    {
        if (authority.Target != target)
            throw new UnauthorizedAccessException("Import observation must retain the original target.");
        var compiled = await CompileSectionsAsync(target, sourceBytes, request, authority.Subject,
            authority.Subject.PrincipalId, occurredAt, artifactDigest, parser, cancellationToken,
            effects => publicationFactory.ObservePublicationAsync(authority, operationId, artifactDigest,
                effects, occurredAt, cancellationToken), originalLocks.Targets);
        return new(this, operationId, authority.Subject, target, artifactDigest, compiled);
    }

    internal HumanConfigurationImportApply PrepareAdmittedApply(
        OriginalSections original, Func<CancellationToken, Task> validateSources)
    {
        if (!ReferenceEquals(original.Compiler, this) || Interlocked.Exchange(ref original.Consumed, 1) != 0)
            throw new UnauthorizedAccessException("Only the original section compiler may admit its observation once.");
        var compiled = original.Compiled;
        var publications = compiled.PublicationStore is null ? ImmutableArray<HumanConfigurationImportPublication>.Empty
            : compiled.Effects.Where(effect => PublicationPolicySettingKeys.All.Contains(effect.Key, StringComparer.Ordinal)
                    && effect.Kind == HumanSettingsMutationKind.SetValue)
                .GroupBy(effect => effect.TenantId)
                .Select(group => new HumanConfigurationImportPublication(original.OperationId, original.Subject,
                    original.Target, original.Digest, group.Key,
                    group.Select(effect => new PublicationPolicySettingMutation(effect.Key,
                        PublicationPolicyMutationKind.Set, effect.Value, group.Key,
                        group.Key.HasValue ? null : false)).ToImmutableArray(), compiled.PublicationStore))
                .ToImmutableArray();
        return new(original.OperationId, original.Subject, original.Target, original.Digest,
            compiled.Effects, validateSources, token => compiled.Apply(publications, token),
            publications, compiled.PublicationStore);
    }

    public Task<ImmutableArray<SettingChangedNotification>> ApplyAsync(
        ConfigurationImportTarget target,
        ReadOnlyMemory<byte> sourceBytes,
        ConfigurationImportPreviewRequest request,
        HumanCommitSubject subject,
        Guid actorUserId,
        DateTime occurredAt,
        string artifactDigest,
        ConfigurationImportArtifactParser parser,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return ApplyCoreAsync(target, sourceBytes, request, subject, actorUserId,
            occurredAt, artifactDigest, parser, cancellationToken);
    }

    internal Task<ImmutableArray<SettingChangedNotification>> ApplySetupLiveAsync(
        ConfigurationImportTarget target,
        ReadOnlyMemory<byte> sourceBytes,
        ConfigurationImportPreviewRequest request,
        Guid actorUserId,
        DateTime occurredAt,
        string artifactDigest,
        ConfigurationImportArtifactParser parser,
        CancellationToken cancellationToken,
        List<LegalDocument>? ownedLegalDocuments = null)
    {
        if (target.Scope != ConfigurationImportScope.Tenant
            || target.GetSessionStorageKey() == target.AuthorityKey)
            throw new InvalidOperationException("Setup sections require the enrollment-bound target.");
        return ApplyCoreAsync(target, sourceBytes, request, null, actorUserId,
            occurredAt, artifactDigest, parser, cancellationToken);
    }

    private async Task<ImmutableArray<SettingChangedNotification>> ApplyCoreAsync(
        ConfigurationImportTarget target,
        ReadOnlyMemory<byte> sourceBytes,
        ConfigurationImportPreviewRequest request,
        HumanCommitSubject? subject,
        Guid actorUserId,
        DateTime occurredAt,
        string artifactDigest,
        ConfigurationImportArtifactParser parser,
        CancellationToken cancellationToken)
    {
        var compiled = await CompileSectionsAsync(target, sourceBytes, request, subject, actorUserId,
            occurredAt, artifactDigest, parser, cancellationToken);
        return await compiled.Apply([], cancellationToken);
    }

    private async Task<CompiledSections> CompileSectionsAsync(
        ConfigurationImportTarget target, ReadOnlyMemory<byte> sourceBytes,
        ConfigurationImportPreviewRequest request, HumanCommitSubject? subject, Guid actorUserId,
        DateTime occurredAt, string artifactDigest, ConfigurationImportArtifactParser parser,
        CancellationToken cancellationToken,
        Func<ImmutableArray<HumanConfigurationImportSettingEffect>, Task<IHumanConfigurationImportPublicationStore>>?
            observePublication = null,
        ImmutableDictionary<string, Guid>? originalTargets = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(parser);
        // Parse a private byte snapshot and persist the very same parsed objects and target IDs
        // used for effect compilation. Neither caller memory nor a later mapping lookup can
        // broaden the active proof after its artifact digest has been checked.
        sourceBytes = sourceBytes.ToArray();
        var selected = request.SelectedSectionKeys.ToImmutableHashSet(StringComparer.Ordinal);
        var mappings = request.Mappings.ToImmutableDictionary(StringComparer.Ordinal);
        SettingsMutationOperation operation = target.Scope == ConfigurationImportScope.Instance
            ? SettingsMutationOperation.ConfigurationInstanceImport
            : SettingsMutationOperation.ConfigurationTenantImport;
        ConfigurationManifestInstanceV1Alpha2? instance = null;
        var tenantSections = ImmutableArray.CreateBuilder<(Guid TenantId, ConfigurationManifestTenantSpecV1Alpha2 Spec)>();
        var effects = ImmutableArray.CreateBuilder<HumanConfigurationImportSettingEffect>();
        if (target.Scope == ConfigurationImportScope.Instance)
        {
            var parsed = parser.Parse(sourceBytes);
            RequireDigest(parsed.Sha256Digest);
            instance = parsed.Manifest.Spec.Instance;
            if (selected.Contains("instance.settings"))
                AddEffects(null, instance.Settings);
            if (selected.Any(section => section.StartsWith("tenant.", StringComparison.Ordinal)))
            {
                var sources = parsed.Manifest.Spec.Tenants;
                string[] slugs = sources.Select(source => ResolveTargetSlug(source.Metadata.Name, mappings)).ToArray();
                if (slugs.Distinct(StringComparer.Ordinal).Count() != slugs.Length)
                    throw Blocked("configuration_import_mapping_target_duplicate");
                var bySlug = originalTargets ?? (await tenants.GetBySlugsAsNoTrackingAsync(slugs, cancellationToken))
                    .ToImmutableDictionary(tenant => tenant.Slug, tenant => tenant.Id, StringComparer.Ordinal);
                foreach (var source in sources)
                {
                    if (!bySlug.TryGetValue(ResolveTargetSlug(source.Metadata.Name, mappings), out Guid tenantId))
                        throw Blocked("configuration_import_mapping_target_missing");
                    tenantSections.Add((tenantId, source.Spec));
                }
            }
        }
        else
        {
            var parsed = parser.ParseTenantPackage(sourceBytes);
            RequireDigest(parsed.Sha256Digest);
            var spec = parsed.Package.Spec;
            tenantSections.Add((target.TenantId
                ?? throw new ConfigurationImportSessionException(ConfigurationImportFailureCodes.TargetMismatch),
                new ConfigurationManifestTenantSpecV1Alpha2
                {
                    DisplayName = spec.DisplayName,
                    Settings = spec.Settings,
                    Documents = spec.Documents,
                    LegalDocuments = spec.LegalDocuments
                }));
        }
        var resolvedTenants = tenantSections.ToImmutable();
        if (selected.Contains("tenant.settings"))
            foreach (var tenant in resolvedTenants)
                AddEffects(tenant.TenantId, tenant.Spec.Settings);

        var frozenEffects = effects.ToImmutable();
        var publicationEffects = frozenEffects.Where(effect =>
            PublicationPolicySettingKeys.All.Contains(effect.Key, StringComparer.Ordinal)).ToImmutableArray();
        var publicationStore = observePublication is not null && !publicationEffects.IsEmpty
            ? await observePublication(publicationEffects) : null;
        return new(frozenEffects, publicationStore, async (publications, token) =>
        {
            var notifications = ImmutableArray.CreateBuilder<SettingChangedNotification>();
            var ownedLegalDocuments = new List<LegalDocument>();
            Guid transactionId = legalDocuments.CaptureConfigurationImportTransaction();
            if (instance is not null)
                await ApplyInstanceAsync(instance, selected, subject!, operation, actorUserId,
                    occurredAt, artifactDigest, notifications, ownedLegalDocuments, token,
                    publications.SingleOrDefault(publication => publication.TenantId is null));
            foreach (var tenant in resolvedTenants)
                await ApplyTenantAsync(tenant.TenantId, tenant.Spec.DisplayName, tenant.Spec.Settings,
                    tenant.Spec.Documents, tenant.Spec.LegalDocuments, selected, subject, operation,
                    actorUserId, occurredAt, artifactDigest, notifications, ownedLegalDocuments, token,
                    publications.SingleOrDefault(publication => publication.TenantId == tenant.TenantId));
            if (ownedLegalDocuments.Count > 0)
                await legalDocuments.FlushConfigurationImportInCurrentTransactionAsync(transactionId, target,
                    target.Scope == ConfigurationImportScope.Instance
                        ? resolvedTenants.Select(tenant => tenant.TenantId).Distinct().ToArray() : [],
                    ownedLegalDocuments, token);
            return notifications.ToImmutable();
        });

        void RequireDigest(string digest)
        {
            if (!string.Equals(digest, artifactDigest, StringComparison.Ordinal))
                throw new ConfigurationImportSessionException(ConfigurationImportFailureCodes.ArtifactIntegrityInvalid);
        }

        void AddEffects(Guid? tenantId, IReadOnlyDictionary<string, JsonElement> settings)
        {
            var scope = tenantId.HasValue ? SettingScope.Tenant : SettingScope.Instance;
            foreach (var pair in settings.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                effects.Add(new(scope, tenantId, pair.Key, HumanSettingsMutationKind.SetValue, pair.Value.GetRawText()));
                // These exact import boundaries replace the lock field with false. Resource
                // value writes and tenant coordinated writes preserve it and get no lock grant.
                if (!EventResourceSettingMutationGuard.Handles(pair.Key)
                    && (!tenantId.HasValue
                        || !ConfigurationManifestCatalog.TenantSettings[pair.Key].Definition.RequiresCoordinatedMutation))
                    effects.Add(new(scope, tenantId, pair.Key, HumanSettingsMutationKind.SetLock, IsLocked: false));
            }
        }
    }

    internal sealed record CompiledSections(ImmutableArray<HumanConfigurationImportSettingEffect> Effects,
        IHumanConfigurationImportPublicationStore? PublicationStore,
        Func<ImmutableArray<HumanConfigurationImportPublication>, CancellationToken,
            Task<ImmutableArray<SettingChangedNotification>>> Apply);

    internal sealed class OriginalSections(ConfigurationImportSectionApplier compiler, Guid operationId,
        HumanCommitSubject subject, ConfigurationImportTarget target, string digest, CompiledSections compiled)
    {
        internal ConfigurationImportSectionApplier Compiler => compiler;
        internal Guid OperationId => operationId;
        internal HumanCommitSubject Subject => subject;
        internal ConfigurationImportTarget Target => target;
        internal string Digest => digest;
        internal CompiledSections Compiled => compiled;
        internal int Consumed;
    }

    public async Task<IReadOnlyList<IReadOnlyList<string>>> CompileLockGroupsAsync(
        ConfigurationImportTarget target,
        ReadOnlyMemory<byte> sourceBytes,
        ConfigurationImportPreviewRequest request,
        ConfigurationImportArtifactParser parser,
        CancellationToken cancellationToken) =>
        (await CaptureLockGroupsAsync(target, sourceBytes, request, parser, cancellationToken)).Groups;

    internal sealed record OriginalLockGroups(IReadOnlyList<IReadOnlyList<string>> Groups,
        ImmutableDictionary<string, Guid> Targets);

    internal async Task<OriginalLockGroups> CaptureLockGroupsAsync(
        ConfigurationImportTarget target,
        ReadOnlyMemory<byte> sourceBytes,
        ConfigurationImportPreviewRequest request,
        ConfigurationImportArtifactParser parser,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        var resources = new HashSet<string>(StringComparer.Ordinal);
        var retainedTargets = ImmutableDictionary<string, Guid>.Empty;
        var selected = request.SelectedSectionKeys.ToHashSet(StringComparer.Ordinal);
        if (target.Scope == ConfigurationImportScope.Instance)
        {
            ConfigurationManifestV1Alpha2 manifest = parser.Parse(sourceBytes).Manifest;
            if (selected.Contains("instance.settings"))
            {
                resources.UnionWith(manifest.Spec.Instance.Settings.Keys);
                AddResourceSettingLocks(
                    resources,
                    manifest.Spec.Instance.Settings.Keys);
            }
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
                retainedTargets = targetIds.ToImmutableDictionary(StringComparer.Ordinal);
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

        return new(new IReadOnlyList<string>[]
        {
            [$"!configuration-import:{target.AuthorityKey}"],
            [.. resources.Order(StringComparer.Ordinal)]
        }, retainedTargets);
    }

    private async Task ApplyInstanceAsync(
        ConfigurationManifestInstanceV1Alpha2 source,
        IReadOnlySet<string> selected,
        HumanCommitSubject subject,
        SettingsMutationOperation operation,
        Guid actorUserId,
        DateTime occurredAt,
        string artifactDigest,
        ImmutableArray<SettingChangedNotification>.Builder deferredNotifications,
        List<LegalDocument>? ownedLegalDocuments,
        CancellationToken cancellationToken,
        HumanConfigurationImportPublication? publication = null)
    {
        if (selected.Contains("instance.settings") && source.Settings.Count > 0)
        {
            KeyValuePair<string, JsonElement>[] resourceSettings = source.Settings
                .Where(pair => EventResourceSettingMutationGuard.Handles(pair.Key))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToArray();
            if (resourceSettings.Length > 0)
            {
                EventResourceSettingsWriteResult resourceResult =
                    await eventResourceSettingsWriter.ApplyAsync(
                        [.. resourceSettings.Select(pair =>
                            new EventResourceSettingMutation(
                                TenantId: null,
                                pair.Key,
                                EventResourceSettingMutationKind.SetValue,
                                pair.Value.GetRawText()))],
                        subject,
                        operation,
                        actorUserId,
                        cancellationToken);
                EnsureResourceAccepted(resourceResult);
                deferredNotifications.AddRange(
                    resourceResult.DeferredNotifications);
            }

            ConfigurationManifestInstanceSettingMutation[] remaining =
            [.. source.Settings
                .Where(pair => !EventResourceSettingMutationGuard.Handles(pair.Key)
                    && (publication is null || !PublicationPolicySettingKeys.All.Contains(pair.Key, StringComparer.Ordinal)))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ConfigurationManifestInstanceSettingMutation(
                    pair.Key,
                    pair.Value.GetRawText()))];
            if (publication is not null)
            {
                var published = await publicationPolicy.ApplyImportInCurrentTransactionAsync(publication, cancellationToken);
                Ensure(published.Success, published.FailureCode, published.Message);
                deferredNotifications.AddRange(published.DeferredNotifications);
            }
            if (remaining.Length > 0)
            {
                ConfigurationManifestInstanceSettingMutationResult result =
                    await instanceSettings.ApplyInCurrentTransactionAsync(
                        new ConfigurationManifestInstanceSettingMutationInput(
                            remaining,
                            actorUserId,
                            occurredAt),
                        cancellationToken);
                Ensure(result.Success, result.FailureCode, result.Message);
            }
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
                actorUserId,
                occurredAt,
                ownedLegalDocuments,
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
        HumanCommitSubject? subject,
        SettingsMutationOperation operation,
        Guid actorUserId,
        DateTime occurredAt,
        string artifactDigest,
        ImmutableArray<SettingChangedNotification>.Builder deferredNotifications,
        List<LegalDocument>? ownedLegalDocuments,
        CancellationToken cancellationToken,
        HumanConfigurationImportPublication? publication = null)
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
                    .Definition.RequiresCoordinatedMutation
                    && !EventResourceSettingMutationGuard.Handles(pair.Key))
                .Select(Mutation)
                .ToArray();
            if (guarded.Length > 0)
            {
                PublicationPolicyMutationResult result = publication is not null
                    ? await publicationPolicy.ApplyImportInCurrentTransactionAsync(publication, cancellationToken)
                    : await publicationPolicy.ApplyTenantInCurrentTransactionAsync(
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
                deferredNotifications.AddRange(result.DeferredNotifications);
            }

            EventResourceSettingMutation[] resourceMutations = settings
                .Where(pair => EventResourceSettingMutationGuard.Handles(pair.Key))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new EventResourceSettingMutation(
                    tenantId,
                    pair.Key,
                    EventResourceSettingMutationKind.SetValue,
                    pair.Value.GetRawText()))
                .ToArray();
            if (resourceMutations.Length > 0)
            {
                EventResourceSettingsWriteResult resourceResult = subject is null
                    ? await eventResourceSettingsWriter.ApplyInternalAsync(
                        [.. resourceMutations], actorUserId, cancellationToken)
                    : await eventResourceSettingsWriter.ApplyAsync(
                        [.. resourceMutations], subject, operation, actorUserId, cancellationToken);
                EnsureResourceAccepted(resourceResult);
                deferredNotifications.AddRange(
                    resourceResult.DeferredNotifications);
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
                actorUserId,
                occurredAt,
                ownedLegalDocuments,
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
            await tenantDocuments.ApplyBrandingImportAsync(
                tenantId,
                branding.SchemaVersion,
                catalog.DefaultsVersion!,
                payload,
                actorUserId,
                occurredAt,
                cancellationToken);
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
        Guid actorUserId,
        DateTime occurredAt,
        List<LegalDocument>? ownedLegalDocuments,
        CancellationToken cancellationToken)
    {
        if (source.Values.Any(document => document.JurisdictionAssumptions.Count != 0
                || document.ChangeSummary is not null))
            throw Blocked("configuration_import_legal_metadata_not_supported");

        foreach ((string code, ConfigurationManifestLegalDocumentV1Alpha2 portable)
                 in source.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!Enum.TryParse<LegalDocumentKind>(portable.Kind, ignoreCase: false, out var kind)
                || !Enum.IsDefined(kind)
                || code != kind.ToString()
                || LegalDocumentKindCatalog.Get(kind).Scope != scope
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
                kind,
                cancellationToken);
            string sourceOrigin = $"sha256:{artifactDigest}";
            if (current is null)
            {
                current = LegalDocument.CreateImportedDraft(
                    scope,
                    tenantId,
                    kind,
                    audience,
                    localizations,
                    provenance,
                    sourceOrigin,
                    portable.RequiresFreshAcceptance,
                    occurredAt);
                current.CreatedBy = actorUserId;
                await legalDocuments.AddAsync(current, cancellationToken);
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
                current.UpdatedBy = actorUserId;
            }
            ownedLegalDocuments?.Add(current);
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
        {
            resources.UnionWith(source.Settings.Keys);
            AddResourceSettingLocks(resources, source.Settings.Keys);
        }
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

    private static void AddResourceSettingLocks(
        ISet<string> resources,
        IEnumerable<string> settingKeys)
    {
        if (settingKeys.Any(key => PublicationPolicySettingKeys.All.Contains(key, StringComparer.Ordinal)))
            resources.UnionWith(PublicationPolicySettingKeys.All);
        if (settingKeys.Any(EventResourceSettingMutationGuard.Handles))
            resources.UnionWith(EventResourceSettingMutationGuard.Keys);
    }

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

    private static void EnsureResourceAccepted(
        EventResourceSettingsWriteResult result)
    {
        if (!result.Success)
        {
            throw Blocked(
                result.FailureCode ?? ConfigurationImportFailureCodes.ApplyBlocked,
                "The event-resource settings conflict with current governance ceilings or locks.");
        }
    }

    private static ConfigurationImportSessionException Blocked(
        string failureCode,
        string? message = null) =>
        new(failureCode);
}
