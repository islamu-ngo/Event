// ABOUTME: HTTP client for acquiring real JWT tokens from a containerized Keycloak instance.
// ABOUTME: Uses Resource Owner Password Credentials (ROPC) grant for programmatic test token acquisition.

using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// Acquires real JWT access tokens from a Keycloak container using the
/// Resource Owner Password Credentials (ROPC) grant. Only used in security
/// integration tests — never in production code.
/// </summary>
public sealed class KeycloakTokenClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _tokenEndpoint;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly IReadOnlyDictionary<string, string> _userPasswords;

    public KeycloakTokenClient(
        string keycloakBaseUrl,
        string realm,
        string clientId,
        string clientSecret,
        IReadOnlyDictionary<string, string> userPasswords)
    {
        _httpClient = new HttpClient();
        _tokenEndpoint = $"{keycloakBaseUrl}/realms/{realm}/protocol/openid-connect/token";
        _clientId = clientId;
        _clientSecret = clientSecret;
        _userPasswords = userPasswords;
    }

    /// <summary>
    /// Acquires an access token for the specified test user via ROPC grant.
    /// </summary>
    /// <param name="username">Keycloak username (from test realm export).</param>
    /// <param name="password">Password from the disposable fixture credential authority.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A valid JWT access token string.</returns>
    /// <exception cref="InvalidOperationException">If token acquisition fails.</exception>
    public async Task<string> GetAccessTokenAsync(
        string username,
        string password,
        string scope = "openid profile email",
        CancellationToken cancellationToken = default)
    {
        var requestBody = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["username"] = username,
            ["password"] = password,
            ["scope"] = scope
        };

        using var response = await _httpClient.PostAsync(
            _tokenEndpoint,
            new FormUrlEncodedContent(requestBody),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Failed to acquire Keycloak token. Status: {response.StatusCode}.");
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);

        if (string.IsNullOrEmpty(tokenResponse?.AccessToken))
        {
            throw new InvalidOperationException(
                "Keycloak token response contained an empty access_token.");
        }

        return tokenResponse.AccessToken;
    }

    /// <summary>
    /// Acquires the default test admin token.
    /// </summary>
    public Task<string> GetAdminTokenAsync(CancellationToken cancellationToken = default)
        => GetAccessTokenAsync(username: "test-admin", password: _userPasswords["test-admin"], cancellationToken: cancellationToken);

    /// <summary>
    /// Acquires the default test regular user token.
    /// </summary>
    public Task<string> GetUserTokenAsync(CancellationToken cancellationToken = default)
        => GetAccessTokenAsync(username: "test-user", password: _userPasswords["test-user"], cancellationToken: cancellationToken);

    public Task<string> GetUserTokenWithOfflineAccessAsync(CancellationToken cancellationToken = default)
        => GetAccessTokenAsync(username: "test-user", password: _userPasswords["test-user"], scope: "openid profile email offline_access", cancellationToken: cancellationToken);

    /// <summary>
    /// Acquires the default test tenant admin token.
    /// </summary>
    public Task<string> GetTenantAdminTokenAsync(CancellationToken cancellationToken = default)
        => GetAccessTokenAsync(username: "test-tenant-admin", password: _userPasswords["test-tenant-admin"], cancellationToken: cancellationToken);

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }
    }
}
