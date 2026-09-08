namespace Explore.Application.Configuration;

public sealed class AuthenticationProviderDeploymentOptions
{
    public const string SectionName = "Authentication";
    public const string LocalProvider = "local";
    public const string KeycloakProvider = "keycloak";
    public const string AtprotoProvider = "atproto";

    public string? Provider { get; set; }

    public bool? AtprotoLoginEnabled { get; set; }

    public string? GetProvider()
    {
        string? provider = Provider?.Trim().ToLowerInvariant();
        return provider switch
        {
            null or "" => null,
            LocalProvider or KeycloakProvider => provider,
            AtprotoProvider when AtprotoLoginEnabled is not false => provider,
            AtprotoProvider => throw new InvalidOperationException(
                "Authentication:AtprotoLoginEnabled cannot be false when AT Protocol is the primary provider."),
            _ => throw new InvalidOperationException(
                "Authentication:Provider must be blank, 'local', 'keycloak', or 'atproto'.")
        };
    }

    public static bool IsValid(AuthenticationProviderDeploymentOptions options)
    {
        string? provider = options.Provider?.Trim();
        if (string.IsNullOrWhiteSpace(provider))
        {
            return true;
        }

        if (provider.Equals(AtprotoProvider, StringComparison.OrdinalIgnoreCase))
        {
            return options.AtprotoLoginEnabled is not false;
        }

        return provider.Equals(LocalProvider, StringComparison.OrdinalIgnoreCase)
               || provider.Equals(KeycloakProvider, StringComparison.OrdinalIgnoreCase);
    }
}
