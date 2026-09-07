using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed class PlatformFeeFixedCharge
{
    private PlatformFeeFixedCharge(string currencyCode, long amountMinor)
    {
        Id = Guid.CreateVersion7();
        CurrencyCode = currencyCode;
        AmountMinor = amountMinor;
    }

    public Guid Id { get; private set; }

    public string CurrencyCode { get; private set; } = string.Empty;

    public long AmountMinor { get; private set; }

    public static PlatformFeeFixedCharge Create(string currencyCode, long amountMinor)
    {
        CurrencyMetadata currency = CurrencyMetadata.Get(currencyCode);
        if (currency.IsNoCurrency || amountMinor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amountMinor));
        }

        return new PlatformFeeFixedCharge(currency.Code, amountMinor);
    }

    internal PlatformFeeFixedCharge Clone() => new(CurrencyCode, AmountMinor);
}
