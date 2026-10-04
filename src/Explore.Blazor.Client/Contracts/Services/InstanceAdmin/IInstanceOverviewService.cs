using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.InstanceAdmin;

public interface IInstanceOverviewService
{
    Task<HalResourceOfInstanceOverviewDto> GetOverviewAsync(
        CancellationToken cancellationToken = default);
}
