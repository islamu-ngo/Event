using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Reporting;

public interface ITenantReportingIntakePolicyService
{
    Task<HalResourceOfTenantReportingIntakePolicyDto> GetAsync(
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> UpdateAsync(
        bool enabled,
        CancellationToken cancellationToken = default);
}
