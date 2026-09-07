using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.ControlPlane;

public interface IControlPlaneOverviewService
{
    Task<HalResourceOfControlPlaneOverviewDto> GetOverviewAsync(
        CancellationToken cancellationToken = default);
}
