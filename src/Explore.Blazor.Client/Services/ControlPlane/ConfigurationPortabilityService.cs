using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.ControlPlane;
using Explore.Blazor.Client.Contracts.Interop;
using Explore.Blazor.Client.Contracts.Services.ControlPlane;
using Explore.Blazor.Client.Routing.ControlPlane;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Explore.Blazor.Client.Services.ControlPlane;

internal sealed record ConfigurationPortabilityCapabilities(
    bool CanImport,
    bool CanExportOverrides,
    bool CanExportPortable,
    bool CanExportTenantPackage,
    bool CanViewHistory,
    bool CanCreateCloneTarget,
    Guid? TenantId);

internal sealed record ConfigurationImportClientSession(
    Guid SessionId,
    string AccessToken,
    ConfigurationImportScope Scope,
    Guid? TenantId,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string> AvailableSectionKeys,
    Guid? RollbackOfOperationId = null);

internal sealed record ConfigurationImportPreviewLine(
    string SectionKey,
    ConfigurationImportPreviewCategory Category,
    string ReasonCode,
    string? SourceMappingIdentity,
    string? TargetMappingIdentity);

internal sealed record ConfigurationImportPreviewView(
    bool IsApplyReady,
    bool CanApply,
    IReadOnlyList<ConfigurationImportPreviewLine> Items);

internal sealed record ConfigurationImportOperationView(
    Guid OperationId,
    ConfigurationImportOperationKind Kind,
    ConfigurationImportOperationStatus Status,
    IReadOnlyList<string> SelectedSectionKeys,
    IReadOnlyList<string> OmittedSectionKeys,
    bool SnapshotAvailable,
    ConfigurationImportEffectStatus EffectStatus,
    int EffectRetryCount,
    bool FidelityVerified,
    string FidelityDigest,
    DateTimeOffset CompletedAt,
    bool CanRollback);

internal interface IConfigurationPortabilityService
{
    Task<ConfigurationPortabilityCapabilities> GetCapabilitiesAsync(
        ConfigurationImportScope scope,
        CancellationToken cancellationToken);

    Task<bool> DownloadAsync(
        ConfigurationImportScope scope,
        Guid? tenantId,
        ConfigurationManifestExportView view,
        CancellationToken cancellationToken);

    Task<ConfigurationImportClientSession> CreateSessionAsync(
        ConfigurationImportScope scope,
        Guid? tenantId,
        IBrowserFile file,
        CancellationToken cancellationToken);

    Task<ConfigurationImportPreviewView> PreviewAsync(
        ConfigurationImportClientSession session,
        IReadOnlyCollection<string> selectedSections,
        IReadOnlyDictionary<string, string> mappings,
        IReadOnlyCollection<string> approvalCodes,
        ConfigurationImportApplyMode applyMode,
        CancellationToken cancellationToken);

    Task<ConfigurationImportOperationView> ApplyAsync(
        ConfigurationImportClientSession session,
        IReadOnlyCollection<string> selectedSections,
        IReadOnlyDictionary<string, string> mappings,
        IReadOnlyCollection<string> approvalCodes,
        ConfigurationImportApplyMode applyMode,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConfigurationImportOperationView>> HistoryAsync(
        ConfigurationImportScope scope,
        Guid? tenantId,
        CancellationToken cancellationToken);

    Task<ConfigurationImportOperationView> GetReceiptAsync(
        ConfigurationImportScope scope,
        Guid? tenantId,
        Guid operationId,
        CancellationToken cancellationToken);

    Task<ConfigurationImportClientSession> CreateRollbackSessionAsync(
        ConfigurationImportScope scope,
        Guid? tenantId,
        Guid operationId,
        CancellationToken cancellationToken);
}

internal sealed class ConfigurationPortabilityService(
    IControlPlaneClient controlPlaneClient,
    ITenantOnboardingClient tenantOnboardingClient,
    IControl_Plane_ConfigurationClient instanceConfigurationClient,
    ITenant_ConfigurationClient tenantConfigurationClient,
    IConfigurationManifestExportService manifestExports,
    IBrowserActionInterop browserActions,
    NavigationManager navigation) : IConfigurationPortabilityService
{
    private const long MaximumArtifactBytes = 4L * 1024 * 1024;

    public async Task<ConfigurationPortabilityCapabilities> GetCapabilitiesAsync(
        ConfigurationImportScope scope,
        CancellationToken cancellationToken)
    {
        if (scope == ConfigurationImportScope.Instance)
        {
            HalResourceOfControlPlaneOverviewDto resource =
                await controlPlaneClient.GetControlPlaneOverviewAsync(
                    cancellationToken: cancellationToken);
            return new ConfigurationPortabilityCapabilities(
                ControlPlaneHal.HasLink(
                    resource._links,
                    ControlPlaneLinkRelations.CreateConfigurationImportSession),
                ControlPlaneHal.HasLink(
                    resource._links,
                    ControlPlaneLinkRelations.ExportConfigurationOverrides),
                ControlPlaneHal.HasLink(
                    resource._links,
                    ControlPlaneLinkRelations.ExportConfigurationPortable),
                CanExportTenantPackage: false,
                ControlPlaneHal.HasLink(
                    resource._links,
                    ControlPlaneLinkRelations.ConfigurationImportHistory),
                CanCreateCloneTarget: false,
                TenantId: null);
        }

        HalResourceOfTenantOnboardingStatusDto status =
            await tenantOnboardingClient.GetTenantOnboardingStatusAsync(
                cancellationToken: cancellationToken);
        bool canCreateCloneTarget = false;
        try
        {
            HalCollectionResourceOfControlPlaneTenantListItemDto tenants =
                await controlPlaneClient.GetControlPlaneTenantsAsync(
                    cancellationToken: cancellationToken);
            canCreateCloneTarget = ControlPlaneHal.HasLink(
                tenants._links,
                ControlPlaneLinkRelations.Create);
        }
        catch (ApiException exception) when (exception.StatusCode is 401 or 403)
        {
        }
        return new ConfigurationPortabilityCapabilities(
            ControlPlaneHal.HasLink(
                status._links,
                ControlPlaneLinkRelations.CreateConfigurationImportSession),
            CanExportOverrides: false,
            CanExportPortable: false,
            ControlPlaneHal.HasLink(
                status._links,
                ControlPlaneLinkRelations.ExportTenantConfigurationPackage),
            ControlPlaneHal.HasLink(
                status._links,
                ControlPlaneLinkRelations.ConfigurationImportHistory),
            canCreateCloneTarget,
            status.TenantId);
    }

    public async Task<bool> DownloadAsync(
        ConfigurationImportScope scope,
        Guid? tenantId,
        ConfigurationManifestExportView view,
        CancellationToken cancellationToken)
    {
        if (scope == ConfigurationImportScope.Instance)
        {
            return (await manifestExports.DownloadAsync(view, cancellationToken)).Started;
        }

        ConfigurationPortabilityCapabilities capabilities =
            await GetCapabilitiesAsync(scope, cancellationToken);
        if (!capabilities.CanExportTenantPackage
            || tenantId is null
            || capabilities.TenantId != tenantId)
        {
            return false;
        }

        string route = ConfigurationManifestExportRoutes.BffTenantExport.Replace(
            "{tenantId:guid}",
            tenantId.Value.ToString("D"),
            StringComparison.Ordinal);
        var baseUri = new Uri(navigation.BaseUri, UriKind.Absolute);
        string pathBase = baseUri.AbsolutePath.TrimEnd('/');
        return await browserActions.DownloadFileFromUrlAsync(
            $"{pathBase}{route}?view={view}",
            cancellationToken);
    }

    public async Task<ConfigurationImportClientSession> CreateSessionAsync(
        ConfigurationImportScope scope,
        Guid? tenantId,
        IBrowserFile file,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        await using Stream stream = file.OpenReadStream(
            MaximumArtifactBytes,
            cancellationToken);
        HalResourceOfConfigurationImportSessionCreatedResult created =
            scope == ConfigurationImportScope.Instance
                ? await instanceConfigurationClient.CreateInstanceConfigurationImportSessionAsync(
                    stream,
                    cancellationToken: cancellationToken)
                : await tenantConfigurationClient.CreateTenantConfigurationImportSessionAsync(
                    RequireTenantId(tenantId),
                    stream,
                    cancellationToken: cancellationToken);
        return MapSession(created);
    }

    public async Task<ConfigurationImportPreviewView> PreviewAsync(
        ConfigurationImportClientSession session,
        IReadOnlyCollection<string> selectedSections,
        IReadOnlyDictionary<string, string> mappings,
        IReadOnlyCollection<string> approvalCodes,
        ConfigurationImportApplyMode applyMode,
        CancellationToken cancellationToken)
    {
        ConfigurationImportPreviewRequest request = Request(
            selectedSections,
            mappings,
            approvalCodes,
            applyMode);
        HalResourceOfConfigurationImportPreviewResult preview =
            session.Scope == ConfigurationImportScope.Instance
                ? await instanceConfigurationClient.PreviewInstanceConfigurationImportSessionAsync(
                    session.SessionId,
                    session.AccessToken,
                    request,
                    cancellationToken: cancellationToken)
                : await tenantConfigurationClient.PreviewTenantConfigurationImportSessionAsync(
                    RequireTenantId(session.TenantId),
                    session.SessionId,
                    session.AccessToken,
                    request,
                    cancellationToken: cancellationToken);
        return new ConfigurationImportPreviewView(
            preview.IsApplyReady,
            ControlPlaneHal.HasLink(
                preview._links,
                ControlPlaneLinkRelations.ApplyConfigurationImport),
            [.. preview.Items.Select(item => new ConfigurationImportPreviewLine(
                item.SectionKey,
                (ConfigurationImportPreviewCategory)item.Category,
                item.ReasonCode,
                item.SourceMappingIdentity,
                item.TargetMappingIdentity))]);
    }

    public async Task<ConfigurationImportOperationView> ApplyAsync(
        ConfigurationImportClientSession session,
        IReadOnlyCollection<string> selectedSections,
        IReadOnlyDictionary<string, string> mappings,
        IReadOnlyCollection<string> approvalCodes,
        ConfigurationImportApplyMode applyMode,
        CancellationToken cancellationToken)
    {
        var request = new ConfigurationImportApplyRequest
        {
            Preview = Request(
                selectedSections,
                mappings,
                approvalCodes,
                applyMode),
            RollbackOfOperationId = session.RollbackOfOperationId
        };
        HalResourceOfConfigurationImportOperationResult operation =
            session.Scope == ConfigurationImportScope.Instance
                ? await instanceConfigurationClient.ApplyInstanceConfigurationImportSessionAsync(
                    session.SessionId,
                    session.AccessToken,
                    request,
                    cancellationToken: cancellationToken)
                : await tenantConfigurationClient.ApplyTenantConfigurationImportSessionAsync(
                    RequireTenantId(session.TenantId),
                    session.SessionId,
                    session.AccessToken,
                    request,
                    cancellationToken: cancellationToken);
        return MapOperation(operation);
    }

    public async Task<IReadOnlyList<ConfigurationImportOperationView>> HistoryAsync(
        ConfigurationImportScope scope,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        HalResourceOfConfigurationImportHistoryResult history =
            scope == ConfigurationImportScope.Instance
                ? await instanceConfigurationClient.ListInstanceConfigurationImportHistoryAsync(
                    50,
                    cancellationToken: cancellationToken)
                : await tenantConfigurationClient.ListTenantConfigurationImportHistoryAsync(
                    RequireTenantId(tenantId),
                    50,
                    cancellationToken: cancellationToken);
        return [.. history.Operations.Select(operation =>
            new ConfigurationImportOperationView(
                operation.OperationId,
                (ConfigurationImportOperationKind)operation.Kind,
                (ConfigurationImportOperationStatus)operation.Status,
                [.. operation.SelectedSectionKeys],
                [.. operation.OmittedSectionKeys],
                operation.SnapshotAvailable,
                (ConfigurationImportEffectStatus)operation.EffectStatus,
                operation.EffectRetryCount,
                operation.FidelityVerified,
                operation.FidelityDigest,
                operation.CompletedAt,
                CanRollback: false))];
    }

    public async Task<ConfigurationImportOperationView> GetReceiptAsync(
        ConfigurationImportScope scope,
        Guid? tenantId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        HalResourceOfConfigurationImportOperationResult operation =
            scope == ConfigurationImportScope.Instance
                ? await instanceConfigurationClient.GetInstanceConfigurationImportReceiptAsync(
                    operationId,
                    cancellationToken: cancellationToken)
                : await tenantConfigurationClient.GetTenantConfigurationImportReceiptAsync(
                    RequireTenantId(tenantId),
                    operationId,
                    cancellationToken: cancellationToken);
        return MapOperation(operation);
    }

    public async Task<ConfigurationImportClientSession> CreateRollbackSessionAsync(
        ConfigurationImportScope scope,
        Guid? tenantId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        HalResourceOfConfigurationImportRollbackSessionCreatedResult created =
            scope == ConfigurationImportScope.Instance
                ? await instanceConfigurationClient.CreateInstanceConfigurationRollbackSessionAsync(
                    operationId,
                    cancellationToken: cancellationToken)
                : await tenantConfigurationClient.CreateTenantConfigurationRollbackSessionAsync(
                    RequireTenantId(tenantId),
                    operationId,
                    cancellationToken: cancellationToken);
        return MapSession(created.Session) with
        {
            RollbackOfOperationId = created.SourceOperationId
        };
    }

    private static ConfigurationImportPreviewRequest Request(
        IReadOnlyCollection<string> selectedSections,
        IReadOnlyDictionary<string, string> mappings,
        IReadOnlyCollection<string> approvalCodes,
        ConfigurationImportApplyMode applyMode) =>
        new()
        {
            SelectedSectionKeys = [.. selectedSections],
            Mappings = new Dictionary<string, string>(mappings, StringComparer.Ordinal),
            GrantedApprovalCodes = [.. approvalCodes],
            ApplyMode = applyMode
        };

    private static ConfigurationImportClientSession MapSession(
        HalResourceOfConfigurationImportSessionCreatedResult created) =>
        new(
            created.SessionId,
            created.AccessToken,
            created.TargetScope,
            created.TargetTenantId,
            created.ExpiresAt,
            [.. created.AvailableSectionKeys]);

    private static ConfigurationImportClientSession MapSession(
        ConfigurationImportSessionCreatedResult created) =>
        new(
            created.SessionId,
            created.AccessToken,
            created.TargetScope,
            created.TargetTenantId,
            created.ExpiresAt,
            [.. created.AvailableSectionKeys]);

    private static ConfigurationImportOperationView MapOperation(
        HalResourceOfConfigurationImportOperationResult operation) =>
        new(
            operation.OperationId,
            operation.Kind,
            operation.Status,
            [.. operation.SelectedSectionKeys],
            [.. operation.OmittedSectionKeys],
            operation.SnapshotAvailable,
            operation.EffectStatus,
            operation.EffectRetryCount,
            operation.FidelityVerified,
            operation.FidelityDigest,
            operation.CompletedAt,
            ControlPlaneHal.HasLink(
                operation._links,
                ControlPlaneLinkRelations.CreateConfigurationImportRollback));

    private static Guid RequireTenantId(Guid? tenantId) =>
        tenantId is { } value && value != Guid.Empty
            ? value
            : throw new InvalidOperationException(
                "A route-authoritative tenant is required for tenant portability.");
}
