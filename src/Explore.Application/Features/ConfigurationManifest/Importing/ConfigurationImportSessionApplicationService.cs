namespace Explore.Application.Features.ConfigurationManifest.Importing;

using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Domain;
using MediatR;

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
    IRequestHandler<
        ExportConfigurationManifestQuery,
        ConfigurationManifestExportResult> currentStateExporter,
    ITenantRepository tenants,
    TimeProvider timeProvider)
{
    public async Task<ConfigurationImportSessionCreatedResult> CreateInstanceAsync(
        ReadOnlyMemory<byte> artifact,
        CancellationToken cancellationToken)
    {
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
        ConfigurationImportParsedTenantPackage parsed =
            parser.ParseTenantPackage(artifact);
        return await CreateAsync(
            ConfigurationImportTarget.ForTenant(tenantId),
            artifact,
            ConfigurationImportArtifactSnapshotFactory.FromTenantPackage(
                parsed.Package),
            cancellationToken);
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

    public Task CancelInstanceAsync(
        Guid sessionId,
        string accessToken,
        CancellationToken cancellationToken) =>
        manager.CancelAsync(
            sessionId,
            ConfigurationImportTarget.ForInstance(),
            accessToken,
            UtcNow(),
            cancellationToken);

    public Task CancelTenantAsync(
        Guid tenantId,
        Guid sessionId,
        string accessToken,
        CancellationToken cancellationToken) =>
        manager.CancelAsync(
            sessionId,
            ConfigurationImportTarget.ForTenant(tenantId),
            accessToken,
            UtcNow(),
            cancellationToken);

    private async Task<ConfigurationImportSessionCreatedResult> CreateAsync(
        ConfigurationImportTarget target,
        ReadOnlyMemory<byte> artifact,
        IEnumerable<ConfigurationImportSectionSnapshot> availableSections,
        CancellationToken cancellationToken)
    {
        ConfigurationImportSessionCreated created = await manager.CreateAsync(
            target,
            artifact,
            UtcNow(),
            ConfigurationImportSessionLimits.DefaultSessionLifetime,
            cancellationToken);
        return new ConfigurationImportSessionCreatedResult(
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
    }

    private async Task<ConfigurationImportPreviewResult> PreviewAsync(
        Guid sessionId,
        ConfigurationImportTarget target,
        string accessToken,
        ConfigurationImportPreviewRequest request,
        CancellationToken cancellationToken)
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

        DateTime occurredAt = UtcNow();
        ConfigurationImportAuthorizedArtifact source =
            await manager.ReadArtifactForPreviewAsync(
                sessionId,
                target,
                accessToken,
                occurredAt,
                cancellationToken);
        ConfigurationImportPreviewPreparation preparation =
            await PreparePreviewAsync(
                target,
                source,
                request,
                cancellationToken);
        ConfigurationImportPreview preview;
        try
        {
            preview = await manager.PreparePreviewAsync(
                sessionId,
                target,
                accessToken,
                preparation.Input,
                occurredAt,
                cancellationToken);
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
            await currentStateExporter.Handle(
                new ExportConfigurationManifestQuery(targetView),
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
