using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.ControlPlane;

public interface IControlPlaneTenantConfigurationService
{
    Task<HalResourceOfControlPlaneTenantEffectiveConfigurationDto> GetEffectiveConfigurationAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> SetSettingAsync(
        Guid tenantId,
        string key,
        string value,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> LockSettingAsync(
        Guid tenantId,
        string key,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> UnlockSettingAsync(
        Guid tenantId,
        string key,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> SwitchPlanAsync(
        Guid tenantId,
        Guid tenantPlanVersionId,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> ApplyPlanAsync(
        Guid tenantId,
        Guid assignmentId,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> RollbackPlanAsync(
        Guid tenantId,
        Guid assignmentId,
        CancellationToken cancellationToken = default);
}
