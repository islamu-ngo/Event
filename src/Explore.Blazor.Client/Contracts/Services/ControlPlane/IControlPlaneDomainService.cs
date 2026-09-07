using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.ControlPlane;

public interface IControlPlaneDomainService
{
    Task<HalResourceOfControlPlaneDomainOverviewDto> GetDomainsAsync(
        CancellationToken cancellationToken = default);
}
