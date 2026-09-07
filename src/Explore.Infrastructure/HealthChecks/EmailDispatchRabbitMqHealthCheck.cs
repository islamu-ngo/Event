using Explore.Application.Contracts.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Explore.Infrastructure.HealthChecks;

public sealed class EmailDispatchRabbitMqHealthCheck(IEmailDispatchTransport transport) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        EmailDispatchTransportHealth health = await transport.CheckHealthAsync(cancellationToken);

        return health.Healthy
            ? HealthCheckResult.Healthy(health.Description, health.Data)
            : HealthCheckResult.Unhealthy(health.Description, data: health.Data);
    }
}
