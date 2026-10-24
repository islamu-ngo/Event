namespace Explore.Domain;

/// <summary>
/// Exact retained-store coordinates, not backup, restore or reconciliation evidence.
/// Persistence must acquire this value under the original retained writer transaction.
/// </summary>
public sealed record PrivacyErasureRecoveryState
{
    public PrivacyErasureRecoveryState(
        Guid storeId, long revision, long recoveryGeneration, PrivacyErasureAuthorityState authorityState)
    {
        if (storeId == Guid.Empty || storeId.Version != 7 || storeId.Variant is < 8 or > 11)
            throw new ArgumentException("The retained store requires a writer-issued UUIDv7 identity.", nameof(storeId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revision);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recoveryGeneration);
        ArgumentNullException.ThrowIfNull(authorityState);
        StoreId = storeId;
        Revision = revision;
        RecoveryGeneration = recoveryGeneration;
        AuthorityState = authorityState;
    }

    public Guid StoreId { get; }
    public long Revision { get; }
    public long RecoveryGeneration { get; }
    public PrivacyErasureAuthorityState AuthorityState { get; }
}
