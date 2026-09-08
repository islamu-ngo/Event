using Explore.Secrets.Abstractions;
using Explore.Secrets.Providers;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TUnit.Core;

namespace Explore.Secrets.UnitTests.Providers;

[NotInParallel]
public class EnvironmentSecretProviderTests
{
    private readonly ILogger<EnvironmentSecretProvider> _logger;
    private readonly EnvironmentSecretProvider _provider;

    public EnvironmentSecretProviderTests()
    {
        _logger = Substitute.For<ILogger<EnvironmentSecretProvider>>();
        _provider = new EnvironmentSecretProvider(_logger);
    }

    [Test]
    public async Task ProviderType_ShouldReturnNone()
    {
        await Assert.That(_provider.ProviderType).IsEqualTo(SecretProviderType.Environment);
    }

    [Test]
    public async Task SupportsRefresh_ShouldReturnFalse()
    {
        await Assert.That(_provider.SupportsRefresh).IsFalse();
    }

    [Test]
    public async Task InitializeAsync_ShouldComplete()
    {
        await _provider.InitializeAsync();
        // Should not throw
    }

    [Test]
    public async Task GetSecretAsync_WhenNotInitialized_ShouldThrow()
    {
        var act = async () => await _provider.GetSecretAsync("Test:Key");

        await Assert.That(act).Throws<InvalidOperationException>()
            .WithMessageContaining("not initialized");
    }

    [Test]
    public async Task GetSecretAsync_WhenEnvVarExists_ShouldReturnValue()
    {
        // Arrange
        string secretValue = SecretsTestValues.CreateSecret();
        Environment.SetEnvironmentVariable("TEST__SECRET", secretValue);
        await _provider.InitializeAsync();

        try
        {
            // Act
            var result = await _provider.GetSecretAsync("Test:Secret");

            // Assert
            await Assert.That(result).IsEqualTo(secretValue);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST__SECRET", null);
        }
    }

    [Test]
    public async Task GetSecretAsync_WhenEnvVarNotExists_ShouldReturnNull()
    {
        // Arrange
        await _provider.InitializeAsync();

        // Act
        var result = await _provider.GetSecretAsync("NonExistent:Key");

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetSecretAsync_WhenOnlyLegacyCaseAliasExists_DoesNotResolveIt()
    {
        string secretValue = SecretsTestValues.CreateSecret();
        Environment.SetEnvironmentVariable("Test__Secret", secretValue);
        Environment.SetEnvironmentVariable("TEST__SECRET", null);
        await _provider.InitializeAsync();

        try
        {
            var result = await _provider.GetSecretAsync("Test:Secret");

            await Assert.That(result).IsNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("Test__Secret", null);
        }
    }

    [Test]
    public async Task GetSecretWithMetadataAsync_WhenExists_ShouldReturnSecretValue()
    {
        // Arrange
        Environment.SetEnvironmentVariable("METADATA__TEST", "test-value");
        await _provider.InitializeAsync();

        try
        {
            // Act
            var result = await _provider.GetSecretWithMetadataAsync("Metadata:Test");

            // Assert
            await Assert.That(result).IsNotNull();
            await Assert.That(result!.Value).IsEqualTo("test-value");
            await Assert.That(result.Version).IsNull(); // Env vars don't have versions
        }
        finally
        {
            Environment.SetEnvironmentVariable("METADATA__TEST", null);
        }
    }

    [Test]
    public async Task GetSecretWithMetadataAsync_WhenNotExists_ShouldReturnNull()
    {
        // Arrange
        await _provider.InitializeAsync();

        // Act
        var result = await _provider.GetSecretWithMetadataAsync("NonExistent:Key");

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetSecretsByPathAsync_ShouldReturnMatchingSecrets()
    {
        // Arrange
        Environment.SetEnvironmentVariable("PREFIX__KEY1", "value1");
        Environment.SetEnvironmentVariable("PREFIX__KEY2", "value2");
        Environment.SetEnvironmentVariable("OTHER__KEY", "other");
        await _provider.InitializeAsync();

        try
        {
            // Act
            var results = await _provider.GetSecretsByPathAsync("Prefix");

            // Assert
            await Assert.That(results.ContainsKey("Prefix:Key1")).IsTrue();
            await Assert.That(results.ContainsKey("Prefix:Key2")).IsTrue();
            await Assert.That(results.ContainsKey("Other:Key")).IsFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PREFIX__KEY1", null);
            Environment.SetEnvironmentVariable("PREFIX__KEY2", null);
            Environment.SetEnvironmentVariable("OTHER__KEY", null);
        }
    }

    [Test]
    public async Task RefreshAsync_ShouldNotThrow()
    {
        // Arrange
        await _provider.InitializeAsync();

        // Act & Assert - Refresh should complete without error
        await _provider.RefreshAsync();
    }

    [Test]
    public async Task GetHealthAsync_WhenInitialized_ShouldReturnHealthy()
    {
        // Arrange
        await _provider.InitializeAsync();

        // Act
        var health = await _provider.GetHealthAsync();

        // Assert
        await Assert.That(health.IsHealthy).IsTrue();
        await Assert.That(health.ProviderType).IsEqualTo(SecretProviderType.Environment);
        await Assert.That(health.ConsecutiveFailures).IsEqualTo(0);
    }

    [Test]
    public async Task GetHealthAsync_WhenNotInitialized_ShouldReturnUnhealthy()
    {
        // Act
        var health = await _provider.GetHealthAsync();

        // Assert
        await Assert.That(health.IsHealthy).IsFalse();
    }
}
