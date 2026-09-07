using Explore.Application.Contracts.Services;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using MediatR;

namespace Explore.API.BackgroundServices;

public sealed class AiAssistantRunWorker(
    IAiAssistantRunQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<AiAssistantRunWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in queue.ReadAllAsync(stoppingToken))
        {
            await ProcessAsync(item, stoppingToken);
        }
    }

    private async Task ProcessAsync(AiAssistantRunQueueItem item, CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var tenantAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        tenantAccessor.SetTenant(item.TenantId);

        try
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.Send(new ProcessAiRunCommand
            {
                TenantId = item.TenantId,
                ConversationId = item.ConversationId,
                RunId = item.RunId,
                Mode = item.Mode
            }, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown owns cancellation. Any in-progress run will be released by stale-run recovery.
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "AI assistant background run processing failed for run {RunId}.",
                item.RunId);
        }
        finally
        {
            tenantAccessor.Clear();
        }
    }
}
