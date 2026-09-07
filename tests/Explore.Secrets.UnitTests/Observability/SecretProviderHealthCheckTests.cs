using Explore.Secrets.Abstractions;
using Explore.Secrets.Observability;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TUnit.Core;

namespace Explore.Secrets.UnitTests.Observability;

public class SecretProviderHealthCheckTests : IDisposable
{
    private readonly ISecretProvider _provider;
    private readonly ILogger<SecretProviderHealthCheck> _logger;
    private readonly SecretRefreshMetrics _metrics;
    private readonly SecretProviderHealthCheck _healthCheck;

    public SecretProviderHealthCheckTests()
    {
        _provider = Substitute.For<ISecretProvider>();
        _logger = Substitute.For<ILogger<SecretProviderHealthCheck>>();
        var clock = new SecretsFixedTimeProvider();
        _metrics = new SecretRefreshMetrics(clock: clock);
        _healthCheck = new SecretProviderHealthCheck(
            _provider,
            _logger,
            _metrics,
            clock);
    }

    public void Dispose()
    {
        _metrics.Dispose();
    }

    [Test]
    public async Task CheckHealthAsync_WhenProviderHealthy_ShouldReturnHealthy()
    {
        // Arrange
        _provider.GetHealthAsync(Arg.Any<CancellationToken>())
            .Returns(new ProviderHealthInfo(
                ProviderType: SecretProviderType.Infisical,
                IsHealthy: true,
                ConsecutiveFailures: 0,
                LastSuccessfulRefresh: SecretsTestValues.UtcNow,
                ErrorMessage: null));

        _provider.SupportsRefresh.Returns(true);

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Description).Contains("Infisical");
        await Assert.That(result.Description).Contains("healthy");
    }

    [Test]
    public async Task CheckHealthAsync_WhenProviderDegraded_ShouldReturnDegraded()
    {
        // Arrange
        _provider.GetHealthAsync(Arg.Any<CancellationToken>())
            .Returns(new ProviderHealthInfo(
                ProviderType: SecretProviderType.Environment,
                IsHealthy: false,
                ConsecutiveFailures: 2, // Less than 3 = degraded
                LastSuccessfulRefresh: SecretsTestValues.UtcNow.AddMinutes(-5),
                ErrorMessage: "Temporary connection issue"));

        _provider.SupportsRefresh.Returns(true);

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Description).Contains("transient resolution failures");
        await Assert.That(result.Description).DoesNotContain("Temporary connection issue");
    }

    [Test]
    public async Task CheckHealthAsync_WhenProviderUnhealthy_ShouldReturnUnhealthy()
    {
        // Arrange
        _provider.GetHealthAsync(Arg.Any<CancellationToken>())
            .Returns(new ProviderHealthInfo(
                ProviderType: SecretProviderType.Infisical,
                IsHealthy: false,
                ConsecutiveFailures: 5, // 3+ = unhealthy
                LastSuccessfulRefresh: SecretsTestValues.UtcNow.AddMinutes(-30),
                ErrorMessage: "Authentication failed"));

        _provider.SupportsRefresh.Returns(true);

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Description).Contains("5 consecutive failures");
        await Assert.That(result.Description).DoesNotContain("Authentication failed");
    }

    [Test]
    public async Task CheckHealthAsync_WhenProviderThrows_ShouldReturnUnhealthy()
    {
        // Arrange
        _provider.GetHealthAsync(Arg.Any<CancellationToken>())
            .Returns<Task<ProviderHealthInfo>>(x => throw new InvalidOperationException("Provider crashed"));

        _provider.ProviderType.Returns(SecretProviderType.Environment);

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Exception).IsNull();
        await Assert.That(result.Description).IsEqualTo("Secret provider health check failed.");
    }

    [Test]
    public async Task CheckHealthAsync_ShouldIncludeProviderTypeInData()
    {
        // Arrange
        _provider.GetHealthAsync(Arg.Any<CancellationToken>())
            .Returns(new ProviderHealthInfo(
                ProviderType: SecretProviderType.Infisical,
                IsHealthy: true,
                ConsecutiveFailures: 0,
                LastSuccessfulRefresh: SecretsTestValues.UtcNow,
                ErrorMessage: null));

        _provider.SupportsRefresh.Returns(true);

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        await Assert.That(result.Data.ContainsKey("provider")).IsTrue();
        await Assert.That(result.Data["provider"]).IsEqualTo("Infisical");
    }

    [Test]
    public async Task CheckHealthAsync_ShouldIncludeSupportsRefreshInData()
    {
        // Arrange
        _provider.GetHealthAsync(Arg.Any<CancellationToken>())
            .Returns(new ProviderHealthInfo(
                ProviderType: SecretProviderType.Environment,
                IsHealthy: true,
                ConsecutiveFailures: 0,
                LastSuccessfulRefresh: null,
                ErrorMessage: null));

        _provider.SupportsRefresh.Returns(false);

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        await Assert.That(result.Data.ContainsKey("supportsRefresh")).IsTrue();
        await Assert.That(result.Data["supportsRefresh"]).IsEqualTo(false);
    }

    [Test]
    public async Task CheckHealthAsync_ShouldIncludeLastRefreshTimestamp()
    {
        // Arrange
        var lastRefresh = SecretsTestValues.UtcNow.AddMinutes(-10);
        _provider.GetHealthAsync(Arg.Any<CancellationToken>())
            .Returns(new ProviderHealthInfo(
                ProviderType: SecretProviderType.Environment,
                IsHealthy: true,
                ConsecutiveFailures: 0,
                LastSuccessfulRefresh: lastRefresh,
                ErrorMessage: null));

        _provider.SupportsRefresh.Returns(true);

        // Act
        var result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        await Assert.That(result.Data.ContainsKey("lastSuccessfulRefresh")).IsTrue();
        await Assert.That(result.Data.ContainsKey("secondsSinceLastRefresh")).IsTrue();
    }

    [Test]
    public async Task Name_ShouldBeSecretProvider()
    {
        await Assert.That(SecretProviderHealthCheck.Name).IsEqualTo("secret_provider");
    }

    [Test]
    public async Task Tag_ShouldBeSecrets()
    {
        await Assert.That(SecretProviderHealthCheck.Tag).IsEqualTo("secrets");
    }

    [Test]
    public async Task CheckHealthAsync_WithoutMetrics_ShouldStillWork()
    {
        // Arrange - Create health check without metrics
        var healthCheckWithoutMetrics = new SecretProviderHealthCheck(_provider, _logger, metrics: null);

        _provider.GetHealthAsync(Arg.Any<CancellationToken>())
            .Returns(new ProviderHealthInfo(
                ProviderType: SecretProviderType.Infisical,
                IsHealthy: true,
                ConsecutiveFailures: 0,
                LastSuccessfulRefresh: SecretsTestValues.UtcNow,
                ErrorMessage: null));

        _provider.SupportsRefresh.Returns(true);

        // Act
        var result = await healthCheckWithoutMetrics.CheckHealthAsync(new HealthCheckContext());

        // Assert
        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
    }
}
