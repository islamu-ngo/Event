using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Secrets.Providers;

/// <summary>
/// Secret provider that retrieves secrets from Infisical using Universal Auth via direct REST API calls.
/// Uses HttpClient with IPv4 forcing instead of Infisical.Sdk (whose Rust FFI hangs against self-hosted instances).
/// Caches secrets locally and supports periodic refresh.
/// </summary>
public sealed class InfisicalSecretProvider : ISecretProvider, IAsyncDisposable
{
    private readonly ILogger<InfisicalSecretProvider> _logger;
    private readonly InfisicalOptions _options;
    private readonly ConcurrentDictionary<string, SecretValue> _secretCache = new(StringComparer.OrdinalIgnoreCase);

    private string? _accessToken;
    private bool _initialized;
    private DateTime? _lastSuccessfulRefresh;
    private int _consecutiveFailures;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public InfisicalSecretProvider(
        ILogger<InfisicalSecretProvider> logger,
        IOptions<SecretProviderOptions> options)
    {
        _logger = logger;
        _options = options.Value.Infisical;
    }

    /// <inheritdoc />
    public SecretProviderType ProviderType => SecretProviderType.Infisical;

    /// <inheritdoc />
    public bool SupportsRefresh => true;

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                _logger.LogDebug("Infisical provider already initialized");
                return;
            }

            ValidateConfiguration();

            _logger.LogInformation("secret_provider_initializing");

            await AuthenticateAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("Infisical authentication successful");

            // Load initial secrets
            await LoadSecretsAsync(cancellationToken).ConfigureAwait(false);

            _initialized = true;
            _lastSuccessfulRefresh = DateTime.UtcNow;
            _consecutiveFailures = 0;

            _logger.LogInformation("secret_provider_initialized");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SecretProviderException)
        {
            _consecutiveFailures++;
            _logger.LogError("secret_provider_initialization_failed");
            throw;
        }
        catch (Exception)
        {
            _consecutiveFailures++;
            _logger.LogError("secret_provider_initialization_failed");
            throw SecretProviderException.Permanent(
                "secret_provider_initialization_failed",
                SecretProviderType.Infisical,
                "Initialize");
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <inheritdoc />
    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        if (_secretCache.TryGetValue(key, out var secret))
        {
            _logger.LogDebug("secret_cache_hit");
            return Task.FromResult<string?>(secret.Value);
        }

        _logger.LogDebug("secret_cache_miss");
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task<SecretValue?> GetSecretWithMetadataAsync(string key, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        if (_secretCache.TryGetValue(key, out var secret))
        {
            return Task.FromResult<SecretValue?>(secret);
        }

        return Task.FromResult<SecretValue?>(null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> GetSecretsByPathAsync(
        string pathPrefix,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var normalizedPrefix = NormalizePath(pathPrefix);

        foreach (var (key, secret) in _secretCache)
        {
            // Check if the key starts with the path prefix
            if (key.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                results[key] = secret.Value;
            }
        }

        _logger.LogDebug("secret_provider_read_completed count={Count}", results.Count);

        return Task.FromResult<IReadOnlyDictionary<string, string>>(results);
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogDebug("Refreshing secrets from Infisical");

            var previousCount = _secretCache.Count;
            await LoadSecretsAsync(cancellationToken);

            _lastSuccessfulRefresh = DateTime.UtcNow;
            _consecutiveFailures = 0;

            _logger.LogInformation(
                "Refreshed {Count} secrets from Infisical (previous: {Previous})",
                _secretCache.Count,
                previousCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _consecutiveFailures++;
            _logger.LogWarning(
                "secret_provider_refresh_failed consecutive_failures={Failures}",
                _consecutiveFailures);

            throw SecretProviderException.Transient(
                "secret_provider_refresh_failed",
                SecretProviderType.Infisical,
                "Refresh");
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <inheritdoc />
    public Task<ProviderHealthInfo> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ProviderHealthInfo(
            IsHealthy: _initialized && _consecutiveFailures < 3,
            ProviderType: SecretProviderType.Infisical,
            LastSuccessfulRefresh: _lastSuccessfulRefresh,
            ConsecutiveFailures: _consecutiveFailures));
    }

    /// <summary>
    /// Authenticates with Infisical using Universal Auth via direct REST API.
    /// </summary>
    private async Task AuthenticateAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_accessToken))
        {
            return;
        }

        using var handler = InfisicalConfigurationProvider.CreateIpv4Handler();
        using var http = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(15),
        };

        var effectiveUrl = (_options.Url ?? string.Empty).TrimEnd('/');
        var loginResp = await http.PostAsJsonAsync(
            $"{effectiveUrl}/api/v1/auth/universal-auth/login",
            new { clientId = _options.ClientId, clientSecret = _options.ClientSecret },
            cancellationToken).ConfigureAwait(false);

        if (!loginResp.IsSuccessStatusCode)
        {
            throw SecretProviderException.Permanent(
                "secret_provider_initialization_failed",
                SecretProviderType.Infisical,
                "UniversalAuth");
        }

        var loginJson = await loginResp.Content
            .ReadFromJsonAsync<InfisicalConfigurationProvider.InfisicalLoginResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _accessToken = loginJson?.AccessToken;
        if (string.IsNullOrEmpty(_accessToken))
        {
            throw SecretProviderException.Permanent(
                "secret_provider_initialization_failed",
                SecretProviderType.Infisical,
                "UniversalAuth");
        }
    }

    /// <summary>
    /// Loads secrets from all configured paths into the cache via direct REST API.
    /// </summary>
    private async Task LoadSecretsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_accessToken))
        {
            await AuthenticateAsync(cancellationToken).ConfigureAwait(false);
        }

        using var handler = InfisicalConfigurationProvider.CreateIpv4Handler();
        using var http = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(15),
        };

        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

        var effectiveUrl = (_options.Url ?? string.Empty).TrimEnd('/');
        var newSecrets = new Dictionary<string, SecretValue>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in _options.Paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var listUrl =
                    $"{effectiveUrl}/api/v3/secrets/raw"
                    + $"?workspaceId={Uri.EscapeDataString(_options.ProjectId!)}"
                    + $"&environment={Uri.EscapeDataString(_options.Environment)}"
                    + $"&secretPath={Uri.EscapeDataString(path)}"
                    + "&expandSecretReferences=true&recursive=true";

                var listResp = await http.GetAsync(listUrl, cancellationToken).ConfigureAwait(false);

                if (!listResp.IsSuccessStatusCode)
                {
                    _logger.LogError("secret_provider_path_unavailable status={StatusCode} path={Path}", listResp.StatusCode, path);
                    throw SecretProviderException.Transient(
                        "secret_provider_path_unavailable",
                        SecretProviderType.Infisical,
                        "ListSecrets");
                }

                var listJson = await listResp.Content
                    .ReadFromJsonAsync<InfisicalConfigurationProvider.InfisicalListSecretsResponse>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (listJson?.Secrets is null || listJson.Secrets.Count == 0)
                {
                    _logger.LogWarning("secret_provider_path_empty");
                    continue;
                }

                foreach (var secret in listJson.Secrets)
                {
                    if (string.IsNullOrEmpty(secret.SecretKey)) continue;

                    var canonicalKey = ConvertToCanonicalKey(secret.SecretKey, path);
                    var secretValue = new SecretValue(
                        secret.SecretValue ?? string.Empty,
                        Version: secret.Version?.ToString());

                    newSecrets[canonicalKey] = secretValue;

                    _logger.LogTrace("secret_provider_item_loaded");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not SecretProviderException)
            {
                _logger.LogError(ex, "secret_provider_path_unavailable");
                throw SecretProviderException.Transient(
                    "secret_provider_path_unavailable",
                    SecretProviderType.Infisical,
                    "ListSecrets");
            }
        }

        // Atomic swap of cache
        _secretCache.Clear();
        foreach (var (key, value) in newSecrets)
        {
            _secretCache[key] = value;
        }
    }

    /// <summary>
    /// Converts an Infisical secret key to canonical format.
    /// e.g., "KEYCLOAK_ENDPOINT" with path "/keycloak" -> "Keycloak:Endpoint"
    /// </summary>
    private static string ConvertToCanonicalKey(string infisicalKey, string path)
    {
        // Normalize path to section name
        var section = path.Trim('/').Replace("/", ":");

        // Convert SCREAMING_SNAKE_CASE to PascalCase
        var parts = infisicalKey.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var pascalCaseParts = parts.Select(p =>
            char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant());
        var pascalCaseKey = string.Join("", pascalCaseParts);

        // If path provides context, use it
        if (!string.IsNullOrEmpty(section))
        {
            // Check if the key already starts with the section name
            var sectionParts = section.Split(':', StringSplitOptions.RemoveEmptyEntries);
            var firstSectionPart = sectionParts.FirstOrDefault()?.ToUpperInvariant();

            if (firstSectionPart is not null &&
                infisicalKey.StartsWith(firstSectionPart, StringComparison.OrdinalIgnoreCase))
            {
                // Key already includes section, just convert to Pascal
                return pascalCaseKey.Replace(firstSectionPart,
                    char.ToUpperInvariant(firstSectionPart[0]) + firstSectionPart[1..].ToLowerInvariant());
            }

            return $"{char.ToUpperInvariant(section[0])}{section[1..]}:{pascalCaseKey}";
        }

        return pascalCaseKey;
    }

    /// <summary>
    /// Normalizes a path for comparison.
    /// </summary>
    private static string NormalizePath(string path)
    {
        return path.Trim('/').Replace("/", ":");
    }

    /// <summary>
    /// Validates that required configuration is present.
    /// </summary>
    private void ValidateConfiguration()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(_options.Url))
            errors.Add("Infisical Url is required");

        if (string.IsNullOrWhiteSpace(_options.ProjectId))
            errors.Add("Infisical ProjectId is required");

        if (string.IsNullOrWhiteSpace(_options.ClientId))
            errors.Add("Infisical ClientId is required");

        if (string.IsNullOrWhiteSpace(_options.ClientSecret))
            errors.Add("Infisical ClientSecret is required");

        if (errors.Count > 0)
        {
            throw SecretProviderException.Permanent(
                $"Invalid Infisical configuration: {string.Join(", ", errors)}",
                SecretProviderType.Infisical,
                "Initialize");
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException(
                "Infisical secret provider not initialized. Call InitializeAsync first.");
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _initLock.Dispose();
        _refreshLock.Dispose();
        _initialized = false;
        _accessToken = null;
        return ValueTask.CompletedTask;
    }
}
