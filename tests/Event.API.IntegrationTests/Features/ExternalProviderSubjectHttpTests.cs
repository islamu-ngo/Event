using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Configuration;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class ExternalProviderSubjectHttpTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ValidatedIssuerNotRequestBodyControlsCorrelationAndReadsStayBindingOnly(bool trusted)
    {
        var ct = TestContext.Current!.Execution.CancellationToken;
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(
            primaryProvider: AuthenticationProviderKind.Keycloak);
        factory.Services.GetRequiredService<IOptions<IdentityCorrelationOptions>>().Value.TrustedIssuers =
            trusted ? [factory.ExternalIssuer] : [];
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        Guid firstSubject = Guid.CreateVersion7();
        string email = $"correlation-{firstSubject:N}@example.test";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.CreateExternalProviderToken(firstSubject, email, true));
        using HttpResponseMessage first = await client.PostAsync("/api/user/sync", null, ct);
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await using var before = factory.CreateDatabase();
        Guid firstUserId = (await before.UserExternalLogins.SingleAsync(ct)).UserId;
        int usersBefore = await before.Users.CountAsync(ct);
        Guid secondSubject = Guid.CreateVersion7();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.CreateExternalProviderToken(secondSubject, email, true));

        using HttpResponseMessage unboundRead = await client.GetAsync("/api/user", ct);
        await Assert.That(unboundRead.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using HttpResponseMessage synchronized = await client.PostAsJsonAsync("/api/user/sync", new
        {
            id = firstUserId,
            email,
            emailVerified = true,
            trustedIssuer = true,
            issuer = factory.ExternalIssuer
        }, ct);

        await Assert.That(synchronized.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await using var database = factory.CreateDatabase();
        string key = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            factory.ExternalIssuer, secondSubject.ToString("D")).Value;
        var binding = await database.UserExternalLogins.Include(login => login.User)
            .SingleAsync(login => login.ProviderKey == key, ct);
        await Assert.That(binding.UserId == firstUserId).IsEqualTo(trusted);
        await Assert.That(binding.User.EmailVerified).IsEqualTo(true);
        await Assert.That(await database.Users.CountAsync(ct)).IsEqualTo(usersBefore + (trusted ? 0 : 1));
    }

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
