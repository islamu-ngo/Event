namespace Explore.Infrastructure.Services;

public sealed class AuthorizationProviderDeploymentOptions
{
    public const string SectionName = "Authorization";
    public const string LocalProvider = "local";
    public const string CerbosProvider = "cerbos";

    public string? Provider { get; set; }

    public string? GetProvider()
    {
        var provider = Provider?.Trim().ToLowerInvariant();
        return provider switch
        {
            null or "" => null,
            LocalProvider or CerbosProvider => provider,
            _ => throw new InvalidOperationException(
                "Authorization:Provider must be blank, 'local', or 'cerbos'.")
        };
    }

    public static bool IsValid(AuthorizationProviderDeploymentOptions options)
    {
        var provider = options.Provider?.Trim();
        return string.IsNullOrWhiteSpace(provider)
            || provider.Equals(LocalProvider, StringComparison.OrdinalIgnoreCase)
            || provider.Equals(CerbosProvider, StringComparison.OrdinalIgnoreCase);
    }
}
