using Event.Web.BffHosting.Authentication;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Constants;
using Explore.Blazor.Configuration;
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
    private readonly AuthenticationCredentialAcquisitionReceipts? _acquisitionReceipts;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly object _registeredSchemesSync = new();

    /// <summary>
    /// Tracks which dynamic provider schemes are currently registered.
    /// </summary>
    private readonly HashSet<string> _registeredSchemes = [];

    private readonly string? _deploymentPrimaryProvider;
    private string _activePrimaryProvider;

    public DynamicAuthSchemeManager(
        IAuthenticationSchemeProvider schemeProvider,
        IOptionsMonitorCache<OpenIdConnectOptions> oidcOptionsCache,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IDataProtectionProvider dataProtection,
        IEventBffOidcOptionsFactory oidcOptionsFactory,
        ILogger<DynamicAuthSchemeManager> logger,
        AuthenticationCredentialAcquisitionReceipts? acquisitionReceipts = null)
    {
        _schemeProvider = schemeProvider;
        _oidcOptionsCache = oidcOptionsCache;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _dataProtection = dataProtection;
        _oidcOptionsFactory = oidcOptionsFactory;
        _logger = logger;
        _acquisitionReceipts = acquisitionReceipts;
        _deploymentPrimaryProvider = ResolveDeploymentPrimaryProvider(
            configuration["Authentication:Provider"] ?? configuration["AUTHENTICATION_PROVIDER"]);
        _activePrimaryProvider = _deploymentPrimaryProvider ?? "local";
    }

    public async Task InitializeAsync()
    {
        await _lock.WaitAsync();
        try
        {
            _logger.LogInformation("Initializing dynamic auth schemes...");

            // Confidential schemes require credentials acquired by this process.
            var envAuthority = _configuration["Keycloak:Authority"];
            var envClientId = _configuration["Keycloak:ClientId"];
            var envClientSecret = _configuration["Keycloak:ClientSecret"];
            var envMetadataAddress = _configuration["Keycloak:MetadataAddress"];
            var envGoogleClientId = _configuration["Google:ClientId"];
            var envGoogleClientSecret = _configuration["Google:ClientSecret"];

            if (!string.IsNullOrEmpty(envAuthority)
                && !string.IsNullOrEmpty(envClientId))
            {
                if (_deploymentPrimaryProvider is null)
                {
                    _activePrimaryProvider = "keycloak";
                }

                if (!string.IsNullOrWhiteSpace(envClientSecret))
                {
                    _logger.LogInformation(
                        "Keycloak config detected in environment variables — registering Keycloak scheme from env " +
                        "(authority={Authority}, clientId={ClientId}, hasSecret={HasSecret})",
                        envAuthority, envClientId, true);

                    RegisterKeycloakScheme(envAuthority, envClientId, envClientSecret, envMetadataAddress);
                }
            }

            if (!string.IsNullOrEmpty(envGoogleClientId)
                && !string.IsNullOrWhiteSpace(envGoogleClientSecret)
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

            // API configuration carries provider metadata only. Credentials belong to this
            // process's selected deployment authority at cold start and refresh.
            AuthProviderConfigurationDto? dbConfig = null;
            try
            {
                dbConfig = await FetchConfigFromApiAsync();
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

    public async Task RefreshSchemesAsync()
    {
        await _lock.WaitAsync();
        try
        {
            _logger.LogInformation("Refreshing dynamic auth schemes from API...");

            var config = await FetchConfigFromApiAsync();

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

    private async Task<AuthProviderConfigurationDto?> FetchConfigFromApiAsync()
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var apiClient = scope.ServiceProvider.GetRequiredService<IInstanceOnboardingClient>();
        try
        {
            return (await apiClient.GetInstanceOnboardingAuthProviderConfigurationAsync()).ToDto();
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
            && !string.IsNullOrEmpty(config.KeycloakClientId)
            && !string.IsNullOrWhiteSpace(_configuration["Keycloak:ClientSecret"]))
        {
            if (!_registeredSchemes.Contains(AuthSchemeNames.Keycloak))
            {
                RegisterKeycloakScheme(
                    config.KeycloakAuthority,
                    config.KeycloakClientId,
                    _configuration["Keycloak:ClientSecret"],
                    metadataAddress: null);
            }
            else
            {
                UpdateKeycloakSchemeOptions(
                    config.KeycloakAuthority,
                    config.KeycloakClientId,
                    _configuration["Keycloak:ClientSecret"]);
            }
        }
        else if (_registeredSchemes.Contains(AuthSchemeNames.Keycloak))
        {
            RemoveScheme(AuthSchemeNames.Keycloak);
        }
        // Google: register if enabled and has credentials
        if (config.GoogleSsoEnabled == true && !string.IsNullOrEmpty(config.GoogleClientId)
            && !string.IsNullOrWhiteSpace(_configuration["Google:ClientSecret"]))
        {
            if (!_registeredSchemes.Contains(AuthSchemeNames.Google))
            {
                RegisterGoogleScheme(config.GoogleClientId, _configuration["Google:ClientSecret"]);
            }
            else
            {
                UpdateGoogleSchemeOptions(config.GoogleClientId, _configuration["Google:ClientSecret"]);
            }
        }
        else if (_registeredSchemes.Contains(AuthSchemeNames.Google))
        {
            // Only remove if NOT configured via env vars (env vars take priority)
            var envGoogleClientId = _configuration["Google:ClientId"];
            if (string.IsNullOrEmpty(envGoogleClientId)
                || string.IsNullOrWhiteSpace(_configuration["Google:ClientSecret"]))
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
        bool optionsAdded = _oidcOptionsCache.TryAdd(AuthSchemeNames.Keycloak, options);

        var scheme = new AuthenticationScheme(
            AuthSchemeNames.Keycloak,
            displayName: "Keycloak",
            typeof(OpenIdConnectHandler));

        bool schemeAdded = _schemeProvider.TryAddScheme(scheme);
        _acquisitionReceipts?.SetInstalled(AuthenticationCredentialAcquisitionReceipts.Consumer.KeycloakOidc,
            optionsAdded && schemeAdded);
        lock (_registeredSchemesSync)
        {
            _registeredSchemes.Add(AuthSchemeNames.Keycloak);
        }

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
        bool optionsAdded = _oidcOptionsCache.TryAdd(AuthSchemeNames.Keycloak, options);
        _acquisitionReceipts?.SetInstalled(AuthenticationCredentialAcquisitionReceipts.Consumer.KeycloakOidc,
            optionsAdded);

        _logger.LogInformation(
            "Updated Keycloak OIDC scheme options (authority: {Authority}, hasClientSecret: {HasClientSecret})",
            authority, !string.IsNullOrWhiteSpace(clientSecret));
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
        bool optionsAdded = _oidcOptionsCache.TryAdd(AuthSchemeNames.Google, options);

        var scheme = new AuthenticationScheme(
            AuthSchemeNames.Google,
            displayName: "Google",
            typeof(OpenIdConnectHandler));

        bool schemeAdded = _schemeProvider.TryAddScheme(scheme);
        _acquisitionReceipts?.SetInstalled(AuthenticationCredentialAcquisitionReceipts.Consumer.GoogleOidc,
            optionsAdded && schemeAdded);
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
        bool optionsAdded = _oidcOptionsCache.TryAdd(AuthSchemeNames.Google, options);
        _acquisitionReceipts?.SetInstalled(AuthenticationCredentialAcquisitionReceipts.Consumer.GoogleOidc,
            optionsAdded);

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
        if (schemeName == AuthSchemeNames.Keycloak)
            _acquisitionReceipts?.SetInstalled(AuthenticationCredentialAcquisitionReceipts.Consumer.KeycloakOidc, false);
        if (schemeName == AuthSchemeNames.Google)
            _acquisitionReceipts?.SetInstalled(AuthenticationCredentialAcquisitionReceipts.Consumer.GoogleOidc, false);
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
