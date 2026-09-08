using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public sealed record NotificationFanoutClaim(
    Guid RunId,
    Guid TenantId,
    Guid OccurrenceId,
    Guid LeaseToken,
    long Fence,
    int Generation,
    NotificationFanoutAudienceCursor? Cursor);

public sealed record NotificationFanoutClaimRoundRequest(
    string LeaseOwner,
    DateTime ClaimedAt,
    TimeSpan LeaseDuration,
    int MaxTenants,
    int MaxActiveClaims,
    int MaxActiveClaimsPerTenant,
    int OptionalReminderBacklogHighWatermark,
    int OptionalReminderBacklogLowWatermark);

public sealed record NotificationFanoutClaimRoundResult(
    IReadOnlyList<NotificationFanoutClaim> Claims,
    int CandidateCount,
    int LeaseContentionCount,
    int CapacityDeferredCount,
    int UnavailableCount);

public sealed record NotificationFanoutProcessorSnapshot(
    int DueOccurrenceCount,
    int DueCoreOccurrenceCount,
    int DueOptionalReminderCount,
    int ActiveClaimCount,
    int ExpiredClaimCount,
    int SupersededOccurrenceCount,
    long ProcessedRecipientCount,
    DateTime? OldestDueAt,
    bool OptionalRemindersDeferred);

public interface INotificationFanoutRunRepository : IGenericRepository<NotificationFanoutRun, Guid>
{
    Task<NotificationFanoutRun?> GetBySourceAsync(
        Guid tenantId,
        string fanoutKind,
        int notificationEntityTypeId,
        Guid entityId,
        Guid sourceActorId,
        bool trackChanges = false,
        CancellationToken cancellationToken = default);

    Task<List<NotificationFanoutRun>> GetPendingBatchAsync(
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<NotificationFanoutRun?> GetByOccurrenceAsync(
        Guid tenantId,
        Guid occurrenceId,
        bool trackChanges = false,
        CancellationToken cancellationToken = default);

    Task<NotificationFanoutRun?> EnsurePendingOccurrenceRunAsync(
        Guid tenantId,
        Guid occurrenceId,
        Guid runId,
        CancellationToken cancellationToken);

    Task<NotificationFanoutClaimRoundResult> ClaimDueRoundAsync(
        NotificationFanoutClaimRoundRequest request,
        CancellationToken cancellationToken);

    Task<NotificationFanoutProcessorSnapshot> GetProcessorSnapshotAsync(
        DateTime observedAt,
        CancellationToken cancellationToken);

    Task<NotificationFanoutClaim?> TryClaimOccurrenceAsync(
        Guid tenantId,
        Guid occurrenceId,
        string leaseOwner,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        int maxActiveClaims,
        int maxActiveClaimsPerTenant,
        int optionalReminderBacklogHighWatermark,
        int optionalReminderBacklogLowWatermark,
        CancellationToken cancellationToken);

    Task<bool> TryRenewClaimAsync(
        NotificationFanoutClaim claim,
        DateTime observedAt,
        DateTime leaseExpiresAt,
        CancellationToken cancellationToken);

    Task<bool> TryCheckpointAsync(
        NotificationFanoutClaim claim,
        NotificationFanoutAudienceCursor? expectedCursor,
        NotificationFanoutAudienceCursor nextCursor,
        int processedDelta,
        int createdDelta,
        DateTime observedAt,
        CancellationToken cancellationToken);

    Task<bool> TryCompleteAsync(
        NotificationFanoutClaim claim,
        DateTime observedAt,
        CancellationToken cancellationToken);
}
