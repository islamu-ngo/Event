using System.Security.Claims;
using Explore.API.Authentication;
using Explore.Application.Constants;
using Microsoft.AspNetCore.Http;

namespace Event.API.IntegrationTests.Features;

public sealed class RegistrationOrderCreationPrincipalTests
{
    [Test]
    public async Task AcceptedAccountRetainsPurposeAndCapabilityScopeWithoutTakingBodyAuthority()
    {
        Guid accountId = Guid.CreateVersion7();
        var context = new DefaultHttpContext
        {
            User = Account(accountId, "Bearer")
        };
        var reader = new HttpRegistrationCreationPrincipal(new HttpContextAccessor { HttpContext = context });
        var original = reader.GetCurrent();
        await Assert.That(original!.AccountUserId).IsEqualTo(accountId);
        await Assert.That(reader.GetCurrent()).IsEqualTo(original);
        context.Request.Headers["X-Registration-Order-Capability"] = Guid.CreateVersion7().ToString("N");
        await Assert.That(reader.GetCurrent()!.ScopeFingerprint == original.ScopeFingerprint).IsFalse();
        context.Request.Headers.Clear();
        context.User = Account(accountId, "other-admitted-account-purpose");
        await Assert.That(reader.GetCurrent()!.ScopeFingerprint == original.ScopeFingerprint).IsFalse();
    }

    [Test]
    [Arguments(ApiAuthenticationSchemeNames.ApiKey)]
    [Arguments(ApiAuthenticationSchemeNames.SetupSecret)]
    [Arguments(ApiAuthenticationSchemeNames.AdmissionScanner)]
    [Arguments(ApiAuthenticationSchemeNames.ManagedControlPlane)]
    [Arguments(ApiAuthenticationSchemeNames.AtprotoBootstrap)]
    [Arguments(ApiAuthenticationSchemeNames.AtprotoSession)]
    [Arguments(ApiAuthenticationSchemeNames.PrivacyErasureReceipt)]
    [Arguments(ApiAuthenticationSchemeNames.LocalCredentialReplacement)]
    public async Task UnsupportedPurposeNeverBecomesBuyerDespiteGuidSubject(string purpose)
    {
        Guid subjectId = Guid.CreateVersion7();
        var identity = new ClaimsIdentity(
            [new Claim("sub", subjectId.ToString("D")),
             new Claim(ClaimTypes.NameIdentifier, subjectId.ToString("D"))], purpose);
        var reader = new HttpRegistrationCreationPrincipal(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        });
        await Assert.That(reader.GetCurrent()).IsNull();
    }

    [Test]
    public async Task SeparateApiKeysForSameOwnerAreBothRejectedRatherThanCollapsedIntoAccount()
    {
        Guid accountId = Guid.CreateVersion7();
        foreach (string key in new[] { Guid.CreateVersion7().ToString("N"), Guid.CreateVersion7().ToString("N") })
        {
            var identity = new ClaimsIdentity(
                [new Claim("sub", accountId.ToString("D")),
                 new Claim(ApiAuthenticationClaimTypes.ApiKeyId, key),
                 new Claim(ApiAuthenticationClaimTypes.OwnerId, accountId.ToString("D"))],
                ApiAuthenticationSchemeNames.ApiKey);
            var reader = new HttpRegistrationCreationPrincipal(new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            });
            await Assert.That(reader.GetCurrent()).IsNull();
        }
    }

    [Test]
    public async Task MissingOrAmbiguousAccountAuthorityCannotProduceOperationIdentity()
    {
        var context = new DefaultHttpContext();
        var reader = new HttpRegistrationCreationPrincipal(new HttpContextAccessor { HttpContext = context });
        await Assert.That(reader.GetCurrent()).IsNull();
        context.User = new ClaimsPrincipal([
            new ClaimsIdentity([new Claim("sub", Guid.CreateVersion7().ToString("D"))], "Bearer"),
            new ClaimsIdentity([new Claim("sub", Guid.CreateVersion7().ToString("D"))], "another-purpose")
        ]);
        await Assert.That(reader.GetCurrent()).IsNull();
    }

    private static ClaimsPrincipal Account(Guid accountId, string purpose) =>
        new(new ClaimsIdentity([new Claim("sub", accountId.ToString("D"))], purpose));
}
