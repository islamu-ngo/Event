using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Webhooks;
using Explore.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Webhooks;

public sealed class WebhookBulkReplayService(
    IServiceScopeFactory scopeFactory,
    IWebhookBulkReplayPolicyResolver policyResolver,
    TimeProvider timeProvider,
    ILogger<WebhookBulkReplayService> logger) : IWebhookBulkReplayService
{
    private Guid? _lastScannedTenantId;

    public async Task<WebhookBulkReplayProcessResult> ProcessQueuedAsync(
        CancellationToken cancellationToken = default)
    {
        var completedOperations = 0;
        var scheduledTargets = 0;
        var failedOperations = 0;
        var operationsPerPass = policyResolver.Resolve().OperationsPerPass;
        Guid[] tenantIds;
        await using (var discovery = scopeFactory.CreateAsyncScope())
        {
            var directory = await discovery.ServiceProvider.GetRequiredService<ITenantLookupSource>()
                .GetTenantLookupsAsync(cancellationToken);
            tenantIds = directory.Select(tenant => tenant.TenantId).Distinct().Order()
                .Where(id => !_lastScannedTenantId.HasValue || id.CompareTo(_lastScannedTenantId.Value) > 0)
                .Take(operationsPerPass).ToArray();
        }

        // The existing nonconcurrent Quartz job owns this cursor. The directory
        // selects owners only; the native reader rechecks current tenant and queue state.
        foreach (Guid tenantId in tenantIds)
        {
            _lastScannedTenantId = tenantId;
            cancellationToken.ThrowIfCancellationRequested();
            BulkReplayExecution? execution = null;
            try
            {
                execution = await ProcessNextAsync(tenantId, cancellationToken);
                if (execution is null)
                {
                    continue;
                }

                completedOperations++;
                scheduledTargets += execution.ScheduledCount;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (BulkReplayExecutionException exception)
            {
                failedOperations++;
                logger.LogError(
                    "Webhook bulk replay execution failed. FailureCategory={FailureCategory}",
                    "bulk_replay_execution_failed");
                await MarkFailedAsync(
                    exception.TenantId,
                    exception.OperationId,
                    cancellationToken);
                break;
            }
        }
        if (tenantIds.Length < operationsPerPass)
            _lastScannedTenantId = null;

        return new WebhookBulkReplayProcessResult(
            completedOperations,
            scheduledTargets,
            failedOperations);
    }

    private async Task<BulkReplayExecution?> ProcessNextAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(tenantId);
        var repository = scope.ServiceProvider.GetRequiredService<IWebhookBulkReplayRepository>();
        var auditWriter = scope.ServiceProvider.GetRequiredService<IWebhookAuditEventWriter>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        WebhookBulkReplayOperation? selected = null;
        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                selected = await repository.GetNextQueuedForUpdateAsync(token);
                if (selected is null)
                {
                    return null;
                }

                var startedAt = timeProvider.GetUtcNow().UtcDateTime;
                selected.Start(startedAt);
                var scheduledCount = await repository.ScheduleEligibleLocalTargetsAsync(
                    selected,
                    startedAt,
                    token);
                var completedAt = timeProvider.GetUtcNow().UtcDateTime;
                selected.Complete(scheduledCount, completedAt);
                await repository.UpdateAsync(selected, token);
                await auditWriter.AppendAsync(
                    new WebhookAuditWriteRequest(
                        selected.TenantId,
                        WebhookAuditAction.BulkReplayCompleted,
                        WebhookAuditTargetKind.BulkReplayOperation,
                        selected.Id,
                        selected.ReasonCode,
                        WebhookAuditOutcome.Succeeded,
                        SafeAfterJson: JsonSerializer.Serialize(new
                        {
                            selected.EstimatedEligibleCount,
                            selected.EstimatedSelectedCount,
                            selected.ScheduledCount,
                            selected.RequestedMaxItems
                        }),
                        ConfigurationVersion: policyResolver.Resolve().PolicyVersion,
                        PrincipalKind: WebhookAuditPrincipalKind.System,
                        PrincipalReference: "system:webhook-bulk-replay"),
                    token);
                return new BulkReplayExecution(
                    selected.TenantId,
                    selected.Id,
                    scheduledCount);
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (selected is not null)
        {
            throw new BulkReplayExecutionException(selected.TenantId, selected.Id, exception);
        }
    }

    private async Task MarkFailedAsync(
        Guid tenantId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(tenantId);
            var repository = scope.ServiceProvider.GetRequiredService<IWebhookBulkReplayRepository>();
            var auditWriter = scope.ServiceProvider.GetRequiredService<IWebhookAuditEventWriter>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var operation = await repository.GetByTenantAndIdAsync(tenantId, operationId, token);
                if (operation is null || operation.Status != WebhookBulkReplayStatus.Queued)
                {
                    return;
                }

                var failedAt = timeProvider.GetUtcNow().UtcDateTime;
                operation.Start(failedAt);
                operation.Fail("bulk_replay_execution_failed", failedAt);
                await repository.UpdateAsync(operation, token);
                await auditWriter.AppendAsync(
                    new WebhookAuditWriteRequest(
                        operation.TenantId,
                        WebhookAuditAction.BulkReplayFailed,
                        WebhookAuditTargetKind.BulkReplayOperation,
                        operation.Id,
                        "bulk_replay_execution_failed",
                        WebhookAuditOutcome.Failed,
                        SafeAfterJson: JsonSerializer.Serialize(new
                        {
                            operation.StatusId,
                            operation.FailureCode,
                            operation.RequestedMaxItems
                        }),
                        ConfigurationVersion: policyResolver.Resolve().PolicyVersion,
                        PrincipalKind: WebhookAuditPrincipalKind.System,
                        PrincipalReference: "system:webhook-bulk-replay"),
                    token);
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogError(
                "Webhook bulk replay failure settlement failed. FailureCategory={FailureCategory}",
                "bulk_replay_failure_settlement_failed");
        }
    }

    private sealed record BulkReplayExecution(Guid TenantId, Guid OperationId, int ScheduledCount);

    private sealed class BulkReplayExecutionException(
        Guid tenantId,
        Guid operationId,
        Exception innerException)
        : Exception("Webhook bulk replay execution failed.", innerException)
    {
        public Guid TenantId { get; } = tenantId;
        public Guid OperationId { get; } = operationId;
    }
}
