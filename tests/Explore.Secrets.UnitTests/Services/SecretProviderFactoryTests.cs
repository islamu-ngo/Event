using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Explore.Secrets.Providers;
using Explore.Secrets.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using TUnit.Core;

namespace Explore.Secrets.UnitTests.Services;

public class SecretProviderFactoryTests
{
    private readonly ILoggerFactory _loggerFactory;

    public SecretProviderFactoryTests()
    {
        _loggerFactory = Substitute.For<ILoggerFactory>();
        _loggerFactory.CreateLogger(Arg.Any<string>())
            .Returns(Substitute.For<ILogger>());
        _loggerFactory.CreateLogger<EnvironmentSecretProvider>()
            .Returns(Substitute.For<ILogger<EnvironmentSecretProvider>>());
        _loggerFactory.CreateLogger<InfisicalSecretProvider>()
            .Returns(Substitute.For<ILogger<InfisicalSecretProvider>>());
        _loggerFactory.CreateLogger<SecretProviderFactory>()
            .Returns(Substitute.For<ILogger<SecretProviderFactory>>());
    }

    [Test]
    public async Task Create_WhenProviderTypeIsNone_ShouldReturnEnvironmentProvider()
    {
        // Arrange
        var options = Options.Create(new SecretProviderOptions
        {
            Provider = SecretProviderType.Environment
        });
        var factory = new SecretProviderFactory(options, _loggerFactory);

        // Act
        var provider = factory.Create();

        // Assert
        await Assert.That(provider).IsTypeOf<EnvironmentSecretProvider>();
        await Assert.That(provider.ProviderType).IsEqualTo(SecretProviderType.Environment);
    }

    [Test]
    public async Task Create_WhenProviderTypeIsInfisical_ShouldReturnInfisicalProvider()
    {
        // Arrange
        var options = Options.Create(new SecretProviderOptions
        {
            Provider = SecretProviderType.Infisical
        });
        var factory = new SecretProviderFactory(options, _loggerFactory);

        // Act
        var provider = factory.Create();

        // Assert
        await Assert.That(provider).IsTypeOf<InfisicalSecretProvider>();
        await Assert.That(provider.ProviderType).IsEqualTo(SecretProviderType.Infisical);
        await Assert.That(provider.SupportsRefresh).IsTrue();
    }

    [Test]
    public async Task Create_WhenProviderTypeIsUnspecified_ShouldFailClosed()
    {
        // Arrange
        var options = Options.Create(new SecretProviderOptions
        {
            Provider = SecretProviderType.Unspecified
        });
        var factory = new SecretProviderFactory(options, _loggerFactory);

        // Act
        var act = () => factory.Create();

        // Assert
        var exception = await Assert.That(act).Throws<SecretProviderException>();
        await Assert.That(exception!.ProviderType).IsEqualTo(SecretProviderType.Unspecified);
    }
}
