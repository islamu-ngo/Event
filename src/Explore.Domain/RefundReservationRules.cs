using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public static class RefundReservationRules
{
    public static void EnsureReservable(
        long capturedTotalMinor,
        IEnumerable<RefundAttempt> existingAttempts,
        IEnumerable<PaymentDispute> disputes,
        long requestedTotalMinor)
    {
        ArgumentNullException.ThrowIfNull(existingAttempts);
        ArgumentNullException.ThrowIfNull(disputes);
        if (capturedTotalMinor <= 0 || requestedTotalMinor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedTotalMinor));
        }

        if (disputes.Any(static dispute => dispute.IsOpen))
        {
            throw new InvalidOperationException("An open dispute blocks ordinary refunds.");
        }

        long reservedMinor = 0;
        foreach (RefundAttempt attempt in existingAttempts)
        {
            if (attempt.ReservesCapacity)
            {
                reservedMinor = MinorUnitMath.Add(reservedMinor, attempt.Allocation.TotalMinor);
            }
        }

        if (MinorUnitMath.Add(reservedMinor, requestedTotalMinor) > capturedTotalMinor)
        {
            throw new InvalidOperationException("Refund reservation exceeds captured payment capacity.");
        }
    }
}
