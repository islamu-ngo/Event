using Explore.Domain.Enums;

namespace Explore.Domain;

public static class AuthenticationProviderKindExtensions
{
    public static AuthenticationProviderKind ParseAuthenticationProviderKind(this string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        return provider.Trim().ToLowerInvariant() switch
        {
            "keycloak" => AuthenticationProviderKind.Keycloak,
            "atproto" => AuthenticationProviderKind.Atproto,
            "google" => AuthenticationProviderKind.Google,
            "local" => AuthenticationProviderKind.Local,
            "dev" or "development" => AuthenticationProviderKind.Development,
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                "Authentication provider code is outside the closed Domain lookup."),
        };
    }

    public static string ToAuthenticationProviderCode(this AuthenticationProviderKind provider) =>
        provider switch
        {
            AuthenticationProviderKind.Keycloak => "keycloak",
            AuthenticationProviderKind.Atproto => "atproto",
            AuthenticationProviderKind.Google => "google",
            AuthenticationProviderKind.Local => "local",
            AuthenticationProviderKind.Development => "development",
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                "Authentication provider kind is outside the closed Domain lookup."),
        };
}
