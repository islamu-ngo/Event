using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class ExternalProviderSubjectHttpTests
{
    [Test]
    [Arguments("opaque", true)]
    [Arguments("opaque", false)]
    [Arguments("opaque", null)]
    [Arguments("nameidentifier-only", false)]
    [Arguments("missing-subject", false)]
    [Arguments("sid-only", false)]
    [Arguments("missing-issuer", false)]
    [Arguments("wrong-issuer", false)]
    [Arguments("forged-signature", false)]
    [Arguments("tampered-subject", false)]
    public async Task NativeSyncRequiresSignedProviderAccountAuthority(string scenario, bool? verified)
    {
        var ct = TestContext.Current!.Execution.CancellationToken;
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(primaryProvider: AuthenticationProviderKind.Keycloak);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        string subject = "opaque:" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        Guid sessionId = Guid.CreateVersion7();
        Guid platformHint = Guid.CreateVersion7();
        List<Claim> claims =
        [
            new("email", $"external-{Guid.CreateVersion7():N}@example.test"),
            new("given_name", "External"), new("family_name", "Member"), new("auth_provider", "keycloak"),
            new("internal_user_id", platformHint.ToString("D"))
        ];
        if (scenario != "missing-subject") claims.Add(new("sid", sessionId.ToString("D")));
        if (scenario is not ("missing-subject" or "sid-only"))
            claims.Add(new(scenario == "nameidentifier-only" ? ClaimTypes.NameIdentifier : "sub", subject));
        if (verified.HasValue) claims.Add(new("email_verified", verified.Value ? "true" : "false", ClaimValueTypes.Boolean));
        string? issuer = scenario switch
        {
            "missing-issuer" => null,
            "wrong-issuer" => "https://other.example.test/realms/ISLAMU",
            _ => factory.ExternalIssuer
        };
        string token = factory.CreateExternalProviderToken(claims, issuer);
        if (scenario is "forged-signature" or "tampered-subject")
        {
            string[] segments = token.Split('.');
            if (scenario == "forged-signature") segments[2] = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(256));
            else
            {
                var payload = JwtPayload.Deserialize(new JwtSecurityTokenHandler().ReadJwtToken(token).Payload.SerializeToJson());
                payload["sub"] = "different-opaque-account";
                segments[1] = Base64UrlEncoder.Encode(payload.SerializeToJson());
            }
            token = string.Join('.', segments);
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await using var before = factory.CreateDatabase();
        int usersBefore = await before.Users.CountAsync(ct);
        using var sync = await client.PostAsync("/api/user/sync", null, ct);
        bool accepted = scenario == "opaque";
        await Assert.That(sync.StatusCode).IsEqualTo(accepted ? HttpStatusCode.OK : HttpStatusCode.Unauthorized);
        await using var database = factory.CreateDatabase();
        if (!accepted)
        {
            await Assert.That(await database.Users.CountAsync(ct)).IsEqualTo(usersBefore);
            await Assert.That(await database.UserExternalLogins.AnyAsync(ct)).IsFalse();
            return;
        }
        string accountKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(factory.ExternalIssuer, subject).Value;
        var binding = await database.UserExternalLogins.Include(login => login.User).SingleAsync(ct);
        await Assert.That(binding.ProviderKey == accountKey).IsTrue();
        await Assert.That(binding.UserId != sessionId && binding.UserId != platformHint).IsTrue();
        await Assert.That(binding.User.EmailVerified).IsEqualTo(verified == true);
        await Assert.That(await database.LocalIdentityUsers.AnyAsync(ct)).IsFalse();
    }
}
