using System.Security.Claims;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Shell;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Explore.Blazor.Client.Tests.Services.Shell;

public sealed class UiShellContextServiceTests : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly IUiShellClient _apiClient;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly CurrentUserState _currentUserState;

    public UiShellContextServiceTests()
    {
        _apiClient = Substitute.For<IUiShellClient>();
        _authStateProvider = Substitute.For<AuthenticationStateProvider>();
        _currentUserState = new CurrentUserState();

        _services = new ServiceCollection()
            .AddSingleton(_apiClient)
            .AddSingleton(_authStateProvider)
            .AddSingleton(_currentUserState)
            .AddSingleton(Substitute.For<ILogger<UiShellContextService>>())
            .BuildServiceProvider();
    }

    public void Dispose() => _services.Dispose();

    [Test]
    public async Task GetContextAsync_AnonymousUser_NeverCallsEndpoint()
    {
        _authStateProvider.GetAuthenticationStateAsync()
            .Returns(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

        var service = new UiShellContextService(_apiClient, _authStateProvider, _currentUserState,
            Substitute.For<ILogger<UiShellContextService>>());

        var result = await service.GetContextAsync();

        await Assert.That(result).IsNull();
        await _apiClient.DidNotReceive().GetUiShellContextAsync(
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetContextAsync_AuthenticatedUser_CallsEndpointAndCachesResult()
    {
        _authStateProvider.GetAuthenticationStateAsync()
            .Returns(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "test")], "TestAuth"))));

        var context = new UiShellContextDto { DeploymentMode = "MultiTenant" };
        _apiClient.GetUiShellContextAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(context);

        var service = new UiShellContextService(_apiClient, _authStateProvider, _currentUserState,
            Substitute.For<ILogger<UiShellContextService>>());

        var result = await service.GetContextAsync();
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.DeploymentMode).IsEqualTo("MultiTenant");

        var cached = await service.GetCachedContextAsync();
        await Assert.That(cached).IsNotNull();

        await _apiClient.Received(1).GetUiShellContextAsync(
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetContextAsync_ApiFailure_ReturnsNullSafely()
    {
        _authStateProvider.GetAuthenticationStateAsync()
            .Returns(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "test")], "TestAuth"))));

        _apiClient.GetUiShellContextAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Throws(new ApiException("fail", 500, "err", null, null));

        var service = new UiShellContextService(_apiClient, _authStateProvider, _currentUserState,
            Substitute.For<ILogger<UiShellContextService>>());

        var result = await service.GetContextAsync();

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task CurrentUserState_OnChanged_InvalidatesCache()
    {
        _authStateProvider.GetAuthenticationStateAsync()
            .Returns(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "test")], "TestAuth"))));

        var firstContext = new UiShellContextDto { DeploymentMode = "MultiTenant" };
        var secondContext = new UiShellContextDto { DeploymentMode = "SingleTenant" };

        _apiClient.GetUiShellContextAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(firstContext, secondContext);

        var service = new UiShellContextService(_apiClient, _authStateProvider, _currentUserState,
            Substitute.For<ILogger<UiShellContextService>>());

        var first = await service.GetCachedContextAsync();
        await Assert.That(first!.DeploymentMode).IsEqualTo("MultiTenant");

        _currentUserState.NotifyUpdated(new UserDto());

        var second = await service.GetCachedContextAsync();
        await Assert.That(second!.DeploymentMode).IsEqualTo("SingleTenant");

        await _apiClient.Received(2).GetUiShellContextAsync(
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetCachedContextAsync_AfterLogout_DoesNotReturnAuthenticatedCache()
    {
        _authStateProvider.GetAuthenticationStateAsync()
            .Returns(
                new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "test")], "TestAuth"))),
                new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())),
                new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
        _apiClient.GetUiShellContextAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new UiShellContextDto { DeploymentMode = "MultiTenant" });

        var service = new UiShellContextService(_apiClient, _authStateProvider, _currentUserState,
            Substitute.For<ILogger<UiShellContextService>>());

        var authenticated = await service.GetContextAsync();
        var afterLogout = await service.GetCachedContextAsync();

        await Assert.That(authenticated).IsNotNull();
        await Assert.That(afterLogout).IsNull();
        await _apiClient.Received(1).GetUiShellContextAsync(
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }
}
