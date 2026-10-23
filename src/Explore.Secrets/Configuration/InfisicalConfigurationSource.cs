namespace Explore.Secrets.Configuration;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Configuration source for Infisical secrets.
/// Loads secrets from Infisical and makes them available through IConfiguration.
/// </summary>
public sealed class InfisicalConfigurationSource : IConfigurationSource
{
    /// <summary>
    /// Infisical server URL (e.g., "https://infisical.example.com").
    /// </summary>
    public required string Url { get; set; }

    /// <summary>
    /// Infisical project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// Client ID for Universal Auth.
    /// </summary>
    public required string ClientId { get; set; }

    /// <summary>
    /// Client secret for Universal Auth.
    /// </summary>
    public required string ClientSecret { get; set; }

    /// <summary>
    /// Environment slug (e.g., "dev", "staging", "prod").
    /// </summary>
    public required string Environment { get; set; }

    /// <summary>
    /// Exact consumer roots to load. Empty and broad roots are rejected before authentication.
    /// </summary>
    public List<string> Paths { get; set; } = [];

    public SecretConsumer Consumer { get; set; } = SecretConsumer.Api;

    internal Func<HttpMessageHandler>? MessageHandlerFactory { get; set; }

    /// <summary>
    /// Whether to throw on first load failure or continue with empty configuration.
    /// </summary>
    public bool ThrowOnFirstLoadFailure { get; set; } = true;

    /// <summary>
    /// Whether to reload configuration periodically.
    /// </summary>
    public bool ReloadOnChange { get; set; } = false;

    /// <summary>
    /// Interval for reloading secrets from Infisical.
    /// </summary>
    public TimeSpan ReloadInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new InfisicalConfigurationProvider(this);
    }
}
