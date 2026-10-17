using System.Globalization;

namespace ISLAMU.Event.Setup.Core.Environment;

internal static class KeycloakTokenEnvironment
{
    internal const string Audience = "KEYCLOAK__AUDIENCE";
    internal const string ClientPrefix = "KEYCLOAK__PERMITTEDCLIENTS__";
    internal const string FirstClient = ClientPrefix + "0";

    internal static bool IsClientKey(string key)
    {
        if (!key.StartsWith(ClientPrefix, StringComparison.Ordinal))
            return false;
        ReadOnlySpan<char> suffix = key.AsSpan(ClientPrefix.Length);
        return int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
            && index >= 0 && suffix.SequenceEqual(index.ToString(CultureInfo.InvariantCulture));
    }
}
