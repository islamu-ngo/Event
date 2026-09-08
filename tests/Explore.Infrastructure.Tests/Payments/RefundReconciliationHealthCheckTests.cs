using Explore.Application.Contracts.Persistence;
using Explore.Infrastructure.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Payments;

public sealed class RefundReconciliationHealthCheckTests
{
    private static readonly DateTime ObservedAt = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task CheckHealthAsync_WhenWorkIsStale_ReportsOnlyBoundedAggregateFields()
    {
        var repository = Substitute.For<IRefundAttemptRepository>();
        repository.GetReconciliationHealthAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new RefundReconciliationHealth(3, 2, 1, 1, 1, 1, 1, 1, ObservedAt.AddMinutes(-15)));
        await using ServiceProvider provider = CreateProvider(repository);
        var healthCheck = new RefundReconciliationHealthCheck(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(ObservedAt));

        HealthCheckResult result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Data.Keys).IsEquivalentTo([
            "pending", "unknown", "requiresAction", "failed", "campaignsRequiringOperator",
            "disputesDueSoon", "disputesDueWithin72Hours", "disputesOverdue", "oldestNonTerminalAtUtc"]);
        string rendered = string.Join('|', result.Data.Select(pair => $"{pair.Key}:{pair.Value}"));
        await Assert.That(rendered).DoesNotContain("tenant");
        await Assert.That(rendered).DoesNotContain("acct_");
        await Assert.That(rendered).DoesNotContain("evt_");
        await Assert.That(rendered).DoesNotContain("pay_");
        await Assert.That(rendered).DoesNotContain("refund_");
    }

    [Test]
    public async Task CheckHealthAsync_WhenNoActionIsRequired_IsHealthy()
    {
        var repository = Substitute.For<IRefundAttemptRepository>();
        repository.GetReconciliationHealthAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new RefundReconciliationHealth(0, 0, 0, 0, 0, 0, 0, 0, null));
        await using ServiceProvider provider = CreateProvider(repository);
        var healthCheck = new RefundReconciliationHealthCheck(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(ObservedAt));

        HealthCheckResult result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
    }

    private static ServiceProvider CreateProvider(IRefundAttemptRepository repository)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => repository);
        return services.BuildServiceProvider();
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
