using System.Security.Claims;
using Explore.Application.Constants;
using Explore.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Explore.Infrastructure.Tests.Identity;

public sealed class UserContextTests
{
    [Test]
    public async Task UserId_UsesDocumentedClaimFallbackBeforeInternalClaim()
    {
        var subUserId = Guid.NewGuid();
        var nameIdentifierUserId = Guid.NewGuid();
        var sidUserId = Guid.NewGuid();
        var internalUserId = Guid.NewGuid();
        var context = CreateUserContext(
            new Claim("sub", subUserId.ToString("D")),
            new Claim(ClaimTypes.NameIdentifier, nameIdentifierUserId.ToString("D")),
            new Claim("sid", sidUserId.ToString("D")),
            new Claim("internal_user_id", internalUserId.ToString("D")));

        await Assert.That(context.UserId).IsEqualTo(subUserId);
    }

    [Test]
    public async Task UserId_SkipsNonGuidProviderClaimsAndUsesInternalClaim()
    {
        var internalUserId = Guid.NewGuid();
        var context = CreateUserContext(
            new Claim("sub", "external-provider-subject"),
            new Claim("sid", "external-provider-session"),
            new Claim("internal_user_id", internalUserId.ToString("D")));

        await Assert.That(context.UserId).IsEqualTo(internalUserId);
    }

    [Test]
    public async Task GetRequiredUserId_ThrowsWhenNoUsableUserIdClaimExists()
    {
        var context = CreateUserContext(new Claim("sub", "external-provider-subject"));

        await Assert.That(() => context.GetRequiredUserId())
            .Throws<UnauthorizedAccessException>();
    }

    [Test]
    public async Task UserId_PurposeBoundPrincipalFailsClosed()
    {
        var userId = Guid.NewGuid();
        var context = CreateUserContext(
            ApiAuthenticationSchemeNames.ApiKey,
            new Claim("sub", userId.ToString("D")));

        await Assert.That(context.UserId).IsNull();
        await Assert.That(() => context.GetRequiredUserId()).Throws<UnauthorizedAccessException>();
    }

    private static UserContext CreateUserContext(params Claim[] claims)
        => CreateUserContext("TestAuth", claims);

    private static UserContext CreateUserContext(string authenticationType, params Claim[] claims)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType))
        });

        return new UserContext(accessor);
    }
}
