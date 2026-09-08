using Explore.Infrastructure.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class AiProviderEnvironmentTests
{
    [Test]
    [Arguments("Development")]
    [Arguments("Testing")]
    public async Task ConfigureInfrastructureServices_InSafeEnvironment_RegistersAndAcceptsFakeProvider(
        string environmentName)
    {
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder().Build();
        IHostEnvironment environment = CreateEnvironment(environmentName);

        services.ConfigureInfrastructureServices(configuration, environment);

        using ServiceProvider provider = services.BuildServiceProvider();
        ValidateOptionsResult validation = provider.GetRequiredService<AiProviderSettingsValidator>()
            .Validate(null, CreateFakeSettings());
        await Assert.That(services.Any(IsFakeProviderRegistration)).IsTrue();
        await Assert.That(validation.Succeeded).IsTrue();
    }

    [Test]
    [Arguments("Production")]
    [Arguments("Staging")]
    public async Task ConfigureInfrastructureServices_InProductionLikeEnvironment_ExcludesAndRejectsFakeProvider(
        string environmentName)
    {
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder().Build();
        IHostEnvironment environment = CreateEnvironment(environmentName);

        services.ConfigureInfrastructureServices(configuration, environment);

        using ServiceProvider provider = services.BuildServiceProvider();
        ValidateOptionsResult validation = provider.GetRequiredService<AiProviderSettingsValidator>()
            .Validate(null, CreateFakeSettings());
        await Assert.That(services.Any(IsFakeProviderRegistration)).IsFalse();
        await Assert.That(validation.Succeeded).IsFalse();
    }

    private static bool IsFakeProviderRegistration(ServiceDescriptor descriptor) =>
        descriptor.ServiceType == typeof(FakeAiChatProvider)
        || descriptor.ImplementationType == typeof(FakeAiProviderStrategy);

    private static IHostEnvironment CreateEnvironment(string environmentName)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        return environment;
    }

    private static AiProviderSettings CreateFakeSettings() => new()
    {
        Enabled = true,
        Provider = AiProviderSettings.ProviderFake
    };
}
