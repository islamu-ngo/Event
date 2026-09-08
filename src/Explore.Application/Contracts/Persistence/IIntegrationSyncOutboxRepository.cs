using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IIntegrationSyncOutboxRepository
{
    Task<IntegrationSyncOutbox> Create(IntegrationSyncOutbox outbox, CancellationToken cancellationToken);

    Task<IReadOnlyList<IntegrationSyncOutbox>> GetPendingBatch(
        int batchSize,
        DateTime now,
        DateTime staleProcessingStartedBefore,
        CancellationToken cancellationToken);

    Task<bool> TryClaimAsync(
        IntegrationSyncClaimRequest request,
        CancellationToken cancellationToken);

    Task<IntegrationSyncOutbox?> GetActiveClaimAsync(
        IntegrationSyncClaimIdentity claim,
        CancellationToken cancellationToken);

    Task<bool> MarkProviderHandoffStartedAsync(
        IntegrationSyncClaimIdentity claim,
        DateTime startedAt,
        CancellationToken cancellationToken);

    Task<bool> CompleteAsync(
        IntegrationSyncClaimIdentity claim,
        DateTime completedAt,
        CancellationToken cancellationToken);

    Task<bool> FailAsync(
        IntegrationSyncClaimIdentity claim,
        string errorMessage,
        bool isRetryable,
        TimeSpan retryDelay,
        int maxAttempts,
        DateTime failedAt,
        CancellationToken cancellationToken);

    Task<bool> ParkAmbiguousAsync(
        IntegrationSyncClaimIdentity claim,
        DateTime parkedAt,
        CancellationToken cancellationToken);

    Task<bool> ParkMalformedProcessingAsync(
        Guid tenantId,
        Guid outboxId,
        DateTime parkedAt,
        CancellationToken cancellationToken);

    Task<IntegrationSyncOutbox?> ResolveAmbiguousAsync(
        IntegrationSyncRecoveryRequest request,
        CancellationToken cancellationToken);
}

public static class IntegrationSyncFailureCodes
{
    public const string ProviderHandoffInDoubt = "provider_handoff_in_doubt";
    public const string ProviderOutcomeAmbiguous = "provider_outcome_ambiguous";
    public const string OperatorConfirmedAccepted = "operator_confirmed_accepted";
    public const string OperatorRetryDefinitelyNotAccepted = "operator_retry_definitely_not_accepted";
    public const string OperatorDeadLettered = "operator_dead_lettered";
}

public sealed record IntegrationSyncClaimRequest(
    Guid TenantId,
    Guid OutboxId,
    Guid LeaseToken,
    DateTime StartedAt,
    DateTime StaleProcessingStartedBefore);

public sealed record IntegrationSyncClaimIdentity(
    Guid TenantId,
    Guid OutboxId,
    Guid LeaseToken,
    DateTime ProcessingStartedAt);

public sealed record IntegrationSyncRecoveryRequest(
    Guid TenantId,
    Guid OutboxId,
    IntegrationSyncRecoveryDecision Decision,
    string EvidenceReference,
    Guid ActorId,
    DateTime ResolvedAt);

public enum IntegrationSyncRecoveryDecision
{
    ConfirmAccepted = 1,
    RetryDefinitelyNotAccepted = 2,
    DeadLetter = 3
}
