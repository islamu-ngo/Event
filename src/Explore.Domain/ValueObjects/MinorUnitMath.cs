namespace Explore.Domain.ValueObjects;

public static class MinorUnitMath
{
    public static long Add(long leftMinor, long rightMinor) => ToLong((Int128)leftMinor + rightMinor);

    public static long Multiply(long leftMinor, long rightMinor) => ToLong((Int128)leftMinor * rightMinor);

    public static long ApplyBasisPoints(long amountMinor, int basisPoints)
    {
        if (amountMinor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amountMinor));
        }

        if (basisPoints is < 0 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(basisPoints));
        }

        return ToLong((((Int128)amountMinor * basisPoints) + 5_000) / 10_000);
    }

    private static long ToLong(Int128 value)
    {
        if (value < (Int128)long.MinValue || value > (Int128)long.MaxValue)
        {
            throw new OverflowException("Minor-unit calculation exceeds Int64 range.");
        }

        return (long)value;
    }
}
