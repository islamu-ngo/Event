using Explore.Blazor.Extensions;
using Explore.Blazor.HealthChecks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class DataProtectionKeyStoreHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsync_WhenKeyStoreIsReachable_ReturnsHealthyWithSafeData()
    {
        var database = Substitute.For<IDatabase>();
        database.PingAsync(Arg.Any<CommandFlags>()).Returns(TimeSpan.FromMilliseconds(3));
        database.KeyExistsAsync(
                (RedisKey)BffDataProtectionExtensions.KeyRingName,
                Arg.Any<CommandFlags>())
            .Returns(true);
        var connectionMultiplexer = Substitute.For<IConnectionMultiplexer>();
        connectionMultiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(database);

        var healthCheck = new DataProtectionKeyStoreHealthCheck(
            [connectionMultiplexer],
            new EphemeralDataProtectionProvider(),
            NullLogger<DataProtectionKeyStoreHealthCheck>.Instance);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Data["keyRingPresent"]).IsEqualTo(true);
        await Assert.That(result.Data["store"]).IsEqualTo("redis");
        await Assert.That(result.Data.Keys).DoesNotContain("xml");
        await Assert.That(result.Data.Keys).DoesNotContain("connectionString");
    }

    [Test]
    public async Task CheckHealthAsync_WhenKeyStoreIsMissing_ReturnsUnhealthyWithSafeFailureType()
    {
        var connectionMultiplexer = Substitute.For<IConnectionMultiplexer>();
        connectionMultiplexer
            .GetDatabase(Arg.Any<int>(), Arg.Any<object?>())
            .Returns(_ => throw new InvalidOperationException("Redis unavailable"));
        var healthCheck = new DataProtectionKeyStoreHealthCheck(
            [connectionMultiplexer],
            new EphemeralDataProtectionProvider(),
            NullLogger<DataProtectionKeyStoreHealthCheck>.Instance);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Description).IsEqualTo("Data Protection key store is unreachable.");
        await Assert.That(result.Data["failureType"]).IsEqualTo(nameof(InvalidOperationException));
        await Assert.That(result.Data.Keys).DoesNotContain("connectionString");
    }

    [Test]
    public async Task CheckHealthAsync_WhenRedisIsNotConfigured_ValidatesLocalDataProtection()
    {
        var healthCheck = new DataProtectionKeyStoreHealthCheck(
            [],
            new EphemeralDataProtectionProvider(),
            NullLogger<DataProtectionKeyStoreHealthCheck>.Instance);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Description).IsEqualTo("Local Data Protection key store is usable.");
        await Assert.That(result.Data["store"]).IsEqualTo("local");
        await Assert.That(result.Data.Keys).DoesNotContain("xml");
        await Assert.That(result.Data.Keys).DoesNotContain("connectionString");
    }
}
