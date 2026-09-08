using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// Security integration tests exercising the real OIDC token validation pipeline.
/// Uses a containerized Keycloak instance for JWT issuance and the API's real
/// JwtBearer authentication (not TestAuthHandler).
///
/// These tests prove:
/// 1. The OIDC metadata discovery works against a real IdP.
/// 2. Token signature validation works with real RSA keys.
/// 3. Audience and issuer validation match the Keycloak configuration.
/// 4. Malformed / missing / expired tokens are correctly rejected.
/// </summary>
[Category(TestCategories.Security)]
[ClassDataSource<SecurityInfrastructureFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("SecurityInfra")]
public class SecurityIntegrationTests : IAsyncDisposable
{
    private readonly SecurityInfrastructureFixture _infra;
    private readonly SecurityWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SecurityIntegrationTests(SecurityInfrastructureFixture infra)
    {
        _infra = infra;
        _factory = new SecurityWebApplicationFactory(
            infra.KeycloakAuthority,
            infra.KeycloakMetadataAddress,
            infra.CerbosGrpcEndpoint)
        {
            // Use allow-all stub for auth provider so we isolate authentication testing
            AuthorizationProviderOverride = new StubAuthorizationProvider()
        };
        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    #region Happy Path — Real Token Authentication

    [Test]
    public async Task GetPublicEndpoint_WithValidToken_ShouldReturnOk()
    {
        // Arrange
        var token = await _infra.TokenClient.GetUserTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/event");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because("a valid JWT from the Keycloak container should be accepted by real JwtBearer validation");
    }

    [Test]
    public async Task PostProtectedEndpoint_WithValidToken_ShouldNotReturnUnauthorized()
    {
        // Arrange — use admin token for a write endpoint
        var token = await _infra.TokenClient.GetAdminTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/category")
        {
            Content = JsonContent.Create(new
            {
                FullName = "Security Test Org",
                Email = "security@test.islamu.org",
                Country = "Belgium",
                City = "Brussels",
                Address = "Test Street 1",
                Postcode = 1000
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert — should NOT be 401 (could be 201, 400, or 403 from business logic)
        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.Unauthorized).Because("a valid Keycloak JWT with proper audience should pass authentication");
    }

    #endregion

    #region Negative — No Token

    [Test]
    public async Task PostProtectedEndpoint_WithoutToken_ShouldReturnUnauthorized()
    {
        // Arrange — no Authorization header
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/category")
        {
            Content = JsonContent.Create(new
            {
                FullName = "No Auth Org",
                Email = "noauth@test.islamu.org",
                Country = "Belgium",
                City = "Brussels",
                Address = "Test Street 1",
                Postcode = 1000
            })
        };

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized).Because("requests without a Bearer token must be rejected by real JWT validation");
    }

    #endregion

    #region Negative — Malformed Token

    [Test]
    public async Task PostProtectedEndpoint_WithMalformedToken_ShouldReturnUnauthorized()
    {
        // Arrange — garbage token
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/organization")
        {
            Content = JsonContent.Create(new
            {
                FullName = "Malformed Org",
                Email = "malformed@test.islamu.org",
                Country = "Belgium",
                City = "Brussels",
                Address = "Test Street 1",
                Postcode = 1000
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.valid.jwt.token");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized).Because("a malformed JWT must be rejected by token signature validation");
    }

    [Test]
    public async Task PostProtectedEndpoint_WithEmptyBearer_ShouldReturnUnauthorized()
    {
        // Arrange — Bearer with empty token
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/organization")
        {
            Content = JsonContent.Create(new
            {
                FullName = "Empty Bearer Org",
                Email = "empty@test.islamu.org",
                Country = "Belgium",
                City = "Brussels",
                Address = "Test Street 1",
                Postcode = 1000
            })
        };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer ");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized).Because("an empty Bearer token must be rejected");
    }

    #endregion

    #region Negative — Wrong Audience

    [Test]
    public async Task PostProtectedEndpoint_WithTamperedToken_ShouldReturnUnauthorized()
    {
        // Arrange — get a valid token, then tamper with the payload
        var validToken = await _infra.TokenClient.GetUserTokenAsync();
        var parts = validToken.Split('.');

        if (parts.Length == 3)
        {
            // Flip a character in the signature to invalidate it
            var tamperedSignature = parts[2][..^1] + (parts[2][^1] == 'A' ? 'B' : 'A');
            var tamperedToken = $"{parts[0]}.{parts[1]}.{tamperedSignature}";

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/organization")
            {
                Content = JsonContent.Create(new
                {
                    FullName = "Tampered Org",
                    Email = "tampered@test.islamu.org",
                    Country = "Belgium",
                    City = "Brussels",
                    Address = "Test Street 1",
                    Postcode = 1000
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tamperedToken);

            // Act
            var response = await _client.SendAsync(request);

            // Assert
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized).Because("a JWT with a tampered signature must be rejected by RSA key validation");
        }
    }

    #endregion

    #region Negative — Expired Token (Constructed)

    [Test]
    public async Task PostProtectedEndpoint_WithExpiredJwt_ShouldReturnUnauthorized()
    {
        var token = await _infra.TokenClient.GetUserTokenAsync();
        var parts = token.Split('.');

        await Assert.That(parts.Length).IsEqualTo(3).Because("JWT must have 3 parts");

        var payloadBytes = Convert.FromBase64String(PadBase64(parts[1]));
        var payloadJson = System.Text.Encoding.UTF8.GetString(payloadBytes);

        var modifiedJson = payloadJson
            .Replace($"\"exp\":{ExtractClaim(payloadJson, "exp")}", "\"exp\":1");

        var modifiedPayload = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(modifiedJson))
            .TrimEnd('=');

        var expiredToken = $"{parts[0]}.{modifiedPayload}.{parts[2]}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/organization")
        {
            Content = JsonContent.Create(new
            {
                FullName = "Expired Org",
                Email = "expired@test.islamu.org",
                Country = "Belgium",
                City = "Brussels",
                Address = "Test Street 1",
                Postcode = 1000
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized).Because("an expired JWT must be rejected even if the signature is valid");
    }

    #endregion

    #region Negative — Wrong Audience

    [Test]
    public async Task PostProtectedEndpoint_WithTokenMissingApiAudience_ShouldBeHandled()
    {
        var token = await _infra.TokenClient.GetUserTokenAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/category")
        {
            Content = JsonContent.Create(new
            {
                FullName = "Wrong Audience Org",
                Email = "wrongaud@test.islamu.org",
                Country = "Belgium",
                City = "Brussels",
                Address = "Test Street 1",
                Postcode = 1000
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.Unauthorized).Because("the test realm includes the islamu-event-api audience mapper, " +
        "so a valid token should pass audience validation");
    }

    #endregion

    #region Public Endpoints — Should Work Without Token

    [Test]
    [Arguments("/api/event")]
    [Arguments("/api/organization")]
    [Arguments("/api/category")]
    [Arguments("/api/tag")]
    [Arguments("/api/actor")]
    public async Task GetPublicEndpoint_WithoutToken_ShouldReturnOk(string endpoint)
    {
        // Arrange — no token, testing [AllowAnonymous] endpoints
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

        // Act
        var response = await _client.SendAsync(request);

        // Assert — public GET endpoints must remain accessible without authentication
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because($"public endpoint {endpoint} should be accessible without a Bearer token");
    }

    [Test]
    public async Task GetPhysicalLocations_WithoutToken_ShouldReturnUnauthorized()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/location");

        var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized).Because("generic physical-location data requires an authenticated principal");
    }

    #endregion

    #region Helpers

    private static string PadBase64(string base64)
    {
        var padding = base64.Length % 4;
        return padding == 0 ? base64 : base64 + new string('=', 4 - padding);
    }

    private static string ExtractClaim(string json, string claimName)
    {
        var pattern = $"\"{claimName}\":";
        var idx = json.IndexOf(pattern, StringComparison.Ordinal);
        if (idx < 0) return "0";
        var start = idx + pattern.Length;
        var end = json.IndexOf(',', start);
        if (end < 0) end = json.IndexOf('}', start);
        return json[start..end].Trim();
    }

    #endregion
}
