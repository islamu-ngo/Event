using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.ControlPlane;

namespace Explore.Blazor.Client.Services.ControlPlane;

public sealed class ControlPlaneApiAdapter(
    IControlPlaneClient controlPlaneClient,
    IControlPlaneDeploymentModeClient deploymentModeClient,
    IControlPlaneTenantConfigurationClient tenantConfigurationClient,
    IControlPlaneTenantLifecycleClient tenantLifecycleClient,
    IControlPlaneTenantPlanClient tenantPlanClient) :
    IControlPlaneOverviewService,
    IControlPlaneTenantService,
    IControlPlaneDomainService,
    IControlPlaneOperationsService,
    IControlPlanePlanCatalogService,
    IControlPlaneTenantConfigurationService
{
    public Task<HalResourceOfControlPlaneOverviewDto> GetOverviewAsync(
        CancellationToken cancellationToken = default) =>
        controlPlaneClient.GetControlPlaneOverviewAsync(cancellationToken: cancellationToken);

    public Task<HalCollectionResourceOfControlPlaneTenantListItemDto> GetTenantsAsync(
        CancellationToken cancellationToken = default) =>
        controlPlaneClient.GetControlPlaneTenantsAsync(cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> CreateTenantAsync(
        CreateTenantDto request,
        CancellationToken cancellationToken = default) =>
        tenantLifecycleClient.CreateControlPlaneTenantAsync(request, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfControlPlaneTenantLifecycleTransitionDto> ActivateTenantAsync(
        Guid tenantId,
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        tenantLifecycleClient.ActivateControlPlaneTenantAsync(
            tenantId,
            body: new ControlPlaneTenantLifecycleTransitionRequestDto { Reason = reason },
            cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfControlPlaneTenantLifecycleTransitionDto> SuspendTenantAsync(
        Guid tenantId,
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        tenantLifecycleClient.SuspendControlPlaneTenantAsync(
            tenantId,
            body: new ControlPlaneTenantLifecycleTransitionRequestDto { Reason = reason },
            cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfControlPlaneTenantLifecycleTransitionDto> ArchiveTenantAsync(
        Guid tenantId,
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        tenantLifecycleClient.ArchiveControlPlaneTenantAsync(
            tenantId,
            body: new ControlPlaneTenantLifecycleTransitionRequestDto { Reason = reason },
            cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfControlPlaneTenantLifecycleTransitionDto> ReactivateTenantAsync(
        Guid tenantId,
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        tenantLifecycleClient.ReactivateControlPlaneTenantAsync(
            tenantId,
            body: new ControlPlaneTenantLifecycleTransitionRequestDto { Reason = reason },
            cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfControlPlaneTenantLifecycleTransitionDto> ScheduleTenantPurgeAsync(
        Guid tenantId,
        string reason,
        string confirmationText,
        CancellationToken cancellationToken = default) =>
        tenantLifecycleClient.ScheduleControlPlaneTenantPurgeAsync(
            tenantId,
            body: new ControlPlaneTenantLifecycleTransitionRequestDto
            {
                Reason = reason,
                ConfirmationText = confirmationText
            },
            cancellationToken: cancellationToken);

    public Task<HalResourceOfControlPlaneDomainOverviewDto> GetDomainsAsync(
        CancellationToken cancellationToken = default) =>
        controlPlaneClient.GetControlPlaneDomainsAsync(cancellationToken: cancellationToken);

    public Task<HalResourceOfControlPlaneOperationsDto> GetOperationsAsync(
        CancellationToken cancellationToken = default) =>
        controlPlaneClient.GetControlPlaneOperationsAsync(cancellationToken: cancellationToken);

    public Task<HalResourceOfControlPlaneDeploymentModeRunbookDto> GetDeploymentModeRunbookAsync(
        CancellationToken cancellationToken = default) =>
        deploymentModeClient.GetControlPlaneDeploymentModeRunbookAsync(cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfControlPlaneDeploymentModeTransitionDto> TransitionDeploymentModeAsync(
        string targetMode,
        string confirmationText,
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        deploymentModeClient.TransitionControlPlaneDeploymentModeAsync(
            body: new ControlPlaneDeploymentModeTransitionRequestDto
            {
                TargetMode = targetMode,
                ConfirmationText = confirmationText,
                Reason = reason
            },
            cancellationToken: cancellationToken);

    public Task<HalCollectionResourceOfControlPlaneTenantPlanListItemDto> GetPlansAsync(
        CancellationToken cancellationToken = default) =>
        tenantPlanClient.GetControlPlaneTenantPlansAsync(cancellationToken: cancellationToken);

    public Task<HalResourceOfControlPlaneTenantPlanDetailDto> GetPlanAsync(
        string key,
        CancellationToken cancellationToken = default) =>
        tenantPlanClient.GetControlPlaneTenantPlanByKeyAsync(key, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> CreatePlanDraftAsync(
        TenantPlanDraft draft,
        CancellationToken cancellationToken = default) =>
        tenantPlanClient.CreateControlPlaneTenantPlanDraftAsync(draft, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> CreateVersionDraftAsync(
        string key,
        TenantPlanDraft draft,
        CancellationToken cancellationToken = default) =>
        tenantPlanClient.CreateControlPlaneTenantPlanVersionDraftAsync(key, draft, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> UpdateVersionDraftAsync(
        Guid versionId,
        TenantPlanDraft draft,
        CancellationToken cancellationToken = default) =>
        tenantPlanClient.UpdateControlPlaneTenantPlanVersionDraftAsync(
            versionId,
            new PatchControlPlaneTenantPlanVersionDraftDto
            {
                Pricing = new PatchTenantPlanPricingDto
                {
                    Amount = draft.Pricing.Amount,
                    CurrencyCode = draft.Pricing.CurrencyCode,
                    BillingPeriod = draft.Pricing.BillingPeriod
                },
                IsActiveForProvisioning = new PatchTenantPlanProvisioningDto
                {
                    Value = draft.IsActiveForProvisioning
                },
                SettingOverrides = new PatchTenantPlanSettingOverridesDto
                {
                    Values = draft.SettingOverrides
                },
                QuotaLimits = new PatchTenantPlanQuotaLimitsDto
                {
                    Values = draft.QuotaLimits
                }
            },
            cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> PublishVersionAsync(
        Guid versionId,
        int existingTenantPolicy,
        CancellationToken cancellationToken = default) =>
        tenantPlanClient.PublishControlPlaneTenantPlanVersionAsync(
            versionId,
            new PublishTenantPlanVersionRequest { ExistingTenantPolicy = existingTenantPolicy },
            cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> ArchiveVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default) =>
        tenantPlanClient.ArchiveControlPlaneTenantPlanVersionAsync(versionId, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> ClonePlanAsync(
        Guid sourceVersionId,
        string key,
        string name,
        CancellationToken cancellationToken = default) =>
        tenantPlanClient.CloneControlPlaneTenantPlanAsync(
            sourceVersionId,
            new CloneTenantPlanRequest { Key = key, Name = name },
            cancellationToken: cancellationToken);

    public Task<TenantPlanValidationResult> ValidateDraftAsync(
        TenantPlanDraft draft,
        CancellationToken cancellationToken = default) =>
        tenantPlanClient.ValidateControlPlaneTenantPlanDraftAsync(draft, cancellationToken: cancellationToken);

    public Task<TenantPlanDiffResult> PreviewDiffAsync(
        TenantPlanEffectiveConfiguration current,
        TenantPlanDraft draft,
        CancellationToken cancellationToken = default) =>
        tenantPlanClient.PreviewControlPlaneTenantPlanDiffAsync(
            new PreviewTenantPlanDiffRequest { Current = current, Draft = draft },
            cancellationToken: cancellationToken);

    public Task<HalResourceOfControlPlaneTenantEffectiveConfigurationDto> GetEffectiveConfigurationAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        tenantConfigurationClient.GetControlPlaneTenantEffectiveConfigurationAsync(tenantId, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> SetSettingAsync(
        Guid tenantId,
        string key,
        string value,
        CancellationToken cancellationToken = default) =>
        tenantConfigurationClient.SetControlPlaneTenantSettingAsync(
            tenantId,
            key,
            new SetControlPlaneTenantSettingRequest { Value = value },
            cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> LockSettingAsync(
        Guid tenantId,
        string key,
        CancellationToken cancellationToken = default) =>
        tenantConfigurationClient.LockControlPlaneTenantSettingAsync(tenantId, key, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> UnlockSettingAsync(
        Guid tenantId,
        string key,
        CancellationToken cancellationToken = default) =>
        tenantConfigurationClient.UnlockControlPlaneTenantSettingAsync(tenantId, key, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> SwitchPlanAsync(
        Guid tenantId,
        Guid tenantPlanVersionId,
        CancellationToken cancellationToken = default) =>
        tenantConfigurationClient.SwitchControlPlaneTenantPlanAssignmentAsync(
            tenantId,
            new SwitchTenantPlanAssignmentRequest { TenantPlanVersionId = tenantPlanVersionId },
            cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> ApplyPlanAsync(
        Guid tenantId,
        Guid assignmentId,
        CancellationToken cancellationToken = default) =>
        tenantConfigurationClient.ApplyControlPlaneTenantPlanAssignmentAsync(
            tenantId,
            assignmentId,
            cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> RollbackPlanAsync(
        Guid tenantId,
        Guid assignmentId,
        CancellationToken cancellationToken = default) =>
        tenantConfigurationClient.RollbackControlPlaneTenantPlanAssignmentAsync(
            tenantId,
            assignmentId,
            cancellationToken: cancellationToken);
}
