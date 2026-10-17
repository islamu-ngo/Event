using Microsoft.Extensions.Hosting;

namespace Explore.Persistence.Database;

/// <summary>Rejects incomplete advertised profiles; startup never installs roles or functions.</summary>
public sealed class InstanceApiKeyCapabilityAdmissionService(InstanceApiKeyDbContextFactories factories) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => factories.RequireReadyAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
