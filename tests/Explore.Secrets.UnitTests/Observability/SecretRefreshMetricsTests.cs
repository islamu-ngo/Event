using Explore.Secrets.Abstractions;
using Explore.Secrets.Observability;
using TUnit.Core;

namespace Explore.Secrets.UnitTests.Observability;

public class SecretRefreshMetricsTests : IDisposable
{
    private readonly SecretRefreshMetrics _metrics;

    public SecretRefreshMetricsTests()
    {
        _metrics = new SecretRefreshMetrics(
            clock: new SecretsFixedTimeProvider());
    }

    public void Dispose()
    {
        _metrics.Dispose();
    }

    [Test]
    public async Task Constructor_ShouldInitializeWithZeroFailures()
    {
        await Assert.That(_metrics.ConsecutiveFailures).IsEqualTo(0);
    }

    [Test]
    public async Task Constructor_ShouldInitializeWithMinValueTimestamp()
    {
        await Assert.That(_metrics.LastSuccessfulRefresh).IsEqualTo(DateTimeOffset.MinValue);
    }

    [Test]
    public async Task RecordRefreshSuccess_ShouldUpdateLastSuccessfulRefresh()
    {
        // Arrange
        // Act
        _metrics.RecordRefreshSuccess(SecretProviderType.Infisical, 0.5);

        // Assert
        await Assert.That(_metrics.LastSuccessfulRefresh)
            .IsEqualTo(SecretsTestValues.UtcNow);
    }

    [Test]
    public async Task RecordRefreshSuccess_ShouldResetConsecutiveFailures()
    {
        // Arrange - Record some failures first
        _metrics.RecordRefreshFailure(SecretProviderType.Infisical, 0.1);
        _metrics.RecordRefreshFailure(SecretProviderType.Infisical, 0.1);
        await Assert.That(_metrics.ConsecutiveFailures).IsEqualTo(2);

        // Act
        _metrics.RecordRefreshSuccess(SecretProviderType.Infisical, 0.5);

        // Assert
        await Assert.That(_metrics.ConsecutiveFailures).IsEqualTo(0);
    }

    [Test]
    public async Task RecordRefreshFailure_ShouldIncrementConsecutiveFailures()
    {
        // Act
        _metrics.RecordRefreshFailure(SecretProviderType.Environment, 1.0);
        _metrics.RecordRefreshFailure(SecretProviderType.Environment, 1.5);
        _metrics.RecordRefreshFailure(SecretProviderType.Environment, 2.0);

        // Assert
        await Assert.That(_metrics.ConsecutiveFailures).IsEqualTo(3);
    }

    [Test]
    public async Task RecordRefreshFailure_ShouldNotUpdateLastSuccessfulRefresh()
    {
        // Arrange
        var initial = _metrics.LastSuccessfulRefresh;

        // Act
        _metrics.RecordRefreshFailure(SecretProviderType.Infisical, 0.5);

        // Assert
        await Assert.That(_metrics.LastSuccessfulRefresh).IsEqualTo(initial);
    }

    [Test]
    public async Task StartRefreshOperation_Complete_ShouldRecordSuccess()
    {
        // Arrange
        using var operation = _metrics.StartRefreshOperation(SecretProviderType.Infisical);

        // Act
        operation.Complete();

        // Assert
        await Assert.That(_metrics.ConsecutiveFailures).IsEqualTo(0);
        await Assert.That(_metrics.LastSuccessfulRefresh)
            .IsEqualTo(SecretsTestValues.UtcNow);
    }

    [Test]
    public async Task StartRefreshOperation_Fail_ShouldRecordFailure()
    {
        // Arrange
        using var operation = _metrics.StartRefreshOperation(SecretProviderType.Environment);

        // Act
        operation.Fail("timeout");

        // Assert
        await Assert.That(_metrics.ConsecutiveFailures).IsEqualTo(1);
    }

    [Test]
    public async Task StartRefreshOperation_Dispose_ShouldNotRecordMetrics()
    {
        // Arrange
        var initialTimestamp = _metrics.LastSuccessfulRefresh;
        var initialFailures = _metrics.ConsecutiveFailures;

        // Act - Only dispose, don't call Complete() or Fail()
        using (var operation = _metrics.StartRefreshOperation(SecretProviderType.Infisical))
        {
            // Let it dispose without calling Complete or Fail
        }

        // Assert - Metrics should remain unchanged
        await Assert.That(_metrics.LastSuccessfulRefresh).IsEqualTo(initialTimestamp);
        await Assert.That(_metrics.ConsecutiveFailures).IsEqualTo(initialFailures);
    }

    [Test]
    public async Task MeterName_ShouldBeExploreSecrets()
    {
        await Assert.That(SecretRefreshMetrics.MeterName).IsEqualTo("Explore.Secrets");
    }

    [Test]
    public async Task MultipleProviders_ShouldTrackIndependently()
    {
        // Act - Failures on different providers
        _metrics.RecordRefreshFailure(SecretProviderType.Infisical, 0.1);
        _metrics.RecordRefreshFailure(SecretProviderType.Environment, 0.1);

        // Assert - All failures counted together (single metrics instance)
        await Assert.That(_metrics.ConsecutiveFailures).IsEqualTo(2);
    }

    [Test]
    public async Task RecordRefreshSuccess_AfterMultipleFailures_ShouldResetAll()
    {
        // Arrange
        _metrics.RecordRefreshFailure(SecretProviderType.Infisical, 0.1);
        _metrics.RecordRefreshFailure(SecretProviderType.Infisical, 0.1);
        _metrics.RecordRefreshFailure(SecretProviderType.Infisical, 0.1);
        await Assert.That(_metrics.ConsecutiveFailures).IsEqualTo(3);

        // Act
        _metrics.RecordRefreshSuccess(SecretProviderType.Infisical, 0.5);

        // Assert
        await Assert.That(_metrics.ConsecutiveFailures).IsEqualTo(0);
    }
}
