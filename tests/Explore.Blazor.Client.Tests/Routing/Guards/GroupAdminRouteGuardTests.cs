using Blazouter.Models;
using Explore.Blazor.Client.Routing.Guards;
using Explore.Blazor.Client.Tests.Common.Authentication;

namespace Explore.Blazor.Client.Tests.Routing.Guards;

public class GroupAdminRouteGuardTests
{
    private static readonly Guid TestGroupId = Guid.Parse("6a5922c6-a7f6-4978-a9cd-76e136e84403");

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

        // Act
        var result = await guard.CanActivateAsync(new RouteMatch { MatchedPath = $"/settings/group/{TestGroupId}" });

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CanActivateAsync_InvalidGroupPath_ReturnsFalse()
    {
        // Arrange
        var principal = new AuthenticationTestBuilder()
            .WithUser(AuthenticationTestConstants.AdminUserId, "User")
            .BuildPrincipal();

        var authState = new AuthenticationState(principal);
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync().Returns(Task.FromResult(authState));
        var guard = CreateGuard(authStateProvider, TestGroupId);

        // Act
        var result = await guard.CanActivateAsync(new RouteMatch { MatchedPath = "/settings/group/not-a-guid" });

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CanActivateAsync_MatchingPersistedGroupAuthority_ReturnsTrue()
    {
        // Arrange
        var principal = new AuthenticationTestBuilder()
            .WithUser(AuthenticationTestConstants.AdminUserId, "Group Admin")
            .BuildPrincipal();

        var authState = new AuthenticationState(principal);
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync().Returns(Task.FromResult(authState));

        var guard = CreateGuard(authStateProvider, TestGroupId);

        // Act
        var result = await guard.CanActivateAsync(new RouteMatch { MatchedPath = $"/settings/group/{TestGroupId}" });

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task CanActivateAsync_DifferentPersistedGroupAuthority_ReturnsFalse()
    {
        // Arrange
        var otherGroupId = Guid.CreateVersion7();
        var principal = new AuthenticationTestBuilder()
            .WithUser(AuthenticationTestConstants.AdminUserId, "Other Group Admin")
            .BuildPrincipal();

        var authState = new AuthenticationState(principal);
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync().Returns(Task.FromResult(authState));

        var guard = CreateGuard(authStateProvider, otherGroupId);

        // Act
        var result = await guard.CanActivateAsync(new RouteMatch { MatchedPath = $"/settings/group/{TestGroupId}" });

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CanActivateAsync_MissingPersistedAuthority_ReturnsFalse()
    {
        // Arrange
        var principal = new AuthenticationTestBuilder()
            .WithUser(AuthenticationTestConstants.DefaultUserId, "Member")
            .BuildPrincipal();

        var authState = new AuthenticationState(principal);
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync().Returns(Task.FromResult(authState));

        var userService = Substitute.For<IUserService>();
        userService.GetAdminAuthorityAsync().Returns((AdminAuthorityDto?)null);
        var guard = new GroupAdminRouteGuard(authStateProvider, userService);

        // Act
        var result = await guard.CanActivateAsync(new RouteMatch { MatchedPath = $"/settings/group/{TestGroupId}" });

        // Assert
        await Assert.That(result).IsFalse();
    }

    private static GroupAdminRouteGuard CreateGuard(
        AuthenticationStateProvider authStateProvider,
        params Guid[] adminGroupIds)
    {
        var userService = Substitute.For<IUserService>();
        userService.GetAdminAuthorityAsync().Returns(new AdminAuthorityDto
        {
            AdminGroupIds = adminGroupIds
        });

        return new GroupAdminRouteGuard(authStateProvider, userService);
    }
}
