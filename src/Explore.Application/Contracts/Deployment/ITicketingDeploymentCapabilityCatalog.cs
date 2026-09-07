namespace Explore.Application.Contracts.Deployment;

public static class TicketingDeploymentStatuses
{
    public const string ProductionApproved = "production-approved";
    public const string TestOnly = "test-only";
    public const string Disabled = "disabled";
}

public sealed record TicketingDeploymentCapability(
    string Code,
    string Status,
    string ReasonCode,
    IReadOnlyList<string> RequiredExternalGates);

public sealed record TicketingDeploymentCapabilitySnapshot(
    int SchemaVersion,
    string Revision,
    string ReferenceTopology,
    IReadOnlyList<TicketingDeploymentCapability> Capabilities);

public interface ITicketingDeploymentCapabilityCatalog
{
    TicketingDeploymentCapabilitySnapshot GetSnapshot();
}
