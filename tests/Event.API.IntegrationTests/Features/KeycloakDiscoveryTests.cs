using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// Validates that the containerized Keycloak serves correct OIDC metadata,
/// JWKS keys, and realm configuration. Catches misconfigured realm exports
/// that would cause token validation failures in the API.
/// </summary>
[Category(TestCategories.Security)]
[NotInParallel("SecurityInfra")]
[ClassDataSource<SecurityInfrastructureFixture>(Shared = SharedType.PerAssembly)]
public class KeycloakDiscoveryTests : IDisposable
{
    private readonly SecurityInfrastructureFixture _infra;
    private readonly HttpClient _httpClient;

    public KeycloakDiscoveryTests(SecurityInfrastructureFixture infra)
    {
        _infra = infra;
        _httpClient = new HttpClient();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    #region OIDC Metadata Discovery

    [Test]
    public async Task OidcMetadataEndpoint_ShouldReturnValidDocument()
    {
        var response = await _httpClient.GetAsync(_infra.KeycloakMetadataAddress);

        await Assert.That(response.IsSuccessStatusCode).IsTrue().Because("the OIDC metadata endpoint must be reachable");

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        await Assert.That(doc.RootElement.TryGetProperty("issuer", out var issuer)).IsTrue();
        await Assert.That(issuer.GetString()).IsEqualTo(_infra.KeycloakAuthority).Because("the issuer in OIDC metadata must match the Keycloak realm URL");

        await Assert.That(doc.RootElement.TryGetProperty("token_endpoint", out _)).IsTrue();
        await Assert.That(doc.RootElement.TryGetProperty("jwks_uri", out _)).IsTrue();
        await Assert.That(doc.RootElement.TryGetProperty("authorization_endpoint", out _)).IsTrue();
        await Assert.That(doc.RootElement.TryGetProperty("response_types_supported", out _)).IsTrue();
        await Assert.That(doc.RootElement.TryGetProperty("subject_types_supported", out _)).IsTrue();
        await Assert.That(doc.RootElement.TryGetProperty("id_token_signing_alg_values_supported", out _)).IsTrue();
    }

    [Test]
    public async Task OidcMetadata_ShouldAdvertiseCodeFlow()
    {
        var metadata = await GetOidcMetadataAsync();

        await Assert.That(metadata.TryGetProperty("response_types_supported", out var responseTypes)).IsTrue();
        var types = responseTypes.EnumerateArray().Select(t => t.GetString()).ToList();

        await Assert.That(types).Contains("code").Because("the OIDC provider must support authorization code flow for the BFF pattern");
    }

    [Test]
    public async Task OidcMetadata_ShouldAdvertiseROPCGrant()
    {
        var metadata = await GetOidcMetadataAsync();

        await Assert.That(metadata.TryGetProperty("grant_types_supported", out var grantTypes)).IsTrue();
        var types = grantTypes.EnumerateArray().Select(t => t.GetString()).ToList();

        await Assert.That(types).Contains("password").Because("the OIDC provider must support ROPC grant for test token acquisition");
    }

    [Test]
    public async Task OidcMetadata_ShouldAdvertiseRS256Signing()
    {
        var metadata = await GetOidcMetadataAsync();

        await Assert.That(metadata.TryGetProperty("id_token_signing_alg_values_supported", out var algs)).IsTrue();
        var algValues = algs.EnumerateArray().Select(a => a.GetString()).ToList();

        await Assert.That(algValues).Contains("RS256").Because("the OIDC provider must support RS256 for JWT signing (production requirement)");
    }

    #endregion

    #region JWKS Endpoint

    [Test]
    public async Task JwksEndpoint_ShouldReturnValidKeys()
    {
        var metadata = await GetOidcMetadataAsync();
        var jwksUri = metadata.GetProperty("jwks_uri").GetString();

        await Assert.That(string.IsNullOrEmpty(jwksUri)).IsFalse();

        var response = await _httpClient.GetAsync(jwksUri!);
        await Assert.That(response.IsSuccessStatusCode).IsTrue();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        await Assert.That(doc.RootElement.TryGetProperty("keys", out var keys)).IsTrue();
        var keyList = keys.EnumerateArray().ToList();
        await Assert.That(keyList).IsNotEmpty().Because("at least one signing key must be available");

        foreach (var key in keyList)
        {
            await Assert.That(key.TryGetProperty("kty", out _)).IsTrue().Because("each key must have a key type");
            await Assert.That(key.TryGetProperty("use", out var use)).IsTrue();
            await Assert.That(new[] { "sig", "enc" }).Contains(use.GetString())
                .Because("Keycloak may publish both signing and encryption keys");
            await Assert.That(key.TryGetProperty("kid", out _)).IsTrue().Because("each key must have a key ID");
        }

        var hasRsaSigningKey = keyList.Any(key =>
            key.TryGetProperty("use", out var use) &&
            use.GetString() == "sig" &&
            key.TryGetProperty("kty", out var keyType) &&
            keyType.GetString() == "RSA");

        await Assert.That(hasRsaSigningKey).IsTrue().Because("at least one RSA signing key must be available for RS256 JWT validation");
    }

    [Test]
    public async Task JwksKeys_ShouldIncludeRSAKey()
    {
        var metadata = await GetOidcMetadataAsync();
        var jwksUri = metadata.GetProperty("jwks_uri").GetString();

        var response = await _httpClient.GetAsync(jwksUri!);
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var keys = doc.RootElement.GetProperty("keys");
        var hasRsa = keys.EnumerateArray()
            .Any(k => k.TryGetProperty("kty", out var kty) && kty.GetString() == "RSA");

        await Assert.That(hasRsa).IsTrue().Because("at least one RSA key must be present for RS256 JWT signing");
    }

    #endregion

    #region Token Endpoint Validation

    [Test]
    public async Task TokenEndpoint_ShouldBeReachable()
    {
        var metadata = await GetOidcMetadataAsync();
        var tokenEndpoint = metadata.GetProperty("token_endpoint").GetString();

        await Assert.That(string.IsNullOrEmpty(tokenEndpoint)).IsFalse();

        var probeResponse = await _httpClient.PostAsync(tokenEndpoint!,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "invalid-client",
                ["client_secret"] = "invalid-secret"
            }));

        await Assert.That(probeResponse.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Unauthorized).Because("the token endpoint must be reachable and reject invalid credentials with 401");
    }

    [Test]
    public async Task TokenEndpoint_ShouldRejectInvalidCredentials()
    {
        var act = async () => await _infra.TokenClient.GetAccessTokenAsync("nonexistent-user", "wrong-password");

        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    #endregion

    #region Realm Configuration

    [Test]
    public async Task RealmWellKnown_ShouldReturnOpenIdConfiguration()
    {
        var response = await _httpClient.GetAsync(_infra.KeycloakMetadataAddress);

        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.OK);

        var contentType = response.Content.Headers.ContentType?.MediaType;
        await Assert.That(contentType).IsEqualTo("application/json").Because("OIDC metadata must be served as JSON");
    }

    [Test]
    public async Task RealmEndpoint_ShouldReturnRealmInfo()
    {
        var response = await _httpClient.GetAsync(_infra.KeycloakMetadataAddress);

        await Assert.That(response.IsSuccessStatusCode).IsTrue();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        await Assert.That(doc.RootElement.GetProperty("issuer").GetString()).IsEqualTo(_infra.KeycloakAuthority);
        await Assert.That(doc.RootElement.GetProperty("authorization_endpoint").GetString()).Contains("/realms/ISLAMU/").Because("the imported ISLAMU realm must serve OIDC endpoints");
    }

    #endregion

    #region Token Issuer Consistency

    [Test]
    public async Task TokenIssuer_ShouldMatchMetadataIssuer()
    {
        var token = await _infra.TokenClient.GetAdminTokenAsync();
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        var metadata = await GetOidcMetadataAsync();
        var metadataIssuer = metadata.GetProperty("issuer").GetString();

        await Assert.That(jwt.Issuer).IsEqualTo(metadataIssuer).Because("the token issuer must exactly match the issuer in OIDC metadata");
    }

    #endregion

    #region Helpers

    private async Task<JsonElement> GetOidcMetadataAsync()
    {
        var response = await _httpClient.GetAsync(_infra.KeycloakMetadataAddress);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement;
    }

    #endregion
}
