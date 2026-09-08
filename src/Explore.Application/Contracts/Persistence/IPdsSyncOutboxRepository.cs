using Explore.Domain.Federation;

namespace Explore.Application.Contracts.Persistence;

public sealed record PdsSyncClaim(
    Guid OutboxId,
    Guid TenantId,
    Guid UserId,
    Guid LeaseToken,
    long LeaseFence);

public sealed record PdsSyncCompensationEvidence(
    IReadOnlyList<string> AllowedPayloads,
    IReadOnlyList<string> AllowedBaseCids,
    bool IsComplete);

public interface IPdsSyncOutboxRepository
{
    const int MaximumEventDeliveryStateBatchSize = 100;

    Task AddAsync(PdsSyncOutbox outbox, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Guid tenantId,
        Guid outboxId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PdsSyncOutbox>> GetCurrentEventDeliveryStatesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> eventIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PdsSyncClaim>> ClaimDueAsync(
        int batchSize,
        string leaseOwner,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task<PdsSyncOutbox?> GetActiveClaimAsync(
        PdsSyncClaim claim,
        DateTime observedAt,
        CancellationToken cancellationToken = default);

    Task<PdsSyncOutbox?> GetLatestUnsettledMutationAsync(
        Guid tenantId,
        string sourceEntityType,
        Guid sourceEntityId,
        string collection,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PdsSyncOutbox>> GetUnsettledEventMutationsForActorAsync(
        Guid actorId,
        string sourceEntityType,
        string collection,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PdsSyncOutbox>> GetUnsettledEventMutationsForActorAndDidAsync(
        Guid actorId,
        string did,
        string sourceEntityType,
        string collection,
        CancellationToken cancellationToken = default);

    Task<PdsSyncCompensationEvidence> GetCompensationEvidenceAsync(
        PdsSyncOutbox successor,
        CancellationToken cancellationToken = default);

    Task<bool> TryRenewClaimAsync(
        PdsSyncClaim claim,
        DateTime observedAt,
        DateTime leaseExpiresAt,
        CancellationToken cancellationToken = default);

    Task<bool> TrySettleAsync(
        PdsSyncClaim claim,
        string? uri,
        string? cid,
        DateTime settledAt,
        CancellationToken cancellationToken = default,
        string? observedBaseCid = null);

    Task<bool> TryFailAsync(
        PdsSyncClaim claim,
        string failureCode,
        bool retryable,
        DateTime failedAt,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default);

    Task<int> SupersedePriorAsync(
        Guid tenantId,
        string sourceEntityType,
        Guid sourceEntityId,
        Guid supersedingOutboxId,
        DateTime supersededAt,
        CancellationToken cancellationToken = default);

}
