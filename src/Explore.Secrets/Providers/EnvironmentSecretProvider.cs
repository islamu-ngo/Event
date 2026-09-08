using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Microsoft.Extensions.Logging;

namespace Explore.Secrets.Providers;

/// <summary>
/// Secret provider that reads from environment variables.
/// Maps canonical keys (e.g., "Database:ConnectionString") to environment variable format
/// (e.g., "DATABASE__CONNECTIONSTRING").
/// </summary>
public sealed class EnvironmentSecretProvider : ISecretProvider
{
    private readonly ILogger<EnvironmentSecretProvider> _logger;
    private readonly UserSecretsAuthority? _userSecretsAuthority;
    private bool _initialized;

    public EnvironmentSecretProvider(
        ILogger<EnvironmentSecretProvider> logger,
        UserSecretsAuthority? userSecretsAuthority = null)
    {
        _logger = logger;
        _userSecretsAuthority = userSecretsAuthority;
    }

    /// <inheritdoc />
    public SecretProviderType ProviderType => _userSecretsAuthority is null
        ? SecretProviderType.Environment
        : SecretProviderType.UserSecrets;

    /// <inheritdoc />
    public bool SupportsRefresh => false;

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _userSecretsAuthority?.EnsureAllowed();
        _initialized = true;
        _logger.LogInformation("Local secret provider initialized authority={Authority}", ProviderType);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var envVarName = ConvertKeyToEnvVar(key);
        return Task.FromResult(_userSecretsAuthority is null
            ? Environment.GetEnvironmentVariable(envVarName)
            : _userSecretsAuthority.Get(envVarName));
    }

    /// <inheritdoc />
    public Task<SecretValue?> GetSecretWithMetadataAsync(string key, CancellationToken cancellationToken = default)
    {
        var value = GetSecretAsync(key, cancellationToken).GetAwaiter().GetResult();
        return Task.FromResult(value is not null ? new SecretValue(value) : null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> GetSecretsByPathAsync(
        string pathPrefix,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var envVarPrefix = ConvertKeyToEnvVar(pathPrefix);
        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (_userSecretsAuthority is not null)
        {
            foreach (var pair in _userSecretsAuthority.GetByPrefix(envVarPrefix))
            {
                results[ConvertEnvVarToKey(pair.Key)] = pair.Value!;
            }
        }
        else
        {
            foreach (var envVar in Environment.GetEnvironmentVariables().Keys.Cast<string>())
            {
                if (!envVar.StartsWith(envVarPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var value = Environment.GetEnvironmentVariable(envVar);
                if (!string.IsNullOrEmpty(value))
                    results[ConvertEnvVarToKey(envVar)] = value;
            }
        }

        _logger.LogDebug("secret_provider_read_completed count={Count}", results.Count);

        return Task.FromResult<IReadOnlyDictionary<string, string>>(results);
    }

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Refresh requested but local secret authorities don't support refresh");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ProviderHealthInfo> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        // Local authorities are healthy once their environment gate passes.
        return Task.FromResult(new ProviderHealthInfo(
            IsHealthy: _initialized,
            ProviderType,
            LastSuccessfulRefresh: null, // N/A for env provider
            ConsecutiveFailures: 0));
    }

    /// <summary>
    /// Converts a canonical key to environment variable format.
    /// "Database:ConnectionString" -> "DATABASE__CONNECTIONSTRING"
    /// </summary>
    private static string ConvertKeyToEnvVar(string key)
    {
        return key
            .Replace(":", "__")
            .Replace(".", "__")
            .ToUpperInvariant();
    }

    /// <summary>
    /// Converts an environment variable name back to canonical key format.
    /// "DATABASE__CONNECTIONSTRING" -> "Database:ConnectionString"
    /// </summary>
    private static string ConvertEnvVarToKey(string envVar)
    {
        // Split by double underscore and capitalize each part
        var parts = envVar.Split("__", StringSplitOptions.RemoveEmptyEntries);
        var capitalizedParts = parts.Select(p =>
            char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant());
        return string.Join(":", capitalizedParts);
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException(
                "Local secret provider not initialized. Call InitializeAsync first.");
        }
    }
}
