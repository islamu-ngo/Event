using Explore.API.BackgroundServices;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Services;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public class CerbosPolicyBootSyncRunnerTests
{
    [Test]
    public async Task RunOnceAsync_WithDeploymentIntent_ReconcilesOnce()
    {
        var configurationService = Substitute.For<IAuthorizationProviderConfigurationService>();
        configurationService.ReconcileDeploymentProviderAsync(Arg.Any<CancellationToken>())
            .Returns(CreateResult(attempted: true, succeeded: true));
        var runner = CreateRunner(configurationService);

        await runner.RunOnceAsync(CancellationToken.None);

        await configurationService.Received(1)
            .ReconcileDeploymentProviderAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunOnceAsync_WithoutDeploymentIntent_CompletesWithoutFailure()
    {
        var configurationService = Substitute.For<IAuthorizationProviderConfigurationService>();
        configurationService.ReconcileDeploymentProviderAsync(Arg.Any<CancellationToken>())
            .Returns(CreateResult(attempted: false, succeeded: false));
        var runner = CreateRunner(configurationService);

        await runner.RunOnceAsync(CancellationToken.None);

        await configurationService.Received(1)
            .ReconcileDeploymentProviderAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunOnceAsync_WhenReconciliationFails_DoesNotThrow()
    {
        var configurationService = Substitute.For<IAuthorizationProviderConfigurationService>();
        configurationService.ReconcileDeploymentProviderAsync(Arg.Any<CancellationToken>())
            .Returns(CreateResult(attempted: true, succeeded: false));
        var runner = CreateRunner(configurationService);

        await runner.RunOnceAsync(CancellationToken.None);

        await configurationService.Received(1)
            .ReconcileDeploymentProviderAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunOnceAsync_WhenReconciliationRecovers_RetriesUntilSuccess()
    {
        var configurationService = Substitute.For<IAuthorizationProviderConfigurationService>();
        configurationService.ReconcileDeploymentProviderAsync(Arg.Any<CancellationToken>())
            .Returns(
                CreateResult(attempted: true, succeeded: false),
                CreateResult(attempted: true, succeeded: true));
        var runner = CreateRunner(configurationService, new CerbosPolicyBootSyncOptions
        {
            InitialDelaySeconds = 0,
            RetryDelaySeconds = 0,
            MaxAttempts = 2,
            TimeoutSeconds = 5
        });

        await runner.RunOnceAsync(CancellationToken.None);

        await configurationService.Received(2)
            .ReconcileDeploymentProviderAsync(Arg.Any<CancellationToken>());
    }

    private static CerbosPolicyBootSyncRunner CreateRunner(
        IAuthorizationProviderConfigurationService configurationService,
        CerbosPolicyBootSyncOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => configurationService);
        var serviceProvider = services.BuildServiceProvider();

        return new CerbosPolicyBootSyncRunner(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options ?? new CerbosPolicyBootSyncOptions
            {
                InitialDelaySeconds = 0,
                RetryDelaySeconds = 0,
                MaxAttempts = 1,
                TimeoutSeconds = 5
            }),
            new AuthorizationProviderBootstrapState(),
            Substitute.For<ILogger<CerbosPolicyBootSyncRunner>>());
    }

    private static AuthorizationProviderReconciliationResult CreateResult(bool attempted, bool succeeded)
    {
        return new AuthorizationProviderReconciliationResult(
            Attempted: attempted,
            Succeeded: succeeded,
            EndpointVerified: succeeded,
            PoliciesSynchronized: succeeded,
            Message: succeeded ? "ready" : "not ready");
    }
}
