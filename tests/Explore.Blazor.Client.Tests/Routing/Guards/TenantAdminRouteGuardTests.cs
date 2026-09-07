using Blazouter.Models;
using Explore.Blazor.Client.Routing.Guards;
using Explore.Blazor.Client.Tests.Common.Authentication;

namespace Explore.Blazor.Client.Tests.Routing.Guards;

public class TenantAdminRouteGuardTests
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
        var tenantOnboardingService = Substitute.For<ITenantOnboardingService>();
        var instanceOnboardingService = Substitute.For<IInstanceOnboardingService>();
        var userService = Substitute.For<IUserService>();

        var guard = new TenantAdminRouteGuard(authStateProvider, tenantOnboardingService, userService);

        // Act
        var result = await guard.CanActivateAsync(new RouteMatch { MatchedPath = "/settings/admin" });

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CanActivateAsync_UserWithTenantClaim_ReturnsFalseWithoutBffStatus()
    {
        // Arrange
        var principal = new AuthenticationTestBuilder()
            .WithUser(AuthenticationTestConstants.AdminUserId, "Tenant Admin")
            .WithClaim("explore:admin:tenant", AuthenticationTestConstants.DefaultTenantId.ToString())
            .BuildPrincipal();

        var authState = new AuthenticationState(principal);
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync().Returns(Task.FromResult(authState));
        var tenantOnboardingService = Substitute.For<ITenantOnboardingService>();
        tenantOnboardingService.GetStatusAsync().Returns((TenantOnboardingStatusDto?)null);
        var instanceOnboardingService = Substitute.For<IInstanceOnboardingService>();
        instanceOnboardingService.GetStatusAsync().Returns((InstanceOnboardingStatusDto?)null);
        var userService = Substitute.For<IUserService>();
        userService.GetAdminAuthorityAsync().Returns((AdminAuthorityDto?)null);

        var guard = new TenantAdminRouteGuard(authStateProvider, tenantOnboardingService, userService);

        // Act
        var result = await guard.CanActivateAsync(new RouteMatch { MatchedPath = "/settings/admin" });

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CanActivateAsync_UserWithOnlyInstanceClaim_ReturnsFalse()
    {
        // Arrange
        var principal = new AuthenticationTestBuilder()
            .WithUser(AuthenticationTestConstants.AdminUserId, "Instance Admin")
            .WithClaim("explore:admin:instance", "true")
            .BuildPrincipal();

        var authState = new AuthenticationState(principal);
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync().Returns(Task.FromResult(authState));
        var tenantOnboardingService = Substitute.For<ITenantOnboardingService>();
        tenantOnboardingService.GetStatusAsync().Returns((TenantOnboardingStatusDto?)null);
        var instanceOnboardingService = Substitute.For<IInstanceOnboardingService>();
        instanceOnboardingService.GetStatusAsync().Returns((InstanceOnboardingStatusDto?)null);
        var userService = Substitute.For<IUserService>();
        userService.GetAdminAuthorityAsync().Returns((AdminAuthorityDto?)null);

        var guard = new TenantAdminRouteGuard(authStateProvider, tenantOnboardingService, userService);

        // Act
        var result = await guard.CanActivateAsync(new RouteMatch { MatchedPath = "/settings/admin" });

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CanActivateAsync_WithoutClaims_ButTenantStatusAdmin_ReturnsTrue()
    {
        // Arrange
        var principal = new AuthenticationTestBuilder()
            .WithUser(AuthenticationTestConstants.DefaultUserId, "Tenant Admin")
            .BuildPrincipal();

        var authState = new AuthenticationState(principal);
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync().Returns(Task.FromResult(authState));
        var tenantOnboardingService = Substitute.For<ITenantOnboardingService>();
        tenantOnboardingService.GetStatusAsync().Returns(new TenantOnboardingStatusDto
        {
            IsAuthenticated = true,
            IsCurrentUserTenantAdministrator = true
        });
        var instanceOnboardingService = Substitute.For<IInstanceOnboardingService>();
        instanceOnboardingService.GetStatusAsync().Returns(new InstanceOnboardingStatusDto
        {
            IsAuthenticated = true,
            IsCurrentUserInstanceAdmin = false,
            SelectedDeploymentMode = "MultiTenant"
        });
        var userService = Substitute.For<IUserService>();
        userService.GetAdminAuthorityAsync().Returns((AdminAuthorityDto?)null);

        var guard = new TenantAdminRouteGuard(authStateProvider, tenantOnboardingService, userService);

        // Act
        var result = await guard.CanActivateAsync(new RouteMatch { MatchedPath = "/settings/admin" });

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task CanActivateAsync_WithoutClaims_ButOnlyPlatformStatusAdmin_ReturnsFalse()
    {
        // Arrange
        var principal = new AuthenticationTestBuilder()
            .WithUser(AuthenticationTestConstants.DefaultUserId, "Platform Admin")
            .BuildPrincipal();

        var authState = new AuthenticationState(principal);
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync().Returns(Task.FromResult(authState));
        var tenantOnboardingService = Substitute.For<ITenantOnboardingService>();
        tenantOnboardingService.GetStatusAsync().Returns(new TenantOnboardingStatusDto
        {
            IsAuthenticated = true,
            IsCurrentUserPlatformAdministrator = true,
            IsCurrentUserTenantAdministrator = false
        });
        var instanceOnboardingService = Substitute.For<IInstanceOnboardingService>();
        instanceOnboardingService.GetStatusAsync().Returns(new InstanceOnboardingStatusDto
        {
            IsAuthenticated = true,
            IsCurrentUserInstanceAdmin = true,
            SelectedDeploymentMode = "MultiTenant"
        });
        var userService = Substitute.For<IUserService>();
        userService.GetAdminAuthorityAsync().Returns(new AdminAuthorityDto
        {
            IsInstanceAdmin = true,
            AdminTenantIds = [],
            AdminOrganizationIds = [],
            HasAnyAuthority = true
        });

        var guard = new TenantAdminRouteGuard(authStateProvider, tenantOnboardingService, userService);

        // Act
        var result = await guard.CanActivateAsync(new RouteMatch { MatchedPath = "/settings/admin" });

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CanActivateAsync_SingleTenantInstanceAdminWithoutTenantAuthority_ReturnsFalse()
    {
        // Arrange
        var principal = new AuthenticationTestBuilder()
            .WithUser(AuthenticationTestConstants.AdminUserId, "Setup Admin")
            .BuildPrincipal();

        var authState = new AuthenticationState(principal);
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync().Returns(Task.FromResult(authState));
        var tenantOnboardingService = Substitute.For<ITenantOnboardingService>();
        tenantOnboardingService.GetStatusAsync().Returns((TenantOnboardingStatusDto?)null);
        var instanceOnboardingService = Substitute.For<IInstanceOnboardingService>();
        instanceOnboardingService.GetStatusAsync().Returns(new InstanceOnboardingStatusDto
        {
            IsAuthenticated = true,
            IsCurrentUserInstanceAdmin = true,
            SelectedDeploymentMode = "SingleTenant"
        });
        var userService = Substitute.For<IUserService>();

        var guard = new TenantAdminRouteGuard(authStateProvider, tenantOnboardingService, userService);

        // Act
        var result = await guard.CanActivateAsync(new RouteMatch { MatchedPath = "/settings/admin" });

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CanActivateAsync_SingleTenantAdminAuthority_ReturnsTrueWhenOnboardingStatusIsStale()
    {
        // Arrange
        var principal = new AuthenticationTestBuilder()
            .WithUser(AuthenticationTestConstants.AdminUserId, "Setup Admin")
            .BuildPrincipal();

        var authState = new AuthenticationState(principal);
        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync().Returns(Task.FromResult(authState));
        var tenantOnboardingService = Substitute.For<ITenantOnboardingService>();
        tenantOnboardingService.GetStatusAsync().Returns((TenantOnboardingStatusDto?)null);
        var instanceOnboardingService = Substitute.For<IInstanceOnboardingService>();
        instanceOnboardingService.GetStatusAsync().Returns(new InstanceOnboardingStatusDto
        {
            IsAuthenticated = true,
            IsCurrentUserInstanceAdmin = false,
            SelectedDeploymentMode = "SingleTenant"
        });
        var userService = Substitute.For<IUserService>();
        userService.GetAdminAuthorityAsync().Returns(new AdminAuthorityDto
        {
            IsInstanceAdmin = true,
            AdminTenantIds = [AuthenticationTestConstants.DefaultTenantId],
            AdminOrganizationIds = [],
            HasAnyAuthority = true
        });

        var guard = new TenantAdminRouteGuard(authStateProvider, tenantOnboardingService, userService);

        // Act
        var result = await guard.CanActivateAsync(new RouteMatch { MatchedPath = "/settings/admin" });

        // Assert
        await Assert.That(result).IsTrue();
    }
}
