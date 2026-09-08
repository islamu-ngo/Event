using Blazouter.Models;
using Explore.Blazor.Client.Routing.Guards;
using Explore.Blazor.Client.Tests.Common.Authentication;

namespace Explore.Blazor.Client.Tests.Routing.Guards;

public class OrgAdminRouteGuardTests
{
    private static readonly Guid TestOrgId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid OtherOrgId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    #region CanActivateAsync — Deny scenarios

    [Test]
    public async Task CanActivateAsync_UnauthenticatedUser_ReturnsFalse()
    {
        // Arrange
        var principal = new AuthenticationTestBuilder()
            .AsAnonymous()
            .BuildPrincipal();

        var authState = new AuthenticationState(principal);
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync().Returns(Task.FromResult(authState));

        var guard = CreateGuard(authStateProvider);
        var routeMatch = new RouteMatch { MatchedPath = $"/settings/organization/{TestOrgId}" };

        // Act
        var result = await guard.CanActivateAsync(routeMatch);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CanActivateAsync_AuthenticatedUserWithoutAdminClaims_ReturnsFalse()
    {
        // Arrange
        var principal = new AuthenticationTestBuilder()
            .WithUser(AuthenticationTestConstants.DefaultUserId, "Regular User")
            .BuildPrincipal();

        var authState = new AuthenticationState(principal);
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync().Returns(Task.FromResult(authState));

        var guard = CreateGuard(authStateProvider);
        var routeMatch = new RouteMatch { MatchedPath = $"/settings/organization/{TestOrgId}" };

        // Act
        var result = await guard.CanActivateAsync(routeMatch);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CanActivateAsync_OrgAdminForDifferentOrg_ReturnsFalse()
    {
        // Arrange — user is org admin for OtherOrgId, but route targets TestOrgId
        var principal = new AuthenticationTestBuilder()
            .WithUser(AuthenticationTestConstants.DefaultUserId, "Other Org Admin")
            .WithClaim("explore:admin:organization", OtherOrgId.ToString())
            .BuildPrincipal();

        var authState = new AuthenticationState(principal);
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync().Returns(Task.FromResult(authState));

        var guard = CreateGuard(authStateProvider, OtherOrgId);
        var routeMatch = new RouteMatch { MatchedPath = $"/settings/organization/{TestOrgId}" };

        // Act
        var result = await guard.CanActivateAsync(routeMatch);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CanActivateAsync_NoOrgIdInRoutePath_ReturnsFalse()
    {
        // Arrange — valid org admin claim but no GUID in the route path
        var principal = new AuthenticationTestBuilder()
            .WithUser(AuthenticationTestConstants.DefaultUserId, "Org Admin")
            .WithClaim("explore:admin:organization", TestOrgId.ToString())
            .BuildPrincipal();

        var authState = new AuthenticationState(principal);
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync().Returns(Task.FromResult(authState));

        var guard = CreateGuard(authStateProvider, TestOrgId);
        var routeMatch = new RouteMatch { MatchedPath = "/settings/organization/invalid-id" };

        // Act
        var result = await guard.CanActivateAsync(routeMatch);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CanActivateAsync_NullMatchedPath_ReturnsFalse()
    {
        // Arrange
        var principal = new AuthenticationTestBuilder()
            .WithUser(AuthenticationTestConstants.DefaultUserId, "Org Admin")
            .WithClaim("explore:admin:organization", TestOrgId.ToString())
            .BuildPrincipal();

        var authState = new AuthenticationState(principal);
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync().Returns(Task.FromResult(authState));

        var guard = CreateGuard(authStateProvider, TestOrgId);
        var routeMatch = new RouteMatch { MatchedPath = null };

        // Act
        var result = await guard.CanActivateAsync(routeMatch);

        // Assert
        await Assert.That(result).IsFalse();
    }

    #endregion

    #region CanActivateAsync — Allow scenarios

    [Test]
    public async Task CanActivateAsync_InstanceAdmin_ReturnsFalse()
    {
        // Arrange — instance admin alone does not grant org route access
        var principal = new AuthenticationTestBuilder()
            .WithUser(AuthenticationTestConstants.AdminUserId, "Instance Admin")
            .WithClaim("explore:admin:instance", "true")
            .BuildPrincipal();

        var authState = new AuthenticationState(principal);
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync().Returns(Task.FromResult(authState));

        var guard = CreateGuard(authStateProvider);
        var routeMatch = new RouteMatch { MatchedPath = $"/settings/organization/{TestOrgId}" };

        // Act
        var result = await guard.CanActivateAsync(routeMatch);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CanActivateAsync_TenantAdmin_ReturnsFalse()
    {
        // Arrange — tenant admin alone does not grant org route access
        var principal = new AuthenticationTestBuilder()
            .WithUser(AuthenticationTestConstants.AdminUserId, "Tenant Admin")
            .WithClaim("explore:admin:tenant", AuthenticationTestConstants.DefaultTenantId.ToString())
            .BuildPrincipal();

        var authState = new AuthenticationState(principal);
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync().Returns(Task.FromResult(authState));

        var guard = CreateGuard(authStateProvider);
        var routeMatch = new RouteMatch { MatchedPath = $"/settings/organization/{TestOrgId}" };

        // Act
        var result = await guard.CanActivateAsync(routeMatch);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CanActivateAsync_OrgAdminForMatchingOrg_ReturnsTrue()
    {
        // Arrange — user has org admin claim for the exact org in the route
        var principal = new AuthenticationTestBuilder()
            .WithUser(AuthenticationTestConstants.DefaultUserId, "Org Admin")
            .WithClaim("explore:admin:organization", TestOrgId.ToString())
            .BuildPrincipal();

        var authState = new AuthenticationState(principal);
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync().Returns(Task.FromResult(authState));

        var guard = CreateGuard(authStateProvider, TestOrgId);
        var routeMatch = new RouteMatch { MatchedPath = $"/settings/organization/{TestOrgId}" };

        // Act
        var result = await guard.CanActivateAsync(routeMatch);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task CanActivateAsync_OrgAdminWithMultipleOrgClaims_ReturnsTrueForMatchingOrg()
    {
        // Arrange — user is admin for multiple orgs, one of which matches the route
        var principal = new AuthenticationTestBuilder()
            .WithUser(AuthenticationTestConstants.DefaultUserId, "Multi-Org Admin")
            .WithClaim("explore:admin:organization", OtherOrgId.ToString())
            .WithClaim("explore:admin:organization", TestOrgId.ToString())
            .BuildPrincipal();

        var authState = new AuthenticationState(principal);
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync().Returns(Task.FromResult(authState));

        var guard = CreateGuard(authStateProvider, OtherOrgId, TestOrgId);
        var routeMatch = new RouteMatch { MatchedPath = $"/settings/organization/{TestOrgId}" };

        // Act
        var result = await guard.CanActivateAsync(routeMatch);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task CanActivateAsync_OrgIdCaseInsensitiveMatch_ReturnsTrue()
    {
        // Arrange — GUID in route is uppercase, claim is lowercase
        var uppercaseOrgId = TestOrgId.ToString().ToUpperInvariant();
        var principal = new AuthenticationTestBuilder()
            .WithUser(AuthenticationTestConstants.DefaultUserId, "Org Admin")
            .WithClaim("explore:admin:organization", TestOrgId.ToString().ToLowerInvariant())
            .BuildPrincipal();

        var authState = new AuthenticationState(principal);
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync().Returns(Task.FromResult(authState));

        var guard = CreateGuard(authStateProvider, TestOrgId);
        var routeMatch = new RouteMatch { MatchedPath = $"/settings/organization/{uppercaseOrgId}" };

        // Act
        var result = await guard.CanActivateAsync(routeMatch);

        // Assert
        await Assert.That(result).IsTrue();
    }

    #endregion

    #region GetRedirectPathAsync

    [Test]
    public async Task GetRedirectPathAsync_WithMatchedPath_ReturnsLoginUrlWithReturnUrl()
    {
        // Arrange
        var authStateProvider = CreateAnonymousAuthStateProvider();
        var guard = CreateGuard(authStateProvider);
        var routeMatch = new RouteMatch { MatchedPath = $"/settings/organization/{TestOrgId}" };

        // Act
        var redirectPath = await guard.GetRedirectPathAsync(routeMatch);

        // Assert
        await Assert.That(redirectPath).Contains("/login?returnUrl=");
        await Assert.That(redirectPath).Contains("organization");
    }

    [Test]
    public async Task GetRedirectPathAsync_WithEmptyMatchedPath_ReturnsLoginWithRootReturnUrl()
    {
        // Arrange
        var authStateProvider = CreateAnonymousAuthStateProvider();
        var guard = CreateGuard(authStateProvider);
        var routeMatch = new RouteMatch { MatchedPath = "" };

        // Act
        var redirectPath = await guard.GetRedirectPathAsync(routeMatch);

        // Assert
        await Assert.That(redirectPath).IsEqualTo("/login?returnUrl=%2F");
    }

    [Test]
    public async Task GetRedirectPathAsync_WithNullMatchedPath_ReturnsLoginWithRootReturnUrl()
    {
        // Arrange
        var authStateProvider = CreateAnonymousAuthStateProvider();
        var guard = CreateGuard(authStateProvider);
        var routeMatch = new RouteMatch { MatchedPath = null };

        // Act
        var redirectPath = await guard.GetRedirectPathAsync(routeMatch);

        // Assert
        await Assert.That(redirectPath).IsEqualTo("/login?returnUrl=%2F");
    }

    #endregion

    private static OrgAdminRouteGuard CreateGuard(
        AuthenticationStateProvider authStateProvider,
        params Guid[] adminOrganizationIds)
    {
        var userService = Substitute.For<IUserService>();
        userService.GetAdminAuthorityAsync().Returns(new AdminAuthorityDto
        {
            AdminOrganizationIds = adminOrganizationIds
        });

        return new OrgAdminRouteGuard(authStateProvider, userService);
    }

    private static AuthenticationStateProvider CreateAnonymousAuthStateProvider()
    {
        var principal = new AuthenticationTestBuilder().AsAnonymous().BuildPrincipal();
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync()
            .Returns(Task.FromResult(new AuthenticationState(principal)));
        return authStateProvider;
    }
}
