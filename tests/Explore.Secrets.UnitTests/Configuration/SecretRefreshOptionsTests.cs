using Explore.Secrets.Configuration;
using TUnit.Core;

namespace Explore.Secrets.UnitTests.Configuration;

public class SecretRefreshOptionsTests
{
    [Test]
    public async Task CalculateBackoffDelay_WhenZeroFailures_ShouldReturnZero()
    {
        // Arrange
        var options = new SecretRefreshOptions();

        // Act
        var delay = options.CalculateBackoffDelay(0);

        // Assert
        await Assert.That(delay).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task CalculateBackoffDelay_WhenOneFailure_ShouldReturnBaseDelay()
    {
        // Arrange
        var options = new SecretRefreshOptions
        {
            BaseBackoffDelay = TimeSpan.FromSeconds(5),
            JitterFactor = 0 // No jitter for predictable test
        };

        // Act
        var delay = options.CalculateBackoffDelay(1);

        // Assert
        await Assert.That(delay).IsEqualTo(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task CalculateBackoffDelay_ShouldDoubleWithEachFailure()
    {
        // Arrange
        var options = new SecretRefreshOptions
        {
            BaseBackoffDelay = TimeSpan.FromSeconds(5),
            MaxBackoffDelay = TimeSpan.FromMinutes(10),
            JitterFactor = 0 // No jitter for predictable test
        };

        // Act & Assert
        await Assert.That(options.CalculateBackoffDelay(1)).IsEqualTo(TimeSpan.FromSeconds(5));  // 5 * 2^0 = 5
        await Assert.That(options.CalculateBackoffDelay(2)).IsEqualTo(TimeSpan.FromSeconds(10)); // 5 * 2^1 = 10
        await Assert.That(options.CalculateBackoffDelay(3)).IsEqualTo(TimeSpan.FromSeconds(20)); // 5 * 2^2 = 20
        await Assert.That(options.CalculateBackoffDelay(4)).IsEqualTo(TimeSpan.FromSeconds(40)); // 5 * 2^3 = 40
    }

    [Test]
    public async Task CalculateBackoffDelay_ShouldCapAtMaxDelay()
    {
        // Arrange
        var options = new SecretRefreshOptions
        {
            BaseBackoffDelay = TimeSpan.FromSeconds(5),
            MaxBackoffDelay = TimeSpan.FromSeconds(30),
            JitterFactor = 0 // No jitter for predictable test
        };

        // Act
        var delay = options.CalculateBackoffDelay(10); // Would be 5 * 2^9 = 2560s without cap

        // Assert
        await Assert.That(delay).IsEqualTo(TimeSpan.FromSeconds(30));
    }

    [Test]
    public async Task CalculateBackoffDelay_WithJitter_ShouldAddRandomness()
    {
        // Arrange
        var options = new SecretRefreshOptions
        {
            BaseBackoffDelay = TimeSpan.FromSeconds(10),
            MaxBackoffDelay = TimeSpan.FromMinutes(5),
            JitterFactor = 0.1 // 10% jitter
        };

        // Act - Call multiple times to check randomness
        var delays = Enumerable.Range(0, 10)
            .Select(_ => options.CalculateBackoffDelay(1))
            .ToList();

        // Assert - Should all be between 10s and 11s (base + up to 10% jitter)
        foreach (var delay in delays)
        {
            await Assert.That(delay.TotalSeconds).IsGreaterThanOrEqualTo(10);
            await Assert.That(delay.TotalSeconds).IsLessThanOrEqualTo(11);
        }

        // At least some variance should exist (not all identical)
        await Assert.That(delays.Distinct().Count()).IsGreaterThan(1);
    }

    [Test]
    public async Task AddJitter_ShouldAddRandomnessToInterval()
    {
        // Arrange
        var options = new SecretRefreshOptions
        {
            JitterFactor = 0.1 // 10% jitter
        };
        var baseInterval = TimeSpan.FromSeconds(100);

        // Act - Call multiple times
        var intervals = Enumerable.Range(0, 10)
            .Select(_ => options.AddJitter(baseInterval))
            .ToList();

        // Assert - Should all be between 100s and 110s
        foreach (var interval in intervals)
        {
            await Assert.That(interval.TotalSeconds).IsGreaterThanOrEqualTo(100);
            await Assert.That(interval.TotalSeconds).IsLessThanOrEqualTo(110);
        }
    }

    [Test]
    public async Task DefaultValues_ShouldBeReasonable()
    {
        // Arrange & Act
        var options = new SecretRefreshOptions();

        // Assert
        await Assert.That(options.Enabled).IsTrue();
        await Assert.That(options.RefreshInterval).IsEqualTo(TimeSpan.FromMinutes(5));
        await Assert.That(options.InitialDelay).IsEqualTo(TimeSpan.FromSeconds(10));
        await Assert.That(options.BaseBackoffDelay).IsEqualTo(TimeSpan.FromSeconds(5));
        await Assert.That(options.MaxBackoffDelay).IsEqualTo(TimeSpan.FromMinutes(5));
        await Assert.That(options.JitterFactor).IsEqualTo(0.1);
        await Assert.That(options.UnhealthyThreshold).IsEqualTo(3);
    }
}
