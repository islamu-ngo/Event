using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public sealed record WebhookDeliveryClaimLimits(
    int MaxInFlightPerTenant,
    int MaxInFlightPerEndpoint,
    int MaxItemsPerClaimCycle);

public sealed record WebhookLocalTargetClaimRequest(
    int BatchSize,
    int CandidateBatchSize,
    int GlobalInFlightLimit,
    IReadOnlyList<Guid> TenantOrder,
    DateTimeOffset ClaimedAtUtc,
    TimeSpan LeaseDuration,
    Guid? TargetId = null);

public sealed record WebhookLocalTargetClaim(
    WebhookLocalTargetSnapshot Target,
    WebhookMessage Message,
    Guid LeaseToken,
    long DeliveryFence,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset LeaseExpiresAtUtc);

public interface IWebhookLocalTargetRepository
{
    Task<IReadOnlyList<Guid>> GetDueTenantIdsAsync(
        int tenantLimit,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookLocalTargetClaim>> ClaimDueAsync(
        WebhookLocalTargetClaimRequest request,
        IReadOnlyDictionary<Guid, WebhookDeliveryClaimLimits> tenantLimits,
        CancellationToken cancellationToken);

    Task<int> CountDueAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task<int> CountStaleDeliveringAsync(
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken);

    Task<int> RecoverExpiredClaimsAsync(
        DateTimeOffset recoveredAtUtc,
        string failureCategory,
        int batchSize,
        CancellationToken cancellationToken);

    Task<WebhookLocalTargetSnapshot?> GetActiveClaimAsync(
        Guid tenantId,
        Guid targetId,
        Guid leaseToken,
        long deliveryFence,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken);

    Task<WebhookLocalTargetSnapshot?> GetByMessageAndEndpointForUpdateAsync(
        Guid tenantId,
        Guid messageId,
        Guid endpointId,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
