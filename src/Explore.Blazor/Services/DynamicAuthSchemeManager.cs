using Event.Web.BffHosting.Authentication;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Explore.Blazor.Services;

public class DynamicAuthSchemeManager : IDynamicAuthSchemeManager, IDisposable
{
    private readonly IAuthenticationSchemeProvider _schemeProvider;
    private readonly IOptionsMonitorCache<OpenIdConnectOptions> _oidcOptionsCache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IDataProtectionProvider _dataProtection;
    private readonly IEventBffOidcOptionsFactory _oidcOptionsFactory;
    private readonly ILogger<DynamicAuthSchemeManager> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly object _registeredSchemesSync = new();

    /// <summary>
    /// Tracks which dynamic provider schemes are currently registered.
    /// </summary>
    private readonly HashSet<string> _registeredSchemes = [];

    // Tracks the last-known Keycloak client secret so it can be preserved
    // when ApplyConfiguration is called without secrets (includeSecrets: false).
    private string? _currentKeycloakSecret;
    private readonly string? _deploymentPrimaryProvider;
    private string _activePrimaryProvider;

    public DynamicAuthSchemeManager(
        IAuthenticationSchemeProvider schemeProvider,
        IOptionsMonitorCache<OpenIdConnectOptions> oidcOptionsCache,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IDataProtectionProvider dataProtection,
        IEventBffOidcOptionsFactory oidcOptionsFactory,
        ILogger<DynamicAuthSchemeManager> logger)
    {
        _schemeProvider = schemeProvider;
        _oidcOptionsCache = oidcOptionsCache;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _dataProtection = dataProtection;
        _oidcOptionsFactory = oidcOptionsFactory;
        _logger = logger;
        _deploymentPrimaryProvider = ResolveDeploymentPrimaryProvider(
            configuration["Authentication:Provider"]);
        _activePrimaryProvider = _deploymentPrimaryProvider ?? "local";
    }

    public async Task InitializeAsync()
    {
        await _lock.WaitAsync();
        try
        {
            _logger.LogInformation("Initializing dynamic auth schemes...");

            // 1. Check environment variables for Keycloak (legacy/Docker config)
            var envAuthority = _configuration["Keycloak:Authority"];
            var envClientId = _configuration["Keycloak:ClientId"];
            var envClientSecret = _configuration["Keycloak:ClientSecret"];
            var envMetadataAddress = _configuration["Keycloak:MetadataAddress"];
            var envGoogleClientId = _configuration["Google:ClientId"];
            var envGoogleClientSecret = _configuration["Google:ClientSecret"];

            if (!string.IsNullOrEmpty(envAuthority) && !string.IsNullOrEmpty(envClientId))
            {
                if (_deploymentPrimaryProvider is null)
                {
                    _activePrimaryProvider = "keycloak";
                }

                _logger.LogInformation(
                    "Keycloak config detected in environment variables — registering Keycloak scheme from env " +
                    "(authority={Authority}, clientId={ClientId}, hasSecret={HasSecret})",
                    envAuthority, envClientId, !string.IsNullOrEmpty(envClientSecret));

                if (string.IsNullOrEmpty(envClientSecret))
                {
                    _logger.LogWarning(
"⚠️ KEYCLOAK_BLAZOR_CLIENT_SECRET is NOT set — Keycloak confidential clients will fail " +
                    "token exchange with 'unauthorized_client'. Ensure KEYCLOAK_BLAZOR_CLIENT_SECRET is set " +
                    "in Infisical (path: /keycloak) or as an environment variable.");
                }

                RegisterKeycloakScheme(envAuthority, envClientId, envClientSecret, envMetadataAddress);
            }

            if (!string.IsNullOrEmpty(envGoogleClientId)
                && !string.Equals(
                    _activePrimaryProvider,
                    "atproto",
                    StringComparison.Ordinal))
            {
                _logger.LogInformation("Google config detected in environment/configuration — registering Google scheme from env");
                RegisterGoogleScheme(envGoogleClientId, envGoogleClientSecret);
            }

            if (string.Equals(
                    _activePrimaryProvider,
                    "atproto",
                    StringComparison.Ordinal)
                || _configuration.GetValue<bool>(
                    "Authentication:AtprotoLoginEnabled"))
            {
                RegisterAtprotoScheme(
                    _configuration["Atproto:PublicUrl"]
                    ?? string.Empty);
            }

            // 2. Read DB configuration via API (may override or add providers).
            //    At startup, the setup secret is not available, so we read without secrets.
            //    Env-var Keycloak is the only scheme that works at cold start.
            //    After setup flow, RefreshSchemesAsync is called with secrets from the cookie.
            AuthProviderConfigurationDto? dbConfig = null;
            try
            {
                dbConfig = await FetchConfigFromApiAsync(includeSecrets: false, setupSecret: null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not read auth provider configuration from API at startup — " +
                    "this is expected on first run before setup is completed");
            }

            if (dbConfig is not null)
            {
                ApplyConfiguration(dbConfig);
            }

            _logger.LogInformation(
                "Dynamic auth scheme initialization complete. Registered providers: [{Providers}]",
                string.Join(", ", SnapshotRegisteredSchemes()));
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RefreshSchemesAsync(string? setupSecret = null)
    {
        await _lock.WaitAsync();
        try
        {
            _logger.LogInformation("Refreshing dynamic auth schemes from API...");

            var config = await FetchConfigFromApiAsync(
                includeSecrets: !string.IsNullOrEmpty(setupSecret),
                setupSecret: setupSecret);

            if (config is not null)
            {
                ApplyConfiguration(config);
            }

            _logger.LogInformation(
                "Dynamic auth scheme refresh complete. Registered providers: [{Providers}]",
                string.Join(", ", SnapshotRegisteredSchemes()));
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<IReadOnlyList<string>> GetRegisteredProviderSchemesAsync()
    {
        return Task.FromResult<IReadOnlyList<string>>(SnapshotRegisteredSchemes().AsReadOnly());
    }

    public string GetActivePrimaryProvider() =>
        Volatile.Read(ref _activePrimaryProvider);

    private List<string> SnapshotRegisteredSchemes()
    {
        lock (_registeredSchemesSync)
        {
            return [.. _registeredSchemes];
        }
    }

    private async Task<AuthProviderConfigurationDto?> FetchConfigFromApiAsync(
        bool includeSecrets,
        string? setupSecret)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var apiClient = scope.ServiceProvider.GetRequiredService<IInstanceOnboardingClient>();
        try
        {
            return includeSecrets && !string.IsNullOrEmpty(setupSecret)
                ? await apiClient.GetInstanceOnboardingAuthProviderConfigurationInternalAsync()
                : await apiClient.GetInstanceOnboardingAuthProviderConfigurationAsync();
        }
        catch (ApiException ex)
        {
            _logger.LogDebug(
                "Auth config API returned {StatusCode}; keeping the current provider configuration.",
                ex.StatusCode);
            return null;
        }
    }

    private void ApplyConfiguration(AuthProviderConfigurationDto config)
    {
        if (_deploymentPrimaryProvider is null)
        {
            _activePrimaryProvider = config.PrimaryProviderId switch
            {
                1 => "keycloak",
                2 => "atproto",
                4 or null => "local",
                _ => throw new InvalidOperationException(
                    "The API returned an unsupported primary authentication provider.")
            };
        }

        // Keep configured Keycloak validation/refresh metadata registered even when Local
        // becomes the primary provider. Provider discovery gates new login challenges.
        if (!string.IsNullOrEmpty(config.KeycloakAuthority)
            && !string.IsNullOrEmpty(config.KeycloakClientId))
        {
            if (!_registeredSchemes.Contains(AuthSchemeNames.Keycloak))
            {
                RegisterKeycloakScheme(
                    config.KeycloakAuthority,
                    config.KeycloakClientId,
                    config.KeycloakClientSecret,
                    metadataAddress: null);
            }
            else
            {
                // Preserve the existing secret from env vars when DB config has no secret
                // (e.g., at startup when includeSecrets: false)
                var effectiveSecret = !string.IsNullOrEmpty(config.KeycloakClientSecret)
                    ? config.KeycloakClientSecret
                    : GetCurrentKeycloakSecret();

                UpdateKeycloakSchemeOptions(
                    config.KeycloakAuthority,
                    config.KeycloakClientId,
                    effectiveSecret);
            }
        }
        // Google: register if enabled and has credentials
        if (config.GoogleSsoEnabled == true && !string.IsNullOrEmpty(config.GoogleClientId))
        {
            if (!_registeredSchemes.Contains(AuthSchemeNames.Google))
            {
                RegisterGoogleScheme(config.GoogleClientId, config.GoogleClientSecret);
            }
            else
            {
                UpdateGoogleSchemeOptions(config.GoogleClientId, config.GoogleClientSecret);
            }
        }
        else if (config.GoogleSsoEnabled != true && _registeredSchemes.Contains(AuthSchemeNames.Google))
        {
            // Only remove if NOT configured via env vars (env vars take priority)
            var envGoogleClientId = _configuration["Google:ClientId"];
            if (string.IsNullOrEmpty(envGoogleClientId))
            {
                RemoveScheme(AuthSchemeNames.Google);
            }
            else
            {
                _logger.LogWarning(
                    "Google is disabled in DB settings but configured via environment variables — keeping scheme registered");
            }
        }

        // ATProto: register handler if enabled (no OIDC — custom handler)
        if (config.AtprotoLoginEnabled == true
            || string.Equals(
                _activePrimaryProvider,
                "atproto",
                StringComparison.Ordinal))
        {
            if (!_registeredSchemes.Contains(AuthSchemeNames.Atproto))
            {
                RegisterAtprotoScheme(config.AtprotoPublicUrl ?? string.Empty);
            }
        }
        else if (config.AtprotoLoginEnabled != true && _registeredSchemes.Contains(AuthSchemeNames.Atproto))
        {
            RemoveScheme(AuthSchemeNames.Atproto);
        }
    }

    private static string? ResolveDeploymentPrimaryProvider(string? configured)
    {
        string? provider = configured?.Trim().ToLowerInvariant();
        return provider switch
        {
            null or "" => null,
            "local" or "keycloak" or "atproto" => provider,
            _ => throw new InvalidOperationException(
                "Authentication:Provider must be blank, 'local', 'keycloak', or 'atproto'.")
        };
    }

    private void RegisterKeycloakScheme(
        string authority,
        string clientId,
        string? clientSecret,
        string? metadataAddress)
    {
        var options = CreateKeycloakOptions(authority, clientId, clientSecret, metadataAddress);
        new OpenIdConnectPostConfigureOptions(_dataProtection)
            .PostConfigure(AuthSchemeNames.Keycloak, options);
        _oidcOptionsCache.TryAdd(AuthSchemeNames.Keycloak, options);

        var scheme = new AuthenticationScheme(
            AuthSchemeNames.Keycloak,
            displayName: "Keycloak",
            typeof(OpenIdConnectHandler));

        _schemeProvider.TryAddScheme(scheme);
        lock (_registeredSchemesSync)
        {
            _registeredSchemes.Add(AuthSchemeNames.Keycloak);
        }

        if (!string.IsNullOrEmpty(clientSecret?.Trim()))
            _currentKeycloakSecret = clientSecret;

        _logger.LogInformation(
            "[OIDC-DIAG] Registered Keycloak OIDC scheme (authority={Authority}, clientId={ClientId}, " +
            "hasClientSecret={HasClientSecret})",
            authority, clientId, !string.IsNullOrWhiteSpace(clientSecret));
    }

    private void UpdateKeycloakSchemeOptions(string authority, string clientId, string? clientSecret)
    {
        _oidcOptionsCache.TryRemove(AuthSchemeNames.Keycloak);
        var options = CreateKeycloakOptions(authority, clientId, clientSecret, metadataAddress: null);
        new OpenIdConnectPostConfigureOptions(_dataProtection)
            .PostConfigure(AuthSchemeNames.Keycloak, options);
        _oidcOptionsCache.TryAdd(AuthSchemeNames.Keycloak, options);

        if (!string.IsNullOrEmpty(clientSecret?.Trim()))
            _currentKeycloakSecret = clientSecret;

        _logger.LogInformation(
            "Updated Keycloak OIDC scheme options (authority: {Authority}, hasClientSecret: {HasClientSecret})",
            authority, !string.IsNullOrWhiteSpace(clientSecret));
    }

    private string? GetCurrentKeycloakSecret()
    {
        return _currentKeycloakSecret ?? _configuration["Keycloak:ClientSecret"];
    }

    private OpenIdConnectOptions CreateKeycloakOptions(
        string authority,
        string clientId,
        string? clientSecret,
        string? metadataAddress)
    {
        return _oidcOptionsFactory.CreateKeycloakOptions(new EventBffOidcProviderOptions(
            authority,
            clientId,
            clientSecret,
            metadataAddress,
            _configuration.GetValue<bool?>("Keycloak:RequireHttpsMetadata")));
    }

    private void RegisterGoogleScheme(string clientId, string? clientSecret)
    {
        var options = CreateGoogleOptions(clientId, clientSecret);
        new OpenIdConnectPostConfigureOptions(_dataProtection)
            .PostConfigure(AuthSchemeNames.Google, options);
        _oidcOptionsCache.TryAdd(AuthSchemeNames.Google, options);

        var scheme = new AuthenticationScheme(
            AuthSchemeNames.Google,
            displayName: "Google",
            typeof(OpenIdConnectHandler));

        _schemeProvider.TryAddScheme(scheme);
        lock (_registeredSchemesSync)
        {
            _registeredSchemes.Add(AuthSchemeNames.Google);
        }

        _logger.LogInformation("Registered Google OIDC scheme");
    }

    private void UpdateGoogleSchemeOptions(string clientId, string? clientSecret)
    {
        _oidcOptionsCache.TryRemove(AuthSchemeNames.Google);
        var options = CreateGoogleOptions(clientId, clientSecret);
        new OpenIdConnectPostConfigureOptions(_dataProtection)
            .PostConfigure(AuthSchemeNames.Google, options);
        _oidcOptionsCache.TryAdd(AuthSchemeNames.Google, options);

        _logger.LogInformation("Updated Google OIDC scheme options");
    }

    private OpenIdConnectOptions CreateGoogleOptions(string clientId, string? clientSecret)
    {
        return _oidcOptionsFactory.CreateGoogleOptions(new EventBffOidcProviderOptions(
            "https://accounts.google.com",
            clientId,
            clientSecret));
    }

    private void RegisterAtprotoScheme(string publicUrl)
    {
        var scheme = new AuthenticationScheme(
            AuthSchemeNames.Atproto,
            displayName: "AT Protocol",
            typeof(Authentication.AtprotoAuthenticationHandler));

        _schemeProvider.TryAddScheme(scheme);
        lock (_registeredSchemesSync)
        {
            _registeredSchemes.Add(AuthSchemeNames.Atproto);
        }

        _logger.LogInformation("Registered ATProto authentication scheme");
    }

    private void RemoveScheme(string schemeName)
    {
        _schemeProvider.RemoveScheme(schemeName);

        // Clean up cached options for OIDC-based schemes
        if (schemeName is AuthSchemeNames.Keycloak or AuthSchemeNames.Google)
        {
            _oidcOptionsCache.TryRemove(schemeName);
        }

        lock (_registeredSchemesSync)
        {
            _registeredSchemes.Remove(schemeName);
        }
        _logger.LogInformation("Removed auth scheme: {SchemeName}", schemeName);
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}
