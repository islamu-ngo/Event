using Explore.Domain.Interfaces;
using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed class RefundLineAllocation : ITenantEntity
{
    private RefundLineAllocation()
    {
    }

    public Guid TenantId { get; set; }
    public Guid RefundAttemptId { get; private set; }
    public Guid PaidOrderAcceptanceSnapshotId { get; private set; }
    public Guid OrderLineId { get; private set; }
    public int Ordinal { get; private set; }
    public long OrganizerAmountMinor { get; private set; }
    public long PlatformFeeMinor { get; private set; }
    public long PlatformContributionMinor { get; private set; }
    public long TotalMinor { get; private set; }

    internal static IReadOnlyList<RefundLineAllocation> Allocate(
        Guid tenantId,
        Guid refundAttemptId,
        RefundAllocation allocation,
        IReadOnlyCollection<PaidOrderAcceptanceLine> acceptedLines)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        ArgumentNullException.ThrowIfNull(acceptedLines);
        PaidOrderAcceptanceLine[] lines = acceptedLines.OrderBy(line => line.Ordinal).ToArray();
        if (tenantId == Guid.Empty || refundAttemptId == Guid.Empty || lines.Length == 0 ||
            lines.Any(line => line.TenantId != tenantId || line.LineTotalMinor < 0))
        {
            throw new ArgumentException("Refund lines require valid accepted tenant lineage.", nameof(acceptedLines));
        }

        long weightTotal = checked(lines.Sum(line => line.LineTotalMinor));
        if (weightTotal <= 0)
        {
            throw new ArgumentException("Refund lines require positive accepted value.", nameof(acceptedLines));
        }

        long[] organizer = AllocateComponent(allocation.OrganizerAmountMinor, lines, weightTotal);
        long[] fee = AllocateFromCapacity(allocation.PlatformFeeMinor, organizer, lines, line => line.Ordinal);
        long[] contribution = AllocateComponent(allocation.PlatformContributionMinor, lines, weightTotal);
        return lines.Select((line, index) => new RefundLineAllocation
        {
            TenantId = tenantId,
            RefundAttemptId = refundAttemptId,
            PaidOrderAcceptanceSnapshotId = line.PaidOrderAcceptanceSnapshotId,
            OrderLineId = line.OrderLineId,
            Ordinal = line.Ordinal,
            OrganizerAmountMinor = organizer[index],
            PlatformFeeMinor = fee[index],
            PlatformContributionMinor = contribution[index],
            TotalMinor = MinorUnitMath.Add(organizer[index], contribution[index])
        }).ToArray();
    }

    internal static IReadOnlyList<RefundLineAllocation> AllocateFromRemaining(
        Guid tenantId,
        Guid refundAttemptId,
        RefundAllocation allocation,
        PaidOrderAcceptanceSnapshot acceptance,
        IReadOnlyCollection<RefundAttempt> existingAttempts)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        ArgumentNullException.ThrowIfNull(acceptance);
        ArgumentNullException.ThrowIfNull(existingAttempts);
        PaidOrderAcceptanceLine[] lines = acceptance.Lines.OrderBy(line => line.Ordinal).ToArray();
        RefundAllocation full = RefundAllocation.AllocatePartial(
            acceptance.TotalMinor,
            acceptance.OrganizerAmountMinor,
            acceptance.PlatformFeeMinor,
            acceptance.PlatformContributionMinor);
        RefundLineAllocation[] capacities = Allocate(tenantId, refundAttemptId, full, lines).ToArray();
        RefundLineAllocation[] used = existingAttempts
            .Where(existing => existing.ReservesCapacity)
            .SelectMany(existing => existing.Lines)
            .ToArray();
        long[] organizerCapacity = GetRemainingCapacity(capacities, used, line => line.OrganizerAmountMinor);
        long[] fee = AllocateFromCapacity(
            allocation.PlatformFeeMinor,
            organizerCapacity,
            capacities,
            line => line.Ordinal);
        long[] organizerRemainder = organizerCapacity
            .Zip(fee, (capacity, allocatedFee) => checked(capacity - allocatedFee))
            .ToArray();
        long[] organizerExtra = AllocateFromCapacity(
            checked(allocation.OrganizerAmountMinor - allocation.PlatformFeeMinor),
            organizerRemainder,
            capacities,
            line => line.Ordinal);
        long[] organizer = fee.Zip(organizerExtra, MinorUnitMath.Add).ToArray();
        long[] contribution = AllocateFromCapacity(
            allocation.PlatformContributionMinor,
            GetRemainingCapacity(capacities, used, line => line.PlatformContributionMinor),
            capacities,
            line => line.Ordinal);
        return lines.Select((line, index) => new RefundLineAllocation
        {
            TenantId = tenantId,
            RefundAttemptId = refundAttemptId,
            PaidOrderAcceptanceSnapshotId = line.PaidOrderAcceptanceSnapshotId,
            OrderLineId = line.OrderLineId,
            Ordinal = line.Ordinal,
            OrganizerAmountMinor = organizer[index],
            PlatformFeeMinor = fee[index],
            PlatformContributionMinor = contribution[index],
            TotalMinor = MinorUnitMath.Add(organizer[index], contribution[index])
        }).ToArray();
    }

    private static long[] GetRemainingCapacity(
        IReadOnlyList<RefundLineAllocation> capacities,
        IReadOnlyCollection<RefundLineAllocation> used,
        Func<RefundLineAllocation, long> component)
    {
        long[] remaining = capacities.Select(capacity => checked(
            component(capacity) - used.Where(line => line.OrderLineId == capacity.OrderLineId).Sum(component))).ToArray();
        if (remaining.Any(value => value < 0))
        {
            throw new InvalidOperationException("Persisted refund lines exceed accepted line capacity.");
        }
        return remaining;
    }

    private static long[] AllocateFromCapacity<TLine>(
        long amountMinor,
        IReadOnlyList<long> capacity,
        IReadOnlyList<TLine> lines,
        Func<TLine, int> ordinal)
    {
        if (amountMinor < 0 || capacity.Count != lines.Count || capacity.Any(value => value < 0) ||
            amountMinor > capacity.Sum())
        {
            throw new InvalidOperationException("Persisted refund lines exceed accepted line capacity.");
        }
        if (amountMinor == 0)
        {
            return new long[capacity.Count];
        }

        long remainingTotal = capacity.Sum();
        var shares = new long[capacity.Count];
        var remainders = new (int Index, Int128 Remainder)[capacity.Count];
        long distributed = 0;
        for (int index = 0; index < capacity.Count; index++)
        {
            Int128 numerator = (Int128)amountMinor * capacity[index];
            shares[index] = checked((long)(numerator / remainingTotal));
            distributed = MinorUnitMath.Add(distributed, shares[index]);
            remainders[index] = (index, numerator % remainingTotal);
        }
        foreach ((int index, _) in remainders
                     .OrderByDescending(item => item.Remainder)
                     .ThenBy(item => ordinal(lines[item.Index]))
                     .Take(checked((int)(amountMinor - distributed))))
        {
            shares[index] = MinorUnitMath.Add(shares[index], 1);
        }
        return shares;
    }

    private static long[] AllocateComponent(
        long amountMinor,
        IReadOnlyList<PaidOrderAcceptanceLine> lines,
        long weightTotal)
    {
        var shares = new long[lines.Count];
        var remainders = new (int Index, Int128 Remainder)[lines.Count];
        long distributed = 0;
        for (int index = 0; index < lines.Count; index++)
        {
            Int128 numerator = (Int128)amountMinor * lines[index].LineTotalMinor;
            shares[index] = checked((long)(numerator / weightTotal));
            distributed = MinorUnitMath.Add(distributed, shares[index]);
            remainders[index] = (index, numerator % weightTotal);
        }

        long remaining = amountMinor - distributed;
        foreach ((int index, _) in remainders
                     .OrderByDescending(item => item.Remainder)
                     .ThenBy(item => lines[item.Index].Ordinal)
                     .Take(checked((int)remaining)))
        {
            shares[index] = MinorUnitMath.Add(shares[index], 1);
        }
        return shares;
    }
}
