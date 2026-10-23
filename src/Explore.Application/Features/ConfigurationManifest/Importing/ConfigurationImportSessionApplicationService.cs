namespace Explore.Application.Features.ConfigurationManifest.Importing;

using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Features.ConfigurationManifest.Application;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Domain;
using Explore.Application.Features.ConfigurationManifest.Moderation;
using System.Text.Json;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ConfigurationImportPreviewRequest
{
    [Required]
    public required IReadOnlyList<string> SelectedSectionKeys { get; init; }

    [Required]
    public required IReadOnlyDictionary<string, string> Mappings { get; init; }

    public required ConfigurationImportApplyMode ApplyMode { get; init; }

    [Required]
    public required IReadOnlyList<string> GrantedApprovalCodes { get; init; }

    public ConfigurationImportPreviewRequest Capture()
    {
        if (SelectedSectionKeys is null || Mappings is null || GrantedApprovalCodes is null
            || !Enum.IsDefined(ApplyMode))
            throw new ConfigurationImportSessionException(ConfigurationImportFailureCodes.ContractInvalid);
        return new()
        {
            SelectedSectionKeys = SelectedSectionKeys.ToImmutableArray(),
            Mappings = Mappings.ToImmutableDictionary(StringComparer.Ordinal),
            ApplyMode = ApplyMode,
            GrantedApprovalCodes = GrantedApprovalCodes.ToImmutableArray()
        };
    }

    public override string ToString() =>
        nameof(ConfigurationImportPreviewRequest);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ConfigurationImportApplyRequest
{
    [Required]
    public required ConfigurationImportPreviewRequest Preview { get; init; }

    public Guid? RollbackOfOperationId { get; init; }

    public Guid? ManagedScheduleId { get; init; }

    public override string ToString() => nameof(ConfigurationImportApplyRequest);
}

public sealed record ConfigurationImportSessionCreatedResult(
    Guid SessionId,
    string AccessToken,
    ConfigurationImportScope TargetScope,
    Guid? TargetTenantId,
    ConfigurationImportSessionState State,
    DateTime ExpiresAt,
    int ArtifactByteLength,
    ImmutableArray<string> AvailableSectionKeys)
{
    public override string ToString() =>
        nameof(ConfigurationImportSessionCreatedResult);
}

public sealed record ConfigurationImportPreviewResult(
    Guid SessionId,
    ConfigurationImportScope TargetScope,
    Guid? TargetTenantId,
    ConfigurationImportSessionState State,
    DateTime ExpiresAt,
    bool IsApplyReady,
    ImmutableArray<ConfigurationImportPreviewItem> Items)
{
    public ImmutableArray<ConfigurationImportModerationTargetPreview> Moderation { get; init; } = [];

    [JsonIgnore]
    public ConfigurationImportPreviewDisclosure? Disclosure { get; init; }

    public override string ToString() =>
        nameof(ConfigurationImportPreviewResult);
}

internal sealed record ConfigurationImportPreviewPreparation(
    ConfigurationImportPreviewInput Input,
    ReadOnlyMemory<byte> CurrentTargetArtifact);

public sealed class ConfigurationImportSessionApplicationService(
    ConfigurationImportSessionManager manager,
    ConfigurationImportArtifactParser parser,
    ConfigurationManifestCurrentStateReader currentState,
    ITenantRepository tenants,
    TimeProvider timeProvider,
    ModerationConfigurationImportAdapter moderation,
    IPrivacyIdentityFenceAuthority privacy,
    IUnitOfWork unit,
    IHumanCommitScopePreparation humanScope,
    IHumanConfigurationImportAdmission admission)
{
    public async Task<ConfigurationImportSessionCreatedResult> CreateInstanceAsync(
        ReadOnlyMemory<byte> artifact,
        CancellationToken cancellationToken)
    {
        artifact = artifact.ToArray();
        ConfigurationImportParsedArtifact parsed = parser.Parse(artifact);
        return await CreateAsync(
            ConfigurationImportTarget.ForInstance(),
            artifact,
            ConfigurationImportArtifactSnapshotFactory.FromManifest(
                parsed.Manifest),
            cancellationToken);
    }

    public async Task<ConfigurationImportSessionCreatedResult> CreateTenantAsync(
        Guid tenantId,
        ReadOnlyMemory<byte> artifact,
        CancellationToken cancellationToken)
    {
        artifact = artifact.ToArray();
        ConfigurationImportParsedTenantPackage parsed = parser.ParseTenantPackage(artifact);
        return await CreateAsync(ConfigurationImportTarget.ForTenant(tenantId), artifact,
            ConfigurationImportArtifactSnapshotFactory.FromTenantPackage(parsed.Package), cancellationToken);
    }

    internal async Task<ConfigurationImportSessionCreatedResult> CreateSetupLiveTenantAsync(
        ConfigurationImportTarget target,
        ReadOnlyMemory<byte> artifact,
        CancellationToken cancellationToken)
    {
        ConfigurationImportParsedTenantPackage parsed =
            parser.ParseTenantPackage(artifact);
        var created = await manager.CreateSetupLiveAsync(target, artifact, UtcNow(),
            ConfigurationImportSessionLimits.DefaultSessionLifetime, cancellationToken);
        return MapCreated(created,
            ConfigurationImportArtifactSnapshotFactory.FromTenantPackage(
                parsed.Package));
    }

    public Task<ConfigurationImportPreviewResult> PreviewInstanceAsync(
        Guid sessionId,
        string accessToken,
        ConfigurationImportPreviewRequest request,
        CancellationToken cancellationToken) =>
        PreviewAsync(
            sessionId,
            ConfigurationImportTarget.ForInstance(),
            accessToken,
            request,
            cancellationToken);

    public Task<ConfigurationImportPreviewResult> PreviewTenantAsync(
        Guid tenantId,
        Guid sessionId,
        string accessToken,
        ConfigurationImportPreviewRequest request,
        CancellationToken cancellationToken) =>
        PreviewAsync(
            sessionId,
            ConfigurationImportTarget.ForTenant(tenantId),
            accessToken,
            request,
            cancellationToken);

    internal Task<ConfigurationImportPreviewResult> PreviewSetupLiveTenantAsync(
        ConfigurationImportTarget target,
        Guid sessionId,
        string accessToken,
        ConfigurationImportPreviewRequest request,
        CancellationToken cancellationToken) =>
        PreviewCoreAsync(null, sessionId, target, accessToken, request, cancellationToken);

    public Task CancelInstanceAsync(
        Guid sessionId,
        string accessToken,
        CancellationToken cancellationToken) =>
        CancelAsync(
            sessionId,
            ConfigurationImportTarget.ForInstance(),
            accessToken,
            cancellationToken);

    public Task CancelTenantAsync(
        Guid tenantId,
        Guid sessionId,
        string accessToken,
        CancellationToken cancellationToken) =>
        CancelAsync(
            sessionId,
            ConfigurationImportTarget.ForTenant(tenantId),
            accessToken,
            cancellationToken);

    private Task CancelAsync(Guid sessionId, ConfigurationImportTarget target, string accessToken,
        CancellationToken cancellationToken) =>
        ExecuteObservationAsync(target, async (owner, token) =>
        {
            await manager.CancelAsync(owner, sessionId, target,
                NormalizeSessionAccessToken(accessToken), UtcNow(), token);
            return true;
        }, cancellationToken);

    private Task<T> ExecuteObservationAsync<T>(ConfigurationImportTarget target,
        Func<IPreparedHumanConfigurationImport, CancellationToken, Task<T>> observe,
        CancellationToken cancellationToken) =>
        admission.ExecuteObservationAsync(new HumanConfigurationImportObservation<T>(
            humanScope.Subject
                ?? throw new UnauthorizedAccessException("Import sessions require their original selected human."),
            target, HumanConfigurationImportObservationAccess.Update, observe), cancellationToken);

    private Task<ConfigurationImportSessionCreatedResult> CreateAsync(
        ConfigurationImportTarget target,
        ReadOnlyMemory<byte> artifact,
        IEnumerable<ConfigurationImportSectionSnapshot> availableSections,
        CancellationToken cancellationToken) =>
        ExecuteObservationAsync(target, async (owner, token) =>
            MapCreated(await manager.CreateAsync(owner, target, artifact, UtcNow(),
                ConfigurationImportSessionLimits.DefaultSessionLifetime, token), availableSections),
            cancellationToken);

    private static ConfigurationImportSessionCreatedResult MapCreated(
        ConfigurationImportSessionCreated created,
        IEnumerable<ConfigurationImportSectionSnapshot> availableSections) =>
        new(
            created.Session.SessionId,
            created.AccessToken,
            created.Session.TargetScope,
            created.Session.TargetTenantId,
            created.Session.State,
            created.Session.ExpiresAt,
            created.Session.ArtifactByteLength,
            [.. availableSections
                .Select(section => section.SectionKey)
                .Order(StringComparer.Ordinal)]);

    private Task<ConfigurationImportPreviewResult> PreviewAsync(
        Guid sessionId,
        ConfigurationImportTarget target,
        string accessToken,
        ConfigurationImportPreviewRequest request,
        CancellationToken cancellationToken)
    {
        ValidatePreviewRequest(request);
        var captured = request.Capture();
        return ExecuteObservationAsync(target, (owner, token) =>
            PreviewCoreAsync(owner, sessionId, target, accessToken, captured, token), cancellationToken);
    }

    private async Task<ConfigurationImportPreviewResult> PreviewCoreAsync(
        IPreparedHumanConfigurationImport? owner,
        Guid sessionId,
        ConfigurationImportTarget target,
        string accessToken,
        ConfigurationImportPreviewRequest request,
        CancellationToken cancellationToken)
    {
        ValidatePreviewRequest(request);

        request = new ConfigurationImportPreviewRequest
        {
            SelectedSectionKeys = request.SelectedSectionKeys.ToImmutableArray(),
            Mappings = request.Mappings.ToImmutableDictionary(StringComparer.Ordinal),
            ApplyMode = request.ApplyMode,
            GrantedApprovalCodes = request.GrantedApprovalCodes.ToImmutableArray()
        };
        DateTime occurredAt = UtcNow();
        accessToken = NormalizeSessionAccessToken(accessToken);
        ConfigurationImportAuthorizedArtifact source = owner is null
            ? await manager.ReadSetupLiveArtifactAsync(sessionId, target, accessToken, occurredAt, cancellationToken)
            : await manager.ReadArtifactForPreviewAsync(owner, sessionId, target, accessToken, occurredAt, cancellationToken);
        ConfigurationImportPreviewPreparation preparation =
            await PreparePreviewAsync(
                target,
                source,
                request,
                cancellationToken);
        ConfigurationImportPreview preview;
        try
        {
            preview = owner is null
                ? await manager.PrepareSetupLivePreviewAsync(sessionId, target, accessToken,
                    preparation.Input, occurredAt, cancellationToken)
                : await manager.PreparePreviewAsync(owner, sessionId, target, accessToken,
                    preparation.Input, occurredAt, cancellationToken);
        }
        catch (ArgumentException)
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ContractInvalid);
        }

        var result = new ConfigurationImportPreviewResult(
            sessionId,
            target.Scope,
            target.TenantId,
            ConfigurationImportSessionState.PreviewReady,
            source.ExpiresAt,
            preview.IsApplyReady,
            preview.Items);
        return preparation.Input.ModerationPreview is null ? result : result with
        {
            Disclosure = new ConfigurationImportPreviewDisclosure(
                (prepare, deliver, token) => DisclosePreviewAsync(
                    target, accessToken, request, preview, result, prepare, deliver, token),
                new DateTimeOffset(result.ExpiresAt), timeProvider, cancellationToken)
        };
    }

    private async Task DisclosePreviewAsync(
        ConfigurationImportTarget target, string accessToken, ConfigurationImportPreviewRequest request,
        ConfigurationImportPreview original, ConfigurationImportPreviewResult result,
        Func<ConfigurationImportPreviewResult, CancellationToken, Task<ReadOnlyMemory<byte>>> prepare,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> deliver, CancellationToken cancellationToken)
    {
        int entered = 0;
        await privacy.ExecuteEnrollmentSerializedAsync(
            token => unit.ExecuteSerializableAsync(async inner =>
            {
                // A provider retry must never replay an output callback, even after a partial write.
                if (Interlocked.Exchange(ref entered, 1) != 0)
                    throw new ConfigurationImportSessionException(ConfigurationImportFailureCodes.StalePreview);
                var current = await ReadCurrentAsync(inner);
                var bytes = await prepare(result with
                {
                    Moderation = Project(current), Disclosure = null
                }, inner);
                inner.ThrowIfCancellationRequested();
                await ReadCurrentAsync(inner);
                inner.ThrowIfCancellationRequested();
                await deliver(bytes, inner);
                inner.ThrowIfCancellationRequested();
                if (UtcNow() >= result.ExpiresAt)
                    throw new OperationCanceledException("Configuration preview expired.");
                return true;
            }, token),
            async token => { await ReadCurrentAsync(token); }, cancellationToken);

        async Task<ModerationConfigurationImportSetPreview> ReadCurrentAsync(CancellationToken token)
        {
            var source = await manager.ReadArtifactForPreviewInCurrentTransactionAsync(
                result.SessionId, target, accessToken, UtcNow(), token);
            var current = await PreparePreviewAsync(target, source, request, token, inCurrentTransaction: true);
            var binding = new ConfigurationImportPreviewComposer().Compose(current.Input).Binding;
            if (!original.Binding.Matches(binding) || UtcNow() >= source.ExpiresAt
                || current.Input.ModerationPreview is null)
                throw new ConfigurationImportSessionException(ConfigurationImportFailureCodes.StalePreview);
            return current.Input.ModerationPreview;
        }
    }

    private static ImmutableArray<ConfigurationImportModerationTargetPreview> Project(
        ModerationConfigurationImportSetPreview preview) => [.. preview.Targets.Select(item => new
        ConfigurationImportModerationTargetPreview(
            item.Target.SectionKey, item.Target.SourceName, item.Target.TargetName,
            item.Preview.Mapping.ExpectedRevision, item.Preview.BaselinePublicationId.HasValue,
            item.Preview.CanApply,
            [.. item.Preview.Diff.Changes.Select(change => new ConfigurationImportModerationChange(
                change.Section, change.Reference?.Code, change.Reference?.Revision,
                change.Kind.ToString(), change.Conflict))],
            [.. (item.Preview.Validation?.Compilation.Diagnostics ?? []).Select(diagnostic => new
                ConfigurationImportModerationDiagnostic(diagnostic.Kind.ToString(), diagnostic.Severity.ToString(),
                    diagnostic.Rule?.Code, diagnostic.Rule?.Revision, diagnostic.ConsumerId,
                    diagnostic.Mode?.ToString()))]))];

    private static void ValidatePreviewRequest(ConfigurationImportPreviewRequest request)
    {
        if (request is null
            || request.SelectedSectionKeys is null
            || request.Mappings is null
            || request.GrantedApprovalCodes is null
            || !Enum.IsDefined(request.ApplyMode))
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ContractInvalid);
        }
    }

    internal async Task<ConfigurationImportPreviewPreparation> PreparePreviewAsync(
        ConfigurationImportTarget target,
        ConfigurationImportAuthorizedArtifact source,
        ConfigurationImportPreviewRequest request,
        CancellationToken cancellationToken,
        bool inCurrentTransaction = false)
    {
        if (request.ApplyMode == ConfigurationImportApplyMode.ReconcileManaged)
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ApplyBlocked);
        }
        ConfigurationImportParsedArtifact? parsedSourceManifest = null;
        ConfigurationImportParsedTenantPackage? parsedSourcePackage = null;
        ConfigurationManifestExportView targetView;
        if (target.Scope == ConfigurationImportScope.Instance)
        {
            parsedSourceManifest = parser.Parse(source.Bytes);
            targetView = ResolveView(
                parsedSourceManifest.Manifest.Metadata.Export?.View);
        }
        else
        {
            parsedSourcePackage = parser.ParseTenantPackage(source.Bytes);
            targetView = ResolveView(
                parsedSourcePackage.Package.Metadata.Export?.View);
        }

        ConfigurationImportParsedArtifact? parsedTarget = null;
        ReadOnlyMemory<byte> currentTargetArtifact;
        ImmutableArray<ConfigurationImportSectionSnapshot> sourceSections;
        ImmutableArray<ConfigurationImportSectionSnapshot> targetSections;
        if (target.Scope == ConfigurationImportScope.Instance)
        {
            ConfigurationManifestExportResult current = await currentState.ReadImportSnapshotAsync(
                targetView, null, cancellationToken);
            parsedTarget = parser.Parse(current.Utf8Json);
            currentTargetArtifact = current.Utf8Json;
            sourceSections =
                ConfigurationImportArtifactSnapshotFactory.FromManifest(
                    parsedSourceManifest!.Manifest);
            targetSections =
                ConfigurationImportArtifactSnapshotFactory.FromManifest(
                    parsedTarget.Manifest);
        }
        else
        {
            Tenant? tenant = await tenants.GetByIdAsNoTrackingAsync(
                target.TenantId
                ?? throw new InvalidOperationException(
                    "Tenant import target is missing."),
                cancellationToken);
            if (tenant is null)
            {
                throw new ConfigurationImportSessionException(
                    ConfigurationImportFailureCodes.ArtifactMissing);
            }

            sourceSections =
                ConfigurationImportArtifactSnapshotFactory.FromTenantPackage(
                    parsedSourcePackage!.Package);
            TenantConfigurationPackageExportResult current = await currentState.ReadTenantInCurrentTransactionAsync(
                tenant.Id, targetView, cancellationToken);
            currentTargetArtifact = current.Utf8Json;
            targetSections =
                ConfigurationImportArtifactSnapshotFactory.FromTenantPackage(
                    parser.ParseTenantPackage(currentTargetArtifact).Package);
        }

        var available = sourceSections
            .Select(section => section.SectionKey)
            .ToHashSet(StringComparer.Ordinal);
        if (request.SelectedSectionKeys.Any(section => !available.Contains(section)))
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ApplyBlocked);
        }

        string[] requiredApprovals = RequiredApprovals(
            request.ApplyMode,
            sourceSections,
            request.SelectedSectionKeys);
        ModerationConfigurationImportSetPreview? nativePreview = null;
        if (request.SelectedSectionKeys.Any(ModerationConfigurationImportSections.IsModeration))
        {
            try
            {
                nativePreview = inCurrentTransaction
                    ? await moderation.PreviewSetInCurrentTransactionAsync(
                        target, source.Bytes, request, source.ModerationReservation, cancellationToken)
                    : await moderation.PreviewSetAsync(
                        target, source.Bytes, request, source.ModerationReservation, cancellationToken);
            }
            catch (ModerationConfigurationImportException exception)
            {
                throw new ConfigurationImportSessionException(
                    exception.Outcome == ModerationPolicyPublicationOutcome.Conflict
                        ? ConfigurationImportFailureCodes.StalePreview
                        : ConfigurationImportFailureCodes.ApplyBlocked);
            }
            if (inCurrentTransaction && (source.ModerationReservation is null
                || source.ModerationReservation.Digest != nativePreview.Reservation.Digest))
                throw new ConfigurationImportSessionException(ConfigurationImportFailureCodes.StalePreview);
            // The current artifact already includes every active tenant's neutral native meaning.
            // Replace only selected destinations with the independently acquired original history.
            // Source comparison uses destination names, not portable source aliases.
            if (target.Scope == ConfigurationImportScope.Instance)
            {
                var sourceManifest = parsedSourceManifest!.Manifest;
                var snapshot = parsedTarget!.Manifest;
                foreach (var item in nativePreview.Targets)
                {
                    var prior = item.Preview.Mapping.Context.Current is { } currentDraft
                        ? ModerationConfigurationImportSections.ToWire(
                            ModerationConfigurationDocumentProjection.FromCompiledDraft(currentDraft)) : null;
                    if (item.Target.SectionKey == ModerationConfigurationImportSections.Instance)
                    {
                        snapshot = snapshot with { Spec = snapshot.Spec with
                            { Instance = snapshot.Spec.Instance with { Moderation = prior } } };
                        if (item.Preview.Document is { } merged)
                            sourceManifest = sourceManifest with { Spec = sourceManifest.Spec with
                                { Instance = sourceManifest.Spec.Instance with
                                    { Moderation = ModerationConfigurationImportSections.ToWire(merged) } } };
                    }
                    else
                    {
                        snapshot = snapshot with { Spec = snapshot.Spec with
                            { Tenants = snapshot.Spec.Tenants.Select(tenant => tenant.Metadata.Name == item.Target.TargetName
                                ? tenant with { Spec = tenant.Spec with { Moderation = prior } } : tenant).ToArray() } };
                    }
                }
                sourceSections = ConfigurationImportArtifactSnapshotFactory.FromManifest(sourceManifest);
                targetSections = ConfigurationImportArtifactSnapshotFactory.FromManifest(snapshot);
                if (nativePreview.Targets.Any(item => item.Target.SectionKey == ModerationConfigurationImportSections.Tenant))
                {
                    var selectedTenants = nativePreview.Targets.Where(item =>
                        item.Target.SectionKey == ModerationConfigurationImportSections.Tenant).ToArray();
                    if (selectedTenants.All(item => item.Preview.Document is not null))
                        sourceSections = [.. sourceSections.Where(section => section.SectionKey != ModerationConfigurationImportSections.Tenant),
                            ModerationConfigurationImportSections.TenantSnapshot(selectedTenants.Select(item =>
                                KeyValuePair.Create(item.Target.TargetName, item.Preview.Document!)))];
                    targetSections = [.. targetSections.Where(section => section.SectionKey != ModerationConfigurationImportSections.Tenant),
                        ModerationConfigurationImportSections.TenantSnapshot(selectedTenants
                            .Where(item => item.Preview.Mapping.Context.Current is not null)
                            .Select(item => KeyValuePair.Create(item.Target.TargetName,
                                ModerationConfigurationDocumentProjection.FromCompiledDraft(item.Preview.Mapping.Context.Current!))))];
                }
                currentTargetArtifact = JsonSerializer.SerializeToUtf8Bytes(snapshot,
                    ConfigurationPortabilityJsonContext.Default.ConfigurationManifestV1Alpha2);
            }
            else
            {
                var preview = nativePreview.Targets.Single().Preview;
                var prior = preview.Mapping.Context.Current is { } currentDraft
                    ? ModerationConfigurationDocumentProjection.FromCompiledDraft(currentDraft) : null;
                if (preview.Document is { } merged)
                    sourceSections = [.. sourceSections.Where(section => section.SectionKey != ModerationConfigurationImportSections.Tenant),
                        ModerationConfigurationImportSections.Snapshot(ModerationConfigurationImportSections.Tenant, merged)];
                targetSections = [.. targetSections.Where(section => section.SectionKey != ModerationConfigurationImportSections.Tenant),
                    .. prior is null ? Array.Empty<ConfigurationImportSectionSnapshot>()
                        : [ModerationConfigurationImportSections.Snapshot(ModerationConfigurationImportSections.Tenant, prior)]];
                var snapshot = parser.ParseTenantPackage(currentTargetArtifact).Package;
                snapshot = snapshot with { Spec = snapshot.Spec with
                    { Moderation = prior is null ? null : ModerationConfigurationImportSections.ToWire(prior) } };
                currentTargetArtifact = JsonSerializer.SerializeToUtf8Bytes(snapshot,
                    ConfigurationPortabilityJsonContext.Default.TenantConfigurationPackageV1Alpha2);
            }
        }
        try
        {
            var input = new ConfigurationImportPreviewInput(
                target,
                source.Digest,
                ConfigurationImportArtifactSnapshotFactory.RevisionDigest(
                    targetSections),
                sourceSections,
                targetSections,
                request.SelectedSectionKeys,
                request.Mappings,
                request.ApplyMode,
                requiredApprovals,
                request.GrantedApprovalCodes,
                source.ExpiresAt,
                nativePreview);
            return new ConfigurationImportPreviewPreparation(
                input,
                currentTargetArtifact);
        }
        catch (ArgumentException)
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ContractInvalid);
        }
    }

    private static string[] RequiredApprovals(
        ConfigurationImportApplyMode applyMode,
        IEnumerable<ConfigurationImportSectionSnapshot> sourceSections,
        IEnumerable<string> selectedSectionKeys)
    {
        var selected = selectedSectionKeys.ToHashSet(StringComparer.Ordinal);
        var approvals = new HashSet<string>(StringComparer.Ordinal);
        if (sourceSections.Any(section =>
                selected.Contains(section.SectionKey)
                && section.SectionKey.EndsWith(
                    ".legal_documents",
                    StringComparison.Ordinal)))
        {
            approvals.Add("legal-review");
        }
        if (applyMode == ConfigurationImportApplyMode
                .ReplacePortableConfiguration)
        {
            approvals.Add("replace-portable-configuration");
        }
        if (applyMode == ConfigurationImportApplyMode.ReconcileManaged)
            approvals.Add("managed-reconciliation");
        return approvals.Order(StringComparer.Ordinal).ToArray();
    }

    private static string NormalizeSessionAccessToken(string accessToken)
    {
        // Preview and cancellation authorize the underlying session secret.
        // Apply separately verifies the prefix against its explicit source operation.
        return accessToken is { Length: > 37 } && accessToken[36] == '.'
            && Guid.TryParseExact(accessToken.AsSpan(0, 36), "D", out Guid sourceOperationId)
            && sourceOperationId.Version == 7
                ? accessToken[37..] : accessToken;
    }

    private DateTime UtcNow() =>
        timeProvider.GetUtcNow().UtcDateTime;

    private static ConfigurationManifestExportView ResolveView(string? view) =>
        string.Equals(
            view,
            ConfigurationManifestExportMetadataValues.PortableView,
            StringComparison.Ordinal)
            ? ConfigurationManifestExportView.Portable
            : ConfigurationManifestExportView.Overrides;
}
