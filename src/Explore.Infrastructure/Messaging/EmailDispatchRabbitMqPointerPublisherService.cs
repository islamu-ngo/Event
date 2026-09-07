using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Messaging;

public sealed class EmailDispatchRabbitMqPointerPublisherService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<EmailDispatchRabbitMqSettings> settings,
    ILogger<EmailDispatchRabbitMqPointerPublisherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting RabbitMQ EmailDispatch pointer publisher");

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = settings.CurrentValue;
            if (options.Enabled)
            {
                await PublishOnceAsync(stoppingToken);
            }

            try
            {
                await Task.Delay(GetPollingInterval(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task PublishOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            var publisher = scope.ServiceProvider.GetRequiredService<EmailDispatchRabbitMqPointerPublisher>();
            EmailDispatchRabbitMqPointerPublisherResult result = await publisher.PublishDuePointersAsync(stoppingToken);
            if (result.EligibleCount > 0)
            {
                logger.LogInformation(
                    "RabbitMQ EmailDispatch pointer publisher processed {EligibleCount} rows: confirmed={ConfirmedCount}, failed={FailedCount}, skipped={SkippedCount}",
                    result.EligibleCount,
                    result.ConfirmedCount,
                    result.FailedCount,
                    result.SkippedCount);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RabbitMQ EmailDispatch pointer publisher pass failed");
        }
    }

    private TimeSpan GetPollingInterval() =>
        TimeSpan.FromSeconds(Math.Max(1, settings.CurrentValue.PublisherPollingIntervalSeconds));
}
