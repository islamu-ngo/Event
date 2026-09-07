namespace Explore.Application.Contracts.Webhooks;

public interface IWebhookDeliveryGovernanceResolver
{
    Task<WebhookDeliveryGovernancePolicy> ResolveAsync(
        Guid? tenantId,
        CancellationToken cancellationToken = default);
}

public sealed record WebhookDeliveryGovernancePolicy(
    int GlobalInFlightLimit,
    int MaxInFlightPerTenant,
    int MaxInFlightPerEndpoint,
    int MaxItemsPerTenantPerClaimCycle,
    int MaxAttempts,
    int EndpointTimeoutSeconds,
    int AutoPauseThreshold,
    string ResolutionVersion);
