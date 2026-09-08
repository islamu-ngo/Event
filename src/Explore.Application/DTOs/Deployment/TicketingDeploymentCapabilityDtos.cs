namespace Explore.Application.DTOs.Deployment;

public sealed record TicketingDeploymentCapabilityDto(
    string Code,
    string Status,
    string ReasonCode,
    IReadOnlyList<string> RequiredExternalGates);

public sealed record TicketingDeploymentCapabilityMatrixDto(
    int SchemaVersion,
    string Revision,
    string ReferenceTopology,
    IReadOnlyList<TicketingDeploymentCapabilityDto> Capabilities);
