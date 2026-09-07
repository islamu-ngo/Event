using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services;

public interface ITicketingDeploymentCapabilityService
{
    Task<TicketingDeploymentCapabilityMatrixDto> GetAsync(
        CancellationToken cancellationToken);
}
