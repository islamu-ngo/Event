using Explore.API.Hosting;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.AiAssistant.Requests.Commands;

namespace Explore.API.BackgroundServices;

public sealed class AiAssistantRunWorker(
    IAiAssistantRunQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<AiAssistantRunWorker> logger,
    AgentBrowserResetCoordinator? agentDatabase = null) : BackgroundService
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
        using var work = agentDatabase is null ? null : await agentDatabase.EnterAsync(stoppingToken);
        // An item may have been dequeued before maintenance but not yet admitted. Never execute that
        // pointer after purge, including when its continuation resumes only after readiness reopens.
        if (agentDatabase is not null && item.AgentDatabaseGeneration != agentDatabase.Generation) return;
        await using var scope = scopeFactory.CreateAsyncScope();
        var tenantAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        tenantAccessor.SetTenant(item.TenantId);

        try
        {
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ProcessAiRunCommand>>();
            await handler.ExecuteAsync(new ProcessAiRunCommand
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
