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

    public KeycloakTokenClient(string keycloakBaseUrl, string realm, string clientId, string clientSecret)
    {
        _httpClient = new HttpClient();
        _tokenEndpoint = $"{keycloakBaseUrl}/realms/{realm}/protocol/openid-connect/token";
        _clientId = clientId;
        _clientSecret = clientSecret;
    }

    /// <summary>
    /// Acquires an access token for the specified test user via ROPC grant.
    /// </summary>
    /// <param name="username">Keycloak username (from test realm export).</param>
    /// <param name="password">Keycloak password (from test realm export).</param>
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
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Failed to acquire Keycloak token for user '{username}'. " +
                $"Status: {response.StatusCode}. Body: {errorBody}");
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);

        if (string.IsNullOrEmpty(tokenResponse?.AccessToken))
        {
            throw new InvalidOperationException(
                $"Keycloak token response for user '{username}' contained an empty access_token.");
        }

        return tokenResponse.AccessToken;
    }

    /// <summary>
    /// Acquires the default test admin token.
    /// </summary>
    public Task<string> GetAdminTokenAsync(CancellationToken cancellationToken = default)
        => GetAccessTokenAsync("test-admin", "test-admin-password", cancellationToken: cancellationToken);

    /// <summary>
    /// Acquires the default test regular user token.
    /// </summary>
    public Task<string> GetUserTokenAsync(CancellationToken cancellationToken = default)
        => GetAccessTokenAsync("test-user", "test-user-password", cancellationToken: cancellationToken);

    public Task<string> GetUserTokenWithOfflineAccessAsync(CancellationToken cancellationToken = default)
        => GetAccessTokenAsync("test-user", "test-user-password", "openid profile email offline_access", cancellationToken);

    /// <summary>
    /// Acquires the default test tenant admin token.
    /// </summary>
    public Task<string> GetTenantAdminTokenAsync(CancellationToken cancellationToken = default)
        => GetAccessTokenAsync("test-tenant-admin", "test-tenant-admin-password", cancellationToken: cancellationToken);

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
