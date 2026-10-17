using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Created once by the caller, before any database execution or retry.</summary>
public sealed record PaymentReconciliationAcquisition(
    Guid LeaseToken, string LeaseOwner, int BatchSize, DateTime ClaimedAt, DateTime LeaseExpiresAt)
{
    public static PaymentReconciliationAcquisition Create(
        string leaseOwner, int batchSize, DateTime claimedAt, TimeSpan leaseDuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        // PostgreSQL stores microseconds. Capture that precision before replay,
        // rather than comparing a rounded persisted timestamp with the original ticks.
        DateTime capturedAt = new(claimedAt.Ticks - claimedAt.Ticks % 10, claimedAt.Kind);
        DateTime deadline = capturedAt.Add(leaseDuration);
        deadline = new(deadline.Ticks - deadline.Ticks % 10, deadline.Kind);
        var acquisition = new PaymentReconciliationAcquisition(
            Guid.CreateVersion7(), leaseOwner.Trim(), batchSize, capturedAt, deadline);
        if (!acquisition.IsValidAt(capturedAt))
            throw new ArgumentException("Payment reconciliation requires a bounded owner, batch and UTC lease.");
        return acquisition;
    }

    public bool IsValidAt(DateTime observedAt) =>
        LeaseToken != Guid.Empty && !string.IsNullOrWhiteSpace(LeaseOwner) &&
        LeaseOwner.Length <= PaymentReconciliationEffect.MaxLeaseOwnerLength &&
        LeaseOwner == LeaseOwner.Trim() && BatchSize is >= 1 and <= 50 &&
        ClaimedAt != default && ClaimedAt.Kind == DateTimeKind.Utc && ClaimedAt.Ticks % 10 == 0 &&
        LeaseExpiresAt.Kind == DateTimeKind.Utc && LeaseExpiresAt.Ticks % 10 == 0 &&
        LeaseExpiresAt > ClaimedAt && observedAt.Kind == DateTimeKind.Utc && observedAt >= ClaimedAt;
}

/// <summary>Global authority admits bounded ownership claims and reports aggregate health, never payment content.</summary>
public interface IPaymentReconciliationAdmission
{
    Task<IReadOnlyList<PaymentReconciliationClaim>> ClaimDueReconciliationsAsync(
        PaymentReconciliationAcquisition acquisition, DateTime observedAt, CancellationToken cancellationToken);

    /// <summary>Returns only this acquisition's still-live rows; never admits or renews work.</summary>
    Task<IReadOnlyList<PaymentReconciliationClaim>> RecoverReconciliationAcquisitionAsync(
        PaymentReconciliationAcquisition acquisition, DateTime observedAt, CancellationToken cancellationToken);

    Task<PaymentReconciliationHealth> GetReconciliationHealthAsync(
        DateTime observedAt, CancellationToken cancellationToken);
}
