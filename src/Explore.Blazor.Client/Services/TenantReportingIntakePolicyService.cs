using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Reporting;

namespace Explore.Blazor.Client.Services;

public sealed class TenantReportingIntakePolicyService(ITenantReportingIntakeSettingsClient apiClient)
    : ITenantReportingIntakePolicyService
{
    public Task<HalResourceOfTenantReportingIntakePolicyDto> GetAsync(
        CancellationToken cancellationToken = default) =>
        apiClient.GetTenantReportingIntakePolicyAsync(
            api_version: "1.0",
            cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> UpdateAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        apiClient.UpdateTenantReportingIntakePolicyAsync(
            new UpdateTenantReportingIntakePolicyDto { Enabled = enabled },
            api_version: "1.0",
            cancellationToken: cancellationToken);
}
