using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Explore.Infrastructure;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public class DeploymentModeProviderTests
{
    [Test]
    public async Task GetConfiguredOnboardingModeAsync_WhenDeploymentModeAbsent_ReturnsSingleTenant()
    {
        var provider = CreateProvider(new Dictionary<string, string?>());

        var mode = await provider.GetConfiguredOnboardingModeAsync();

        await Assert.That(mode).IsEqualTo(DeploymentMode.SingleTenant);
    }

    [Test]
    public async Task GetConfiguredOnboardingModeAsync_WhenDeploymentModeIsMultiTenantWithUnderscore_ReturnsMultiTenant()
    {
        var provider = CreateProvider(new Dictionary<string, string?>
        {
            ["Deployment:Mode"] = "multi_tenant"
        });

        var mode = await provider.GetConfiguredOnboardingModeAsync();

        await Assert.That(mode).IsEqualTo(DeploymentMode.MultiTenant);
    }

    [Test]
    public async Task GetConfiguredOnboardingModeAsync_WhenDeploymentModeIsSingleTenantWithHyphen_ReturnsSingleTenant()
    {
        var provider = CreateProvider(new Dictionary<string, string?>
        {
            ["Deployment:Mode"] = "single-tenant"
        });

        var mode = await provider.GetConfiguredOnboardingModeAsync();

        await Assert.That(mode).IsEqualTo(DeploymentMode.SingleTenant);
    }

    [Test]
    public async Task GetConfiguredOnboardingModeAsync_WhenDeploymentModeInvalid_ReturnsSingleTenantFallback()
    {
        var provider = CreateProvider(new Dictionary<string, string?>
        {
            ["Deployment:Mode"] = "hosted_platform"
        });

        var mode = await provider.GetConfiguredOnboardingModeAsync();

        await Assert.That(mode).IsEqualTo(DeploymentMode.SingleTenant);
    }

    private static DeploymentModeProvider CreateProvider(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var settings = Substitute.For<IOptionsMonitor<DeploymentSettings>>();
        settings.CurrentValue.Returns(new DeploymentSettings { Mode = DeploymentMode.SingleTenant });

        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IInstanceBootstrapStateRepository>());
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new DeploymentModeProvider(
            settings,
            configuration,
            Substitute.For<IDistributedCache>(),
            scopeFactory,
            Substitute.For<ILogger<DeploymentModeProvider>>());
    }
}
