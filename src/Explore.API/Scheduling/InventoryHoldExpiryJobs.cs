using System.Globalization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Contracts.Services;
using Quartz;

namespace Explore.API.Scheduling;

/// <summary>
/// Runs one order's hold expiry at the instant its earliest hold is due, replacing a 60-second poll that
/// could withhold sellable capacity for up to a minute past expiry. Held capacity is inventory nobody can
/// buy, so that latency was a revenue-path defect rather than a cosmetic one.
/// <para>
/// The job is deliberately incapable of doing more than its own order: it reads the order and tenant from
/// pointer data and re-reads every fact it needs from the database, so a scheduler row that outlives the
/// work it described can only ever be a wasted wake-up.
/// </para>
/// </summary>
[DisallowConcurrentExecution]
public sealed class InventoryHoldExpiryJob(
    IInventoryHoldExpiryExecutionScopeFactory executionScopes,
    ILogger<InventoryHoldExpiryJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!TryReadIdentifier(context, ScheduledDeadlinePointerKeys.TenantId, out var tenantId) ||
            !TryReadIdentifier(context, ScheduledDeadlinePointerKeys.RegistrationOrderId, out var orderId))
        {
            // A trigger without usable identifiers cannot be repaired by retrying it, and the reconciliation
            // sweep already covers whatever order it meant, so it is dropped rather than thrown.
            logger.LogWarning(
                "Quartz job {JobName} skipped because no usable pointer was supplied.",
                ScheduledJobNames.InventoryHoldExpiry);
            return;
        }

        // Select an immutable Runtime lease before constructing any repository or lifecycle dependency.
        // Disposal owns the whole scope; no ambient tenant or already-constructed context is retargeted.
        await using var scope = await executionScopes.CreateAsync(tenantId, context.CancellationToken);
        var inventory = scope.Services.GetRequiredService<IRegistrationInventoryRepository>();
        var utcNow = DateTime.UtcNow;
        var expiredAnyHold = false;

        // Every hold on the order is offered to the conditional expiry rather than pre-filtered here:
        // TryExpireDueHoldAsync only acts on a hold that is genuinely due under a recoverable order
        // status, so the database stays the single authority on what may expire and the job cannot
        // race a concurrent checkout into expiring a hold that was just consumed.
        foreach (var hold in await inventory.GetHoldsByOrderAsync(orderId, tenantId, context.CancellationToken))
        {
            expiredAnyHold |= await inventory.TryExpireDueHoldAsync(
                hold.Id,
                utcNow,
                context.CancellationToken);
        }

        if (!expiredAnyHold)
        {
            // Normal, not exceptional: the sweep or a checkout may already have finalized this order
            // between the deadline being registered and the trigger firing.
            logger.LogInformation(
                "Quartz job {JobName} found no due holds to expire.",
                ScheduledJobNames.InventoryHoldExpiry);
            return;
        }

        var lifecycle = scope.Services.GetRequiredService<IRegistrationOrderLifecycleService>();
        await lifecycle.RecoverExpiredHoldAsync(orderId, tenantId, context.CancellationToken);
        logger.LogInformation(
            "Quartz job {JobName} expired due holds and recovered its order.",
            ScheduledJobNames.InventoryHoldExpiry);
    }

    private static bool TryReadIdentifier(IJobExecutionContext context, string key, out Guid value)
    {
        var raw = context.MergedJobDataMap.TryGetValue(key, out var entry) && entry is string text ? text : null;
        return Guid.TryParse(raw, CultureInfo.InvariantCulture, out value) && value != Guid.Empty;
    }
}

/// <summary>
/// The safety net that makes the deadline trigger an optimization rather than a correctness dependency.
/// It is not redundant with the deadline job for three reasons, each of which alone would justify it: holds
/// that pre-date a deployment have no registered deadline; a trigger can be lost with the scheduler row;
/// and <see cref="IInventoryHoldExpiryDiscovery.GetTargetsAsync"/> returns orders that
/// need lifecycle recovery after an interrupted expiry, which have no hold deadline at all.
/// <para>
/// Because the deadline path handles the punctual case, this runs far less often than the one-minute poll
/// it replaces — it exists to catch what fell through, not to be the primary mechanism.
/// </para>
/// </summary>
[DisallowConcurrentExecution]
public sealed class InventoryHoldExpiryReconciliationJob(
    IInventoryHoldExpiryDiscovery discovery,
    IInventoryHoldExpiryExecutionScopeFactory executionScopes,
    ILogger<InventoryHoldExpiryReconciliationJob> logger) : IJob
{
    private const int BatchSize = 100;

    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var cancellationToken = context.CancellationToken;
        var utcNow = DateTime.UtcNow;

        var targets = await discovery.GetTargetsAsync(utcNow, BatchSize, cancellationToken);
        var orders = targets.GroupBy(target => (target.TenantId, target.OrderId));

        var recoveredCount = 0;
        foreach (var order in orders)
        {
            var (tenantId, orderId) = order.Key;
            // A fresh scope per order keeps one order's failure, and one order's tracked state, from
            // reaching the next.
            await using var orderScope = await executionScopes.CreateAsync(tenantId, cancellationToken);
            var repository = orderScope.Services.GetRequiredService<IRegistrationInventoryRepository>();
            var expiredAnyHold = false;
            // Re-read exact order ownership before offering discovered IDs to the conditional expiry.
            var holdIds = order.Where(target => target.HoldId.HasValue).Select(target => target.HoldId!.Value).ToHashSet();
            foreach (var hold in await repository.GetHoldsByOrderAsync(orderId, tenantId, cancellationToken))
            {
                if (holdIds.Contains(hold.Id))
                    expiredAnyHold |= await repository.TryExpireDueHoldAsync(hold.Id, utcNow, cancellationToken);
            }

            // A recovery target needs its lifecycle advanced even when no hold expired now, because the
            // hold expiry it is recovering from already happened in an earlier, interrupted pass.
            if (expiredAnyHold || order.Any(target => target.HoldId is null))
            {
                var lifecycle = orderScope.Services.GetRequiredService<IRegistrationOrderLifecycleService>();
                await lifecycle.RecoverExpiredHoldAsync(orderId, tenantId, cancellationToken);
                recoveredCount++;
            }
        }

        if (recoveredCount > 0)
        {
            logger.LogInformation(
                "Quartz job {JobName} recovered {RecoveredCount} orders the deadline path did not cover.",
                ScheduledJobNames.InventoryHoldExpiryReconciliation,
                recoveredCount);
        }
    }
}
