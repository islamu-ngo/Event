using System.Security.Claims;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace Explore.Blazor.IntegrationTests.Services;

public class AuthStateSerializationPolicyTests
{
    [Test]
    public async Task SerializeDisplaySafeClaimsAsync_AuthenticatedUser_OnlySerializesDisplayClaims()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim("preferred_username", "amina"),
            new Claim("name", "Amina Example"),
            new Claim("given_name", "Amina"),
            new Claim("family_name", "Example"),
            new Claim("email", "amina@example.test"),
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("internal_user_id", Guid.NewGuid().ToString()),
            new Claim("tenant_id", Guid.NewGuid().ToString()),
            new Claim("explore:admin:instance", "true"),
            new Claim("explore:admin:tenant", Guid.NewGuid().ToString()),
            new Claim("explore:admin:organization", Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("access_token", "server-held-access-token"),
            new Claim("refresh_token", "server-held-refresh-token"),
            new Claim("id_token", "server-held-id-token")
        ],
        authenticationType: "Test",
        nameType: "preferred_username",
        roleType: ClaimTypes.Role);

        var result = await AuthStateSerializationPolicy.SerializeDisplaySafeClaimsAsync(
            new AuthenticationState(new ClaimsPrincipal(identity)));

        await Assert.That(result).IsNotNull();
        var claims = result!.Claims.ToDictionary(c => c.Type, c => c.Value);

        await Assert.That(claims).ContainsKey("preferred_username");
        await Assert.That(claims).ContainsKey("name");
        await Assert.That(claims).ContainsKey("given_name");
        await Assert.That(claims).ContainsKey("family_name");

        await Assert.That(claims).DoesNotContainKey("email");
        await Assert.That(claims).DoesNotContainKey("sub");
        await Assert.That(claims).DoesNotContainKey(ClaimTypes.NameIdentifier);
        await Assert.That(claims).DoesNotContainKey("internal_user_id");
        await Assert.That(claims).DoesNotContainKey("tenant_id");
        await Assert.That(claims).DoesNotContainKey("explore:admin:instance");
        await Assert.That(claims).DoesNotContainKey("explore:admin:tenant");
        await Assert.That(claims).DoesNotContainKey("explore:admin:organization");
        await Assert.That(claims).DoesNotContainKey(ClaimTypes.Role);
        await Assert.That(claims).DoesNotContainKey("access_token");
        await Assert.That(claims).DoesNotContainKey("refresh_token");
        await Assert.That(claims).DoesNotContainKey("id_token");
    }

    [Test]
    public async Task SerializeDisplaySafeClaimsAsync_AnonymousUser_ReturnsNull()
    {
        var result = await AuthStateSerializationPolicy.SerializeDisplaySafeClaimsAsync(
            new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

        await Assert.That(result).IsNull();
    }
}
