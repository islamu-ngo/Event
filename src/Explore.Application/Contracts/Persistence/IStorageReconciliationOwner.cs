namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Observes bounded managed targets and quarantines only the unchanged observation.
/// Provider absence is observed by the caller outside this owner's transaction.
/// </summary>
public interface IStorageReconciliationOwner
{
    public const int MaximumCandidates = 500;

    Task<IReadOnlyList<StorageReconciliationSnapshot>> ListMissingBackingCandidatesAsync(
        DateTime createdBeforeUtc, int limit, CancellationToken cancellationToken);

    Task<bool> TryQuarantineMissingBackingAsync(
        StorageReconciliationSnapshot snapshot, DateTime utcNow, CancellationToken cancellationToken);
}

/// <summary>Immutable metadata identity carried across the external absence observation.</summary>
public sealed record StorageReconciliationSnapshot(
    Guid Id, Guid ConcurrencyStamp, Guid TenantId, string Provider,
    Guid StorageProviderBindingId, string ObjectKey, string? ProviderVersionId,
    string LifecycleState, bool IsDeleted, DateTime? DeletedAt, DateTime CreatedAt,
    string Purpose, string? OwningResourceKind, Guid? OwningResourceId, Guid? ActorId,
    DateTime EligibilityCutoffUtc);
