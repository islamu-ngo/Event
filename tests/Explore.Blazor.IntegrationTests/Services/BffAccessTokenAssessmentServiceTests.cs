using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Explore.Blazor.Services.Auth;

namespace Explore.Blazor.IntegrationTests.Services;

public sealed class BffAccessTokenAssessmentServiceTests
{
    [Test]
    public async Task Assess_WithMissingToken_ReturnsMissingReason()
    {
        var service = new BffAccessTokenAssessmentService();

        var result = service.Assess(null);

        await Assert.That(result.IsUsable).IsFalse();
        await Assert.That(result.Reason).IsEqualTo("missing_access_token");
    }

    [Test]
    public async Task Assess_WithValidToken_ReturnsValidUntilReason()
    {
        var service = new BffAccessTokenAssessmentService();
        var token = CreateJwt("user-1", DateTime.UtcNow.AddMinutes(30));

        var result = service.Assess(token);

        await Assert.That(result.IsUsable).IsTrue();
        await Assert.That(result.Reason).IsEqualTo("valid_access_token");
    }

    [Test]
    public async Task Assess_WithExpiredToken_ReturnsExpiredReason()
    {
        var service = new BffAccessTokenAssessmentService();
        var token = CreateJwt("user-1", DateTime.UtcNow.AddMinutes(-5));

        var result = service.Assess(token);

        await Assert.That(result.IsUsable).IsFalse();
        await Assert.That(result.Reason).IsEqualTo("expired_access_token");
    }

    [Test]
    public async Task ResolveUserId_RejectsConflictingProviderSubjectSpellings()
    {
        var service = new BffAccessTokenAssessmentService();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "name-id"),
            new Claim("sid", "session-id"),
            new Claim("sub", "subject-id")
        ], "Cookies"));

        var userId = service.ResolveUserId(principal);

        await Assert.That(userId).IsNull();
    }

    private static string CreateJwt(
        string sub,
        DateTime expires,
        string? issuer = null,
        string? audience = null)
    {
        var jwt = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: [new Claim("sub", sub)],
            expires: expires);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
