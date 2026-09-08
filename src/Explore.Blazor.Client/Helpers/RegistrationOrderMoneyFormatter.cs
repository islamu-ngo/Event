namespace Explore.Blazor.Client.Helpers;

public static class RegistrationOrderMoneyFormatter
{
    public static string Format(long? amountMinor, string? currencyCode) =>
        Format(amountMinor.GetValueOrDefault(), currencyCode);

    public static string Format(long amountMinor, string? currencyCode)
    {
        var currency = string.IsNullOrWhiteSpace(currencyCode) ? string.Empty : $" {currencyCode.Trim()}";
        return $"{amountMinor:N0}{currency} minor units";
    }
}
