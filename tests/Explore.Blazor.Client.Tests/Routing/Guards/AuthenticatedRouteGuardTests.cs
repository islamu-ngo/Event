// Verifies IsAuthenticated check and redirect path generation.

using Blazouter.Models;
using Explore.Blazor.Client.Routing.Guards;
using Explore.Blazor.Client.Tests.Common.Authentication;

namespace Explore.Blazor.Client.Tests.Routing.Guards;

public class AuthenticatedRouteGuardTests
{
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

        var guard = new AuthenticatedRouteGuard(authStateProvider);
        var routeMatch = new RouteMatch { MatchedPath = "/user/profile" };

        // Act
        var result = await guard.CanActivateAsync(routeMatch);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CanActivateAsync_AuthenticatedUser_ReturnsTrue()
    {
        // Arrange
        var principal = new AuthenticationTestBuilder()
            .WithUser(AuthenticationTestConstants.DefaultUserId, "Regular User")
            .BuildPrincipal();

        var authState = new AuthenticationState(principal);
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync().Returns(Task.FromResult(authState));

        var guard = new AuthenticatedRouteGuard(authStateProvider);
        var routeMatch = new RouteMatch { MatchedPath = "/user/profile" };

        // Act
        var result = await guard.CanActivateAsync(routeMatch);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task CanActivateAsync_AuthenticatedAdmin_ReturnsTrue()
    {
        // Arrange — admin is still authenticated, so the guard allows them
        var principal = new AuthenticationTestBuilder()
            .WithUser(AuthenticationTestConstants.AdminUserId, "Admin User")
            .WithClaim("explore:admin:instance", "true")
            .BuildPrincipal();

        var authState = new AuthenticationState(principal);
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync().Returns(Task.FromResult(authState));

        var guard = new AuthenticatedRouteGuard(authStateProvider);
        var routeMatch = new RouteMatch { MatchedPath = "/myevents" };

        // Act
        var result = await guard.CanActivateAsync(routeMatch);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task GetRedirectPathAsync_WithMatchedPath_ReturnsLoginUrlWithReturnUrl()
    {
        // Arrange
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        var guard = new AuthenticatedRouteGuard(authStateProvider);
        var routeMatch = new RouteMatch { MatchedPath = "/user/profile" };

        // Act
        var redirectPath = await guard.GetRedirectPathAsync(routeMatch);

        // Assert
        await Assert.That(redirectPath).IsEqualTo("/login?returnUrl=%2Fuser%2Fprofile");
    }

    [Test]
    public async Task GetRedirectPathAsync_WithEmptyMatchedPath_ReturnsLoginWithRootReturnUrl()
    {
        // Arrange
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        var guard = new AuthenticatedRouteGuard(authStateProvider);
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
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        var guard = new AuthenticatedRouteGuard(authStateProvider);
        var routeMatch = new RouteMatch { MatchedPath = null };

        // Act
        var redirectPath = await guard.GetRedirectPathAsync(routeMatch);

        // Assert
        await Assert.That(redirectPath).IsEqualTo("/login?returnUrl=%2F");
    }
}
