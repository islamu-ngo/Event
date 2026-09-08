using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.PaidEventPolicies;

public interface IPaidEventPolicyService
{
    Task<HalResourceOfPaidEventPolicyDto> GetInstanceAsync(CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> UpdateInstanceAsync(
        RevisePaidEventPolicyDto request,
        CancellationToken cancellationToken = default);

    Task<HalResourceOfTenantPaidEventPolicyConfigurationDto> GetTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> UpdateTenantAsync(
        Guid tenantId,
        RevisePaidEventPolicyDto request,
        CancellationToken cancellationToken = default);
}
