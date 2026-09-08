using Explore.Application.Contracts.Services;

namespace Explore.API.BackgroundServices;

public sealed class EmailDispatchHostedDrainRunner(IServiceProvider serviceProvider)
{
    public async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var drainService = scope.ServiceProvider.GetRequiredService<IEmailDispatchDrainService>();

        await drainService.RecoverStaleProcessingAsync(stoppingToken);
        await drainService.ProcessBatchAsync(stoppingToken);
    }
}
