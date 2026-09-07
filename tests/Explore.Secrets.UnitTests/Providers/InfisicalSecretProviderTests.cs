// Note: Does not test actual Infisical SDK calls (requires integration tests).

using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Explore.Secrets.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using TUnit.Core;

namespace Explore.Secrets.UnitTests.Providers;

public class InfisicalSecretProviderTests
{
    private readonly ILogger<InfisicalSecretProvider> _logger;

    public InfisicalSecretProviderTests()
    {
        _logger = Substitute.For<ILogger<InfisicalSecretProvider>>();
    }

    private InfisicalSecretProvider CreateProvider(InfisicalOptions? infisicalOptions = null)
    {
        var options = new SecretProviderOptions
        {
            Provider = SecretProviderType.Infisical,
            Infisical = infisicalOptions ?? new InfisicalOptions
            {
                Url = "https://infisical.example.com",
                ProjectId = "test-project-id",
                ClientId = "test-client-id",
                ClientSecret = SecretsTestValues.CreateSecret(),
                Environment = "dev"
            }
        };

        return new InfisicalSecretProvider(_logger, Options.Create(options));
    }

    [Test]
    public async Task ProviderType_ShouldReturnInfisical()
    {
        // Arrange
        var provider = CreateProvider();

        // Act & Assert
        await Assert.That(provider.ProviderType).IsEqualTo(SecretProviderType.Infisical);
    }

    [Test]
    public async Task SupportsRefresh_ShouldReturnTrue()
    {
        // Arrange
        var provider = CreateProvider();

        // Act & Assert
        await Assert.That(provider.SupportsRefresh).IsTrue();
    }

    [Test]
    public async Task InitializeAsync_WhenProjectIdMissing_ShouldThrowSecretProviderException()
    {
        // Arrange
        var provider = CreateProvider(new InfisicalOptions
        {
            Url = "https://infisical.example.com",
            ProjectId = null,
            ClientId = "test-client-id",
            ClientSecret = SecretsTestValues.CreateSecret()
        });

        // Act
        var act = () => provider.InitializeAsync();

        // Assert
        var exception = await Assert.That(act).Throws<SecretProviderException>();
        await Assert.That(exception!.ProviderType).IsEqualTo(SecretProviderType.Infisical);
        await Assert.That(exception.Operation).IsEqualTo("Initialize");
        await Assert.That(exception.IsTransient).IsFalse();
    }

    [Test]
    public async Task InitializeAsync_WhenClientIdMissing_ShouldThrowSecretProviderException()
    {
        // Arrange
        var provider = CreateProvider(new InfisicalOptions
        {
            Url = "https://infisical.example.com",
            ProjectId = "test-project",
            ClientId = null,
            ClientSecret = SecretsTestValues.CreateSecret()
        });

        // Act
        var act = () => provider.InitializeAsync();

        // Assert
        var exception = await Assert.That(act).Throws<SecretProviderException>();
        await Assert.That(exception!.ProviderType).IsEqualTo(SecretProviderType.Infisical);
        await Assert.That(exception.Operation).IsEqualTo("Initialize");
        await Assert.That(exception.IsTransient).IsFalse();
    }

    [Test]
    public async Task InitializeAsync_WhenClientSecretMissing_ShouldThrowSecretProviderException()
    {
        // Arrange
        var provider = CreateProvider(new InfisicalOptions
        {
            Url = "https://infisical.example.com",
            ProjectId = "test-project",
            ClientId = "test-client-id",
            ClientSecret = null
        });

        // Act
        var act = () => provider.InitializeAsync();

        // Assert
        var exception = await Assert.That(act).Throws<SecretProviderException>();
        await Assert.That(exception!.ProviderType).IsEqualTo(SecretProviderType.Infisical);
        await Assert.That(exception.Operation).IsEqualTo("Initialize");
        await Assert.That(exception.IsTransient).IsFalse();
    }

    [Test]
    public async Task GetSecretAsync_WhenNotInitialized_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var provider = CreateProvider();

        // Act
        var act = () => provider.GetSecretAsync("SomeKey");

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>()
            .WithMessageContaining("not initialized");
    }

    [Test]
    public async Task GetSecretsByPathAsync_WhenNotInitialized_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var provider = CreateProvider();

        // Act
        Func<Task<IReadOnlyDictionary<string, string>?>> act =
            async () => await provider.GetSecretsByPathAsync("/api");

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>()
            .WithMessageContaining("not initialized");
    }

    [Test]
    public async Task RefreshAsync_WhenNotInitialized_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var provider = CreateProvider();

        // Act
        var act = () => provider.RefreshAsync();

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>()
            .WithMessageContaining("not initialized");
    }

    [Test]
    public async Task GetHealthAsync_WhenNotInitialized_ShouldReturnUnhealthy()
    {
        // Arrange
        var provider = CreateProvider();

        // Act
        var health = await provider.GetHealthAsync();

        // Assert
        await Assert.That(health.IsHealthy).IsFalse();
        await Assert.That(health.ProviderType).IsEqualTo(SecretProviderType.Infisical);
        await Assert.That(health.LastSuccessfulRefresh).IsNull();
        await Assert.That(health.ConsecutiveFailures).IsEqualTo(0);
    }

    [Test]
    public async Task DisposeAsync_ShouldNotThrow()
    {
        // Arrange
        var provider = CreateProvider();

        // Act
        var act = async () => await provider.DisposeAsync();

        // Assert
        await Assert.That(act).ThrowsNothing();
    }
}
