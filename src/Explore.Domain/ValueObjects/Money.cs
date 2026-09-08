using System.Globalization;

namespace Explore.Domain.ValueObjects;

public sealed record Money
{
    private Money(long minorUnits, string currencyCode)
    {
        MinorUnits = minorUnits;
        CurrencyCode = currencyCode;
    }

    public long MinorUnits { get; }
    public string CurrencyCode { get; }

    public static Money Create(long minorUnits, string currencyCode)
    {
        if (minorUnits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minorUnits));
        }

        CurrencyMetadata currency = CurrencyMetadata.Get(currencyCode);
        if (currency.IsNoCurrency && minorUnits != 0)
        {
            throw new ArgumentException(
                "The no-currency sentinel can represent only a zero amount.",
                nameof(currencyCode));
        }

        return new Money(minorUnits, currency.Code);
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{CurrencyCode} {MinorUnits}");
}
