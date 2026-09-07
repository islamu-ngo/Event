using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public static class PromotionDiscountAllocator
{
    public static PromotionDiscountAllocation Allocate(
        PromotionDefinition definition,
        IReadOnlyCollection<PromotionDiscountLine> lines,
        DateTime evaluatedAtUtc,
        int currentTotalRedemptions,
        int currentPurchaserRedemptions)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(lines);
        definition.EnsureRedeemable(evaluatedAtUtc, currentTotalRedemptions, currentPurchaserRedemptions);
        PromotionDiscountLine[] materializedLines = lines.ToArray();
        ValidateLines(definition, materializedLines);

        PromotionDiscountLine[] eligibleLines = materializedLines
            .Where(line => definition.Eligibility.IsEligible(line.TicketTypeId) && line.LineSubtotalMinor > 0)
            .ToArray();
        long eligibleBasisMinor = eligibleLines.Aggregate(0L, static (total, line) => MinorUnitMath.Add(total, line.LineSubtotalMinor));
        long totalDiscountMinor = definition.DiscountRule.CalculateDiscountMinor(eligibleBasisMinor);
        Dictionary<Guid, long> discountsByLine = AllocateEligibleLines(eligibleLines, eligibleBasisMinor, totalDiscountMinor);

        PromotionLineDiscountAllocation[] allocations = materializedLines
            .OrderBy(static line => line.LineId)
            .Select(line =>
            {
                long discountMinor = discountsByLine.GetValueOrDefault(line.LineId);
                return new PromotionLineDiscountAllocation(line.LineId, line.LineSubtotalMinor, discountMinor, line.LineSubtotalMinor - discountMinor);
            })
            .ToArray();
        long postDiscountOrganizerTotalMinor = allocations.Aggregate(0L, static (total, allocation) => MinorUnitMath.Add(total, allocation.PostDiscountLineSubtotalMinor));
        return new PromotionDiscountAllocation(totalDiscountMinor, postDiscountOrganizerTotalMinor, allocations);
    }

    private static Dictionary<Guid, long> AllocateEligibleLines(PromotionDiscountLine[] eligibleLines, long eligibleBasisMinor, long totalDiscountMinor)
    {
        Dictionary<Guid, long> discountsByLine = [];
        if (eligibleBasisMinor == 0 || totalDiscountMinor == 0)
        {
            return discountsByLine;
        }

        var remainders = eligibleLines.Select(line =>
        {
            Int128 product = (Int128)totalDiscountMinor * line.LineSubtotalMinor;
            long floor = ToLong(product / eligibleBasisMinor);
            discountsByLine[line.LineId] = floor;
            return new { line.LineId, Remainder = product % eligibleBasisMinor };
        }).ToArray();

        long allocatedMinor = discountsByLine.Values.Aggregate(0L, static (total, discount) => MinorUnitMath.Add(total, discount));
        long remainingMinor = totalDiscountMinor - allocatedMinor;
        foreach (var remainder in remainders.OrderByDescending(static item => item.Remainder).ThenBy(static item => item.LineId).Take((int)remainingMinor))
        {
            discountsByLine[remainder.LineId] = MinorUnitMath.Add(discountsByLine[remainder.LineId], 1);
        }

        return discountsByLine;
    }

    private static void ValidateLines(PromotionDefinition definition, PromotionDiscountLine[] lines)
    {
        if (lines.Any(static line => line.LineId == Guid.Empty || line.TicketTypeId == Guid.Empty || line.LineSubtotalMinor < 0) ||
            lines.Select(static line => line.LineId).Distinct().Count() != lines.Length ||
            lines.Any(line => !string.Equals(line.CurrencyCode, definition.DiscountRule.CurrencyCode, StringComparison.Ordinal)))
        {
            throw new ArgumentException("Promotion allocation lines must be distinct, non-negative, and currency-matched.", nameof(lines));
        }
    }

    private static long ToLong(Int128 value)
    {
        if (value < long.MinValue || value > long.MaxValue)
        {
            throw new OverflowException("Minor-unit calculation exceeds Int64 range.");
        }

        return (long)value;
    }
}
