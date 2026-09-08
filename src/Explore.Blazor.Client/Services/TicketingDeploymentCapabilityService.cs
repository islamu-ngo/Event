using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;

namespace Explore.Blazor.Client.Services;

public sealed class TicketingDeploymentCapabilityService(
    ITicketingDeploymentCapabilitiesClient apiClient) :
    ITicketingDeploymentCapabilityService
{
    public Task<TicketingDeploymentCapabilityMatrixDto> GetAsync(
        CancellationToken cancellationToken) =>
        apiClient.GetTicketingDeploymentCapabilitiesAsync(
            cancellationToken: cancellationToken);
}
