using Explore.Domain;

namespace Explore.Application.Contracts.Recovery;

public sealed record TicketingRecoveryHealth(
    TicketingRecoveryStatus Status,
    int PendingReissues,
    int AmbiguousEffects,
    DateTime? OldestPendingAt);

public interface ITicketingRecoveryOperatorStore
{
    Task<TicketingRecoveryCheckpoint> BeginRecoveryAsync(
        TicketingRecoveryManifest manifest,
        DateTime createdAtUtc,
        CancellationToken cancellationToken);

    Task<TicketingRecoveryCheckpoint?> GetAsync(
        Guid tenantId,
        Guid recoveryOperationId,
        CancellationToken cancellationToken);

    Task<TicketingRecoveryCheckpoint?> ValidateAndRotateAsync(
        Guid tenantId,
        Guid recoveryOperationId,
        string runningReleaseRevision,
        string runningSchemaRevision,
        int minimumRetainedKeyVersion,
        long minimumAuthorityFloor,
        long minimumProviderCursor,
        long minimumIdempotencyFloor,
        long minimumWorkerFence,
        int nextCapabilityGeneration,
        int nextCredentialGeneration,
        long nextWorkerFence,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken);

    Task<bool> StopSalesAsync(
        Guid tenantId,
        Guid recoveryOperationId,
        long nextWorkerFence,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken);

    Task<bool> PauseWorkersAsync(
        Guid tenantId,
        Guid recoveryOperationId,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken);

    Task<bool> OpenWorkersAsync(
        Guid tenantId,
        Guid recoveryOperationId,
        long expectedWorkerFence,
        DateTime openedAtUtc,
        CancellationToken cancellationToken);

    Task<bool> OpenSalesAsync(
        Guid tenantId,
        Guid recoveryOperationId,
        DateTime openedAtUtc,
        CancellationToken cancellationToken);

    Task<bool> ResolveUnknownAsync(
        Guid tenantId,
        Guid effectId,
        long expectedFence,
        bool retry,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken);

    Task<bool> DeadLetterAsync(
        Guid tenantId,
        Guid effectId,
        long expectedFence,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken);

    Task<TicketingRecoveryHealth?> GetHealthAsync(
        Guid tenantId,
        Guid recoveryOperationId,
        CancellationToken cancellationToken);

}
