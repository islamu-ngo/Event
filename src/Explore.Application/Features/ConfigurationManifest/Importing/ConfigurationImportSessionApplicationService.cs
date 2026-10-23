namespace Explore.Application.Features.ConfigurationManifest.Importing;

using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Application;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Domain;

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

        return new ConfigurationImportPreviewResult(
            sessionId,
            target.Scope,
            target.TenantId,
            ConfigurationImportSessionState.PreviewReady,
            source.ExpiresAt,
            preview.IsApplyReady,
            preview.Items);
    }

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
        CancellationToken cancellationToken)
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

        ConfigurationManifestExportResult current =
            await currentState.ReadImportSnapshotAsync(
                targetView, target.Scope == ConfigurationImportScope.Instance ? null : target.TenantId,
                cancellationToken);
        ConfigurationImportParsedArtifact parsedTarget =
            parser.Parse(current.Utf8Json);
        ReadOnlyMemory<byte> currentTargetArtifact = current.Utf8Json;

        ImmutableArray<ConfigurationImportSectionSnapshot> sourceSections;
        ImmutableArray<ConfigurationImportSectionSnapshot> targetSections;
        if (target.Scope == ConfigurationImportScope.Instance)
        {
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
            targetSections =
                ConfigurationImportArtifactSnapshotFactory.FromManifestTenant(
                    parsedTarget.Manifest,
                    tenant.Slug);
            ConfigurationManifestTenantV1Alpha2 currentTenant =
                parsedTarget.Manifest.Spec.Tenants.Single(candidate =>
                    string.Equals(
                        candidate.Metadata.Name,
                        tenant.Slug,
                        StringComparison.Ordinal));
            currentTargetArtifact = TenantConfigurationPackageSerializer.Serialize(
                TenantConfigurationPackageSerializer.Create(
                    parsedTarget.Manifest,
                    currentTenant));
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
                source.ExpiresAt);
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
