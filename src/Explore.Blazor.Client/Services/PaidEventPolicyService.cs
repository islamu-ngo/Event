using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.PaidEventPolicies;

namespace Explore.Blazor.Client.Services;

public sealed class PaidEventPolicyService(
    IInstancePaidEventPolicySettingsClient instanceClient,
    ITenantPaidEventPolicySettingsClient tenantClient) : IPaidEventPolicyService
{
    public Task<HalResourceOfPaidEventPolicyDto> GetInstanceAsync(CancellationToken cancellationToken = default) =>
        instanceClient.GetInstancePaidEventPolicySettingsAsync(cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> UpdateInstanceAsync(
        RevisePaidEventPolicyDto request,
        CancellationToken cancellationToken = default) =>
        instanceClient.UpdateInstancePaidEventPolicySettingsAsync(request, cancellationToken: cancellationToken);

    public Task<HalResourceOfTenantPaidEventPolicyConfigurationDto> GetTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        tenantClient.GetTenantPaidEventPolicySettingsAsync(tenantId, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> UpdateTenantAsync(
        Guid tenantId,
        RevisePaidEventPolicyDto request,
        CancellationToken cancellationToken = default) =>
        tenantClient.UpdateTenantPaidEventPolicySettingsAsync(tenantId, request, cancellationToken: cancellationToken);
}
