using Explore.Application.Contracts.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Explore.Infrastructure.HealthChecks;

public sealed class PaymentReconciliationHealthCheck(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IRegistrationPaymentAttemptRepository repository = scope.ServiceProvider.GetRequiredService<IRegistrationPaymentAttemptRepository>();
            PaymentReconciliationHealth health = await repository.GetReconciliationHealthAsync(
                timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken);
            var data = new Dictionary<string, object>
            {
                ["due"] = health.Due,
                ["unknown"] = health.Unknown,
                ["parked"] = health.Parked,
                ["configurationBlocked"] = health.ConfigurationBlocked,
                ["duplicateSucceededOrders"] = health.DuplicateSucceededOrders,
                ["code"] = health.DuplicateSucceededOrders > 0
                    ? SucceededPaymentLookupResult.DuplicateCode
                    : health.ConfigurationBlocked > 0 ? "payment_provider_configuration_blocked" : string.Empty,
                ["oldestDueAtUtc"] = health.OldestDueAt?.ToString("O") ?? string.Empty
            };
            return health.Due >= 100 || health.Parked > 0 || health.ConfigurationBlocked > 0 || health.DuplicateSucceededOrders > 0
                ? HealthCheckResult.Degraded("Payment reconciliation requires operator attention.", data: data)
                : HealthCheckResult.Healthy("Payment reconciliation is healthy.", data: data);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("Payment reconciliation readiness query failed.");
        }
    }
}
