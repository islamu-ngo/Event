using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using Event.Api.IntegrationTests.Fixtures;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// Validates that tokens issued by the containerized Keycloak contain the
/// correct claims, audiences, and structure expected by the API's JwtBearer
/// validation pipeline. This catches misconfigured realm exports early.
/// </summary>
[Category(TestCategories.Security)]
[ClassDataSource<SecurityInfrastructureFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("SecurityInfra")]
public class TokenClaimsVerificationTests
{
    private readonly SecurityInfrastructureFixture _infra;

    public TokenClaimsVerificationTests(SecurityInfrastructureFixture infra)
    {
        _infra = infra;
    }

    #region Standard OIDC Claims

    [Test]
    public async Task AdminToken_ShouldContainExpectedIssuer()
    {
        var token = await _infra.TokenClient.GetAdminTokenAsync();
        var jwt = DecodeToken(token);

        await Assert.That(jwt.Issuer).IsEqualTo(_infra.KeycloakAuthority).Because("the token issuer must match the Keycloak container's realm URL");
    }

    [Test]
    public async Task UserToken_ShouldContainCorrectAudience()
    {
        var token = await _infra.TokenClient.GetUserTokenAsync();
        var jwt = DecodeToken(token);

        await Assert.That(jwt.Audiences).Contains("islamu-event-api").Because("the token must contain the islamu-event-api audience mapped in the realm export");
    }

    [Test]
    public async Task AdminToken_ShouldContainSubjectClaim()
    {
        var token = await _infra.TokenClient.GetAdminTokenAsync();
        var jwt = DecodeToken(token);

        await Assert.That(string.IsNullOrEmpty(GetSubject(jwt))).IsFalse()
            .Because("every token must have a subject (sub) claim");
    }

    [Test]
    public async Task AdminToken_ShouldContainPreferredUsername()
    {
        var token = await _infra.TokenClient.GetAdminTokenAsync();
        var claims = DecodeToken(token).Claims;

        await Assert.That(claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value).IsEqualTo("test-admin").Because("the preferred_username claim must match the Keycloak user");
    }

    [Test]
    public async Task UserToken_ShouldContainEmailClaim()
    {
        var token = await _infra.TokenClient.GetUserTokenAsync();
        var claims = DecodeToken(token).Claims;

        await Assert.That(claims.FirstOrDefault(c => c.Type == "email")?.Value).IsEqualTo("user@test.islamu.org").Because("the email claim must match the test realm user configuration");
    }

    #endregion

    #region Token Validity Window

    [Test]
    public async Task AdminToken_ShouldHaveValidTimeBounds()
    {
        var token = await _infra.TokenClient.GetAdminTokenAsync();
        var jwt = DecodeToken(token);

        await Assert.That(jwt.ValidFrom).IsBefore(DateTime.UtcNow).Because("iat (issued-at) must be in the past");
        await Assert.That(jwt.ValidTo).IsAfter(DateTime.UtcNow).Because("exp (expiry) must be in the future — realm sets accessTokenLifespan=3600s");
    }

    [Test]
    public async Task TenantAdminToken_ShouldHaveLongExpiry()
    {
        var token = await _infra.TokenClient.GetTenantAdminTokenAsync();
        var jwt = DecodeToken(token);

        var remaining = jwt.ValidTo - DateTime.UtcNow;
        await Assert.That(remaining).IsGreaterThan(TimeSpan.FromMinutes(50)).Because("access token lifespan is 3600s in the test realm — at least 50 minutes must remain");
    }

    #endregion

    #region Token Uniqueness

    [Test]
    public async Task ConsecutiveTokens_ShouldHaveDifferentJti()
    {
        var token1 = await _infra.TokenClient.GetAdminTokenAsync();
        var token2 = await _infra.TokenClient.GetAdminTokenAsync();

        var jwt1 = DecodeToken(token1);
        var jwt2 = DecodeToken(token2);

        var jti1 = jwt1.Claims.FirstOrDefault(c => c.Type == "jti")?.Value;
        var jti2 = jwt2.Claims.FirstOrDefault(c => c.Type == "jti")?.Value;

        await Assert.That(jti1).IsNotEqualTo(jti2).Because("each token must have a unique jti (JWT ID)");
    }

    #endregion

    #region Per-User Claim Differentiation

    [Test]
    public async Task DifferentUsers_ShouldHaveDifferentSubjects()
    {
        var adminToken = await _infra.TokenClient.GetAdminTokenAsync();
        var userToken = await _infra.TokenClient.GetUserTokenAsync();

        var adminJwt = DecodeToken(adminToken);
        var userJwt = DecodeToken(userToken);

        await Assert.That(GetSubject(adminJwt)).IsNotEqualTo(GetSubject(userJwt)).Because("different users must have different sub claims");
    }

    [Test]
    public async Task DifferentUsers_ShouldHaveDifferentUsernames()
    {
        var adminToken = await _infra.TokenClient.GetAdminTokenAsync();
        var tenantAdminToken = await _infra.TokenClient.GetTenantAdminTokenAsync();

        var adminUsername = DecodeToken(adminToken).Claims
            .First(c => c.Type == "preferred_username").Value;
        var tenantAdminUsername = DecodeToken(tenantAdminToken).Claims
            .First(c => c.Type == "preferred_username").Value;

        await Assert.That(adminUsername).IsEqualTo("test-admin");
        await Assert.That(tenantAdminUsername).IsEqualTo("test-tenant-admin");
    }

    #endregion

    #region Token Is Usable By API

    [Test]
    public async Task UserToken_ShouldBeAcceptedBySecurityWebApplicationFactory()
    {
        using var factory = new SecurityWebApplicationFactory(
            _infra.KeycloakAuthority,
            _infra.KeycloakMetadataAddress,
            _infra.CerbosGrpcEndpoint)
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider()
        };

        using var client = factory.CreateClient();
        var token = await _infra.TokenClient.GetUserTokenAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/event");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.OK).Because("a token with valid claims must be accepted by the real JwtBearer middleware");
    }

    #endregion

    #region Helper

    private static JwtSecurityToken DecodeToken(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        return handler.ReadJwtToken(token);
    }

    private static string? GetSubject(JwtSecurityToken jwt)
    {
        if (!string.IsNullOrWhiteSpace(jwt.Subject))
        {
            return jwt.Subject;
        }

        if (jwt.Payload.TryGetValue(JwtRegisteredClaimNames.Sub, out var payloadSubject))
        {
            return payloadSubject?.ToString();
        }

        return jwt.Claims.FirstOrDefault(IsSubjectClaim)?.Value;
    }

    private static bool IsSubjectClaim(Claim claim)
    {
        return claim.Type is JwtRegisteredClaimNames.Sub or ClaimTypes.NameIdentifier or "nameid" or "sid"
            || claim.Type.EndsWith("/nameidentifier", StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
