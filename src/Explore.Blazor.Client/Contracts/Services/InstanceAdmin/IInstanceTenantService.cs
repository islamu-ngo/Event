using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.InstanceAdmin;

public interface IInstanceTenantService
{
    Task<HalCollectionResourceOfInstanceTenantListItemDto> GetTenantsAsync(
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> CreateTenantAsync(
        CreateTenantDto request,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfInstanceTenantLifecycleTransitionDto> ActivateTenantAsync(
        Guid tenantId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfInstanceTenantLifecycleTransitionDto> SuspendTenantAsync(
        Guid tenantId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfInstanceTenantLifecycleTransitionDto> ArchiveTenantAsync(
        Guid tenantId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfInstanceTenantLifecycleTransitionDto> ReactivateTenantAsync(
        Guid tenantId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfInstanceTenantLifecycleTransitionDto> ScheduleTenantPurgeAsync(
        Guid tenantId,
        string reason,
        string confirmationText,
        CancellationToken cancellationToken = default);
}
