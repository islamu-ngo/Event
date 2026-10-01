using System.Globalization;
using ISLAMU.Wire.Contracts.Identity;

namespace ISLAMU.Event.Setup.Core.Environment;

internal static class IdentityCorrelationEnvironment
{
    internal const string KeyPrefix = "IDENTITYCORRELATION__TRUSTEDISSUERS__";
    internal const string FirstKey = KeyPrefix + "0";

    internal static bool IsIssuerKey(string key)
    {
        if (!key.StartsWith(KeyPrefix, StringComparison.Ordinal))
            return false;
        ReadOnlySpan<char> suffix = key.AsSpan(KeyPrefix.Length);
        return int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
            && index >= 0
            && suffix.SequenceEqual(index.ToString(CultureInfo.InvariantCulture));
    }

    internal static string? NormalizeIssuer(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('*', StringComparison.Ordinal))
            return null;
        try
        {
            return OidcIssuerAuthority.Normalize(value);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
