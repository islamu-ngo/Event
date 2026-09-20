using Explore.Application.Contracts.Deployment;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Deployment;

namespace Explore.Application.Features.Deployment;

public sealed record GetTicketingDeploymentCapabilitiesQuery :
    IQuery<TicketingDeploymentCapabilityMatrixDto>;

public sealed class GetTicketingDeploymentCapabilitiesQueryHandler(
    ITicketingDeploymentCapabilityCatalog catalog) :
    IQueryHandler<
        GetTicketingDeploymentCapabilitiesQuery,
        TicketingDeploymentCapabilityMatrixDto>
{
    public Task<TicketingDeploymentCapabilityMatrixDto> QueryAsync(
        GetTicketingDeploymentCapabilitiesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        TicketingDeploymentCapabilitySnapshot snapshot =
            catalog.GetSnapshot();
        return Task.FromResult(
            new TicketingDeploymentCapabilityMatrixDto(
                snapshot.SchemaVersion,
                snapshot.Revision,
                snapshot.ReferenceTopology,
                snapshot.Capabilities
                    .Select(capability =>
                        new TicketingDeploymentCapabilityDto(
                            capability.Code,
                            capability.Status,
                            capability.ReasonCode,
                            capability.RequiredExternalGates.ToArray()))
                    .ToArray()));
    }
}
