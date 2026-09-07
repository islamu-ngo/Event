namespace Explore.Blazor.Services;

/// <summary>
/// Manages dynamic authentication scheme registration at runtime.
/// Reads auth provider configuration from the API and registers/unregisters
/// the corresponding ASP.NET Core authentication schemes.
/// </summary>
public interface IDynamicAuthSchemeManager
{
    /// <summary>
    /// Initializes auth schemes from API configuration and environment variables.
    /// Called once at application startup after services are built.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Re-reads auth provider configuration from the API and updates registered schemes.
    /// Called after auth config is saved during setup or from admin settings.
    /// </summary>
    /// <param name="setupSecret">
    /// Optional setup secret to include in the API request. When provided, the internal
    /// endpoint is called which returns credentials (client secrets) needed for OIDC scheme registration.
    /// </param>
    Task RefreshSchemesAsync(string? setupSecret = null);

    /// <summary>
    /// Returns the scheme names of all currently registered dynamic auth providers.
    /// Does not include the Cookie scheme (always registered).
    /// </summary>
    Task<IReadOnlyList<string>> GetRegisteredProviderSchemesAsync();

    /// <summary>
    /// Returns the active primary provider boundary code: local, keycloak, or atproto.
    /// </summary>
    string GetActivePrimaryProvider();
}
