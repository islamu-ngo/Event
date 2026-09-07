using Explore.Application.Contracts.Persistence;
using Explore.Infrastructure.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Payments;

public sealed class PaymentReconciliationHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsync_ReportsOnlyBoundedAggregateFields()
    {
        var repository = Substitute.For<IRegistrationPaymentAttemptRepository>();
        repository.GetReconciliationHealthAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentReconciliationHealth(3, 2, 1, new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc), 2));
        var services = new ServiceCollection();
        services.AddScoped(_ => repository);
        await using ServiceProvider provider = services.BuildServiceProvider();
        var healthCheck = new PaymentReconciliationHealthCheck(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System);

        HealthCheckResult result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Data.Keys).IsEquivalentTo(["due", "unknown", "parked", "configurationBlocked", "duplicateSucceededOrders", "code", "oldestDueAtUtc"]);
        await Assert.That(result.Data["code"]).IsEqualTo("payment_provider_configuration_blocked");
        string rendered = string.Join('|', result.Data.Select(pair => $"{pair.Key}:{pair.Value}"));
        await Assert.That(rendered).DoesNotContain("tenant");
        await Assert.That(rendered).DoesNotContain("acct_");
        await Assert.That(rendered).DoesNotContain("evt_");
        await Assert.That(rendered).DoesNotContain("pi_");
    }

    [Test]
    public async Task CheckHealthAsync_DuplicateSucceededOrdersAreDegradedWithBoundedCode()
    {
        var repository = Substitute.For<IRegistrationPaymentAttemptRepository>();
        repository.GetReconciliationHealthAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentReconciliationHealth(0, 0, 0, null, 0, 1));
        var services = new ServiceCollection();
        services.AddScoped(_ => repository);
        await using ServiceProvider provider = services.BuildServiceProvider();
        var healthCheck = new PaymentReconciliationHealthCheck(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System);

        HealthCheckResult result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Data["duplicateSucceededOrders"]).IsEqualTo(1);
        await Assert.That(result.Data["code"]).IsEqualTo("payment_duplicate_succeeded_observations");
    }
}
