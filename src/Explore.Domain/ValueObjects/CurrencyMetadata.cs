namespace Explore.Domain.ValueObjects;

public readonly record struct CurrencyMetadata(
    string Code,
    int MinorUnitDigits,
    long MinorUnitsPerMajorUnit,
    bool IsNoCurrency)
{
    public static CurrencyMetadata Get(string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            throw new ArgumentException("Currency code is required.", nameof(currencyCode));
        }

        string normalizedCurrencyCode = currencyCode.Trim().ToUpperInvariant();
        if (normalizedCurrencyCode.Length != 3 || normalizedCurrencyCode.Any(static character => !char.IsAsciiLetter(character)))
        {
            throw new ArgumentException("Currency code must be a three-letter ISO-style code.", nameof(currencyCode));
        }

        return normalizedCurrencyCode switch
        {
            "BHD" or "IQD" or "JOD" or "KWD" or "LYD" or "OMR" or "TND" => new CurrencyMetadata(normalizedCurrencyCode, 3, 1_000, false),
            "BIF" or "CLP" or "DJF" or "GNF" or "JPY" or "KMF" or "KRW" or "MGA" or "PYG" or "RWF" or "UGX" or "VND" or "VUV" or "XAF" or "XOF" or "XPF" => new CurrencyMetadata(normalizedCurrencyCode, 0, 1, false),
            "AED" or "AUD" or "BDT" or "BRL" or "CAD" or "CHF" or "CNY" or "CZK" or "DKK" or "EGP" or "EUR" or "GBP" or "HKD" or "HUF" or "IDR" or "ILS" or "INR" or "KES" or "MAD" or "MXN" or "MYR" or "NGN" or "NOK" or "NZD" or "PHP" or "PKR" or "PLN" or "QAR" or "RON" or "SAR" or "SEK" or "SGD" or "THB" or "TRY" or "TWD" or "UAH" or "USD" or "ZAR" => new CurrencyMetadata(normalizedCurrencyCode, 2, 100, false),
            "XXX" => new CurrencyMetadata(normalizedCurrencyCode, 0, 1, true),
            _ => throw new ArgumentException("Currency code is not supported.", nameof(currencyCode))
        };
    }
}
