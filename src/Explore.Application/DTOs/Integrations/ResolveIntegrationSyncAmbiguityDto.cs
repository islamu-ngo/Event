using Explore.Application.Contracts.Persistence;

namespace Explore.Application.DTOs.Integrations;

public sealed record ResolveIntegrationSyncAmbiguityDto
{
    public IntegrationSyncRecoveryDecision Decision { get; init; }
    public string EvidenceReference { get; init; } = string.Empty;
}
