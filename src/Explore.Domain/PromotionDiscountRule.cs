using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed record PromotionDiscountRule
{
    private PromotionDiscountRule(string currencyCode, long? fixedDiscountMinor, int? basisPoints, long? maximumDiscountMinor)
    {
        CurrencyCode = currencyCode;
        FixedDiscountMinor = fixedDiscountMinor;
        BasisPointDiscount = basisPoints;
        MaximumDiscountMinor = maximumDiscountMinor;
    }

    public string CurrencyCode { get; }

    public long? FixedDiscountMinor { get; }

    public int? BasisPointDiscount { get; }

    public long? MaximumDiscountMinor { get; }

    public static PromotionDiscountRule FixedMinor(string currencyCode, long fixedDiscountMinor, long? maximumDiscountMinor)
    {
        if (fixedDiscountMinor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedDiscountMinor));
        }

        return Create(currencyCode, fixedDiscountMinor, basisPoints: null, maximumDiscountMinor);
    }

    public static PromotionDiscountRule BasisPoints(string currencyCode, int basisPoints, long? maximumDiscountMinor)
    {
        if (basisPoints is <= 0 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(basisPoints));
        }

        return Create(currencyCode, fixedDiscountMinor: null, basisPoints, maximumDiscountMinor);
    }

    public long CalculateDiscountMinor(long eligibleBasisMinor)
    {
        if (eligibleBasisMinor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(eligibleBasisMinor));
        }

        long discountMinor = FixedDiscountMinor.HasValue
            ? Math.Min(FixedDiscountMinor.Value, eligibleBasisMinor)
            : MinorUnitMath.ApplyBasisPoints(eligibleBasisMinor, BasisPointDiscount!.Value);
        discountMinor = Math.Min(discountMinor, eligibleBasisMinor);
        return MaximumDiscountMinor.HasValue ? Math.Min(discountMinor, MaximumDiscountMinor.Value) : discountMinor;
    }

    private static PromotionDiscountRule Create(string currencyCode, long? fixedDiscountMinor, int? basisPoints, long? maximumDiscountMinor)
    {
        CurrencyMetadata currency = CurrencyMetadata.Get(currencyCode);
        if (currency.IsNoCurrency)
        {
            throw new ArgumentException("Promotions require a monetary currency.", nameof(currencyCode));
        }

        if (maximumDiscountMinor is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDiscountMinor));
        }

        return new PromotionDiscountRule(currency.Code, fixedDiscountMinor, basisPoints, maximumDiscountMinor);
    }
}
