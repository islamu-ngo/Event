using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.ControlPlane;

public interface IControlPlaneTenantService
{
    Task<HalCollectionResourceOfControlPlaneTenantListItemDto> GetTenantsAsync(
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> CreateTenantAsync(
        CreateTenantDto request,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfControlPlaneTenantLifecycleTransitionDto> ActivateTenantAsync(
        Guid tenantId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfControlPlaneTenantLifecycleTransitionDto> SuspendTenantAsync(
        Guid tenantId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfControlPlaneTenantLifecycleTransitionDto> ArchiveTenantAsync(
        Guid tenantId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfControlPlaneTenantLifecycleTransitionDto> ReactivateTenantAsync(
        Guid tenantId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfControlPlaneTenantLifecycleTransitionDto> ScheduleTenantPurgeAsync(
        Guid tenantId,
        string reason,
        string confirmationText,
        CancellationToken cancellationToken = default);
}
