using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.InstanceAdmin;

public interface IInstanceDomainService
{
    Task<HalResourceOfInstanceDomainOverviewDto> GetDomainsAsync(
        CancellationToken cancellationToken = default);
}
