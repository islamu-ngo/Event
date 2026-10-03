using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.InstanceAdmin;

namespace Explore.Blazor.Client.Services.InstanceAdmin;

public sealed class InstanceAdminApiAdapter(
    IInstanceAdminClient controlPlaneClient,
    IInstanceDeploymentModeClient deploymentModeClient,
    IInstanceTenantConfigurationClient tenantConfigurationClient,
    IInstanceTenantLifecycleClient tenantLifecycleClient,
    IInstanceTenantPlanClient tenantPlanClient) :
    IInstanceOverviewService,
    IInstanceTenantService,
    IInstanceDomainService,
    IInstanceOperationsService,
    IInstancePlanCatalogService,
    IInstanceTenantConfigurationService
{
    public Task<HalResourceOfInstanceOverviewDto> GetOverviewAsync(
        CancellationToken cancellationToken = default) =>
        controlPlaneClient.GetInstanceAdminOverviewAsync(cancellationToken: cancellationToken);

    public Task<HalCollectionResourceOfInstanceTenantListItemDto> GetTenantsAsync(
        CancellationToken cancellationToken = default) =>
        controlPlaneClient.GetInstanceAdminTenantsAsync(cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> CreateTenantAsync(
        CreateTenantDto request,
        CancellationToken cancellationToken = default) =>
        tenantLifecycleClient.CreateInstanceAdminTenantAsync(request, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfInstanceTenantLifecycleTransitionDto> ActivateTenantAsync(
        Guid tenantId,
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        tenantLifecycleClient.ActivateInstanceAdminTenantAsync(
            tenantId,
            body: new InstanceTenantLifecycleTransitionRequestDto { Reason = reason },
            cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfInstanceTenantLifecycleTransitionDto> SuspendTenantAsync(
        Guid tenantId,
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        tenantLifecycleClient.SuspendInstanceAdminTenantAsync(
            tenantId,
            body: new InstanceTenantLifecycleTransitionRequestDto { Reason = reason },
            cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfInstanceTenantLifecycleTransitionDto> ArchiveTenantAsync(
        Guid tenantId,
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        tenantLifecycleClient.ArchiveInstanceAdminTenantAsync(
            tenantId,
            body: new InstanceTenantLifecycleTransitionRequestDto { Reason = reason },
            cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfInstanceTenantLifecycleTransitionDto> ReactivateTenantAsync(
        Guid tenantId,
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        tenantLifecycleClient.ReactivateInstanceAdminTenantAsync(
            tenantId,
            body: new InstanceTenantLifecycleTransitionRequestDto { Reason = reason },
            cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfInstanceTenantLifecycleTransitionDto> ScheduleTenantPurgeAsync(
        Guid tenantId,
        string reason,
        string confirmationText,
        CancellationToken cancellationToken = default) =>
        tenantLifecycleClient.ScheduleInstanceAdminTenantPurgeAsync(
            tenantId,
            body: new InstanceTenantLifecycleTransitionRequestDto
            {
                Reason = reason,
                ConfirmationText = confirmationText
            },
            cancellationToken: cancellationToken);

    public Task<HalResourceOfInstanceDomainOverviewDto> GetDomainsAsync(
        CancellationToken cancellationToken = default) =>
        controlPlaneClient.GetInstanceAdminDomainsAsync(cancellationToken: cancellationToken);

    public Task<HalResourceOfInstanceOperationsDto> GetOperationsAsync(
        CancellationToken cancellationToken = default) =>
        controlPlaneClient.GetInstanceAdminOperationsAsync(cancellationToken: cancellationToken);

    public Task<HalResourceOfInstanceDeploymentModeRunbookDto> GetDeploymentModeRunbookAsync(
        CancellationToken cancellationToken = default) =>
        deploymentModeClient.GetInstanceAdminDeploymentModeRunbookAsync(cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfInstanceDeploymentModeTransitionDto> TransitionDeploymentModeAsync(
        string targetMode,
        string confirmationText,
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        deploymentModeClient.TransitionInstanceAdminDeploymentModeAsync(
            body: new InstanceDeploymentModeTransitionRequestDto
            {
                TargetMode = targetMode,
                ConfirmationText = confirmationText,
                Reason = reason
            },
            cancellationToken: cancellationToken);

    public Task<HalCollectionResourceOfInstanceTenantPlanListItemDto> GetPlansAsync(
        CancellationToken cancellationToken = default) =>
        tenantPlanClient.GetInstanceAdminTenantPlansAsync(cancellationToken: cancellationToken);

    public Task<HalResourceOfInstanceTenantPlanDetailDto> GetPlanAsync(
        string key,
        CancellationToken cancellationToken = default) =>
        tenantPlanClient.GetInstanceAdminTenantPlanByKeyAsync(key, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> CreatePlanDraftAsync(
        TenantPlanDraft draft,
        CancellationToken cancellationToken = default) =>
        tenantPlanClient.CreateInstanceAdminTenantPlanDraftAsync(draft, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> CreateVersionDraftAsync(
        string key,
        TenantPlanDraft draft,
        CancellationToken cancellationToken = default) =>
        tenantPlanClient.CreateInstanceAdminTenantPlanVersionDraftAsync(key, draft, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> UpdateVersionDraftAsync(
        Guid versionId,
        TenantPlanDraft draft,
        CancellationToken cancellationToken = default) =>
        tenantPlanClient.UpdateInstanceAdminTenantPlanVersionDraftAsync(
            versionId,
            new PatchInstanceTenantPlanVersionDraftDto
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
        tenantPlanClient.PublishInstanceAdminTenantPlanVersionAsync(
            versionId,
            new PublishTenantPlanVersionRequest { ExistingTenantPolicy = existingTenantPolicy },
            cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> ArchiveVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default) =>
        tenantPlanClient.ArchiveInstanceAdminTenantPlanVersionAsync(versionId, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> ClonePlanAsync(
        Guid sourceVersionId,
        string key,
        string name,
        CancellationToken cancellationToken = default) =>
        tenantPlanClient.CloneInstanceAdminTenantPlanAsync(
            sourceVersionId,
            new CloneTenantPlanRequest { Key = key, Name = name },
            cancellationToken: cancellationToken);

    public Task<TenantPlanValidationResult> ValidateDraftAsync(
        TenantPlanDraft draft,
        CancellationToken cancellationToken = default) =>
        tenantPlanClient.ValidateInstanceAdminTenantPlanDraftAsync(draft, cancellationToken: cancellationToken);

    public Task<TenantPlanDiffResult> PreviewDiffAsync(
        TenantPlanEffectiveConfiguration current,
        TenantPlanDraft draft,
        CancellationToken cancellationToken = default) =>
        tenantPlanClient.PreviewInstanceAdminTenantPlanDiffAsync(
            new PreviewTenantPlanDiffRequest { Current = current, Draft = draft },
            cancellationToken: cancellationToken);

    public Task<HalResourceOfInstanceTenantEffectiveConfigurationDto> GetEffectiveConfigurationAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        tenantConfigurationClient.GetInstanceAdminTenantEffectiveConfigurationAsync(tenantId, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> SetSettingAsync(
        Guid tenantId,
        string key,
        string value,
        CancellationToken cancellationToken = default) =>
        tenantConfigurationClient.SetInstanceAdminTenantSettingAsync(
            tenantId,
            key,
            new SetInstanceTenantSettingRequest { Value = value },
            cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> LockSettingAsync(
        Guid tenantId,
        string key,
        CancellationToken cancellationToken = default) =>
        tenantConfigurationClient.LockInstanceAdminTenantSettingAsync(tenantId, key, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> UnlockSettingAsync(
        Guid tenantId,
        string key,
        CancellationToken cancellationToken = default) =>
        tenantConfigurationClient.UnlockInstanceAdminTenantSettingAsync(tenantId, key, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> SwitchPlanAsync(
        Guid tenantId,
        Guid tenantPlanVersionId,
        CancellationToken cancellationToken = default) =>
        tenantConfigurationClient.SwitchInstanceAdminTenantPlanAssignmentAsync(
            tenantId,
            new SwitchTenantPlanAssignmentRequest { TenantPlanVersionId = tenantPlanVersionId },
            cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> ApplyPlanAsync(
        Guid tenantId,
        Guid assignmentId,
        CancellationToken cancellationToken = default) =>
        tenantConfigurationClient.ApplyInstanceAdminTenantPlanAssignmentAsync(
            tenantId,
            assignmentId,
            cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> RollbackPlanAsync(
        Guid tenantId,
        Guid assignmentId,
        CancellationToken cancellationToken = default) =>
        tenantConfigurationClient.RollbackInstanceAdminTenantPlanAssignmentAsync(
            tenantId,
            assignmentId,
            cancellationToken: cancellationToken);
}
