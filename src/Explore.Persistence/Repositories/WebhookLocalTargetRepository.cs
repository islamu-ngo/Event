using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class WebhookLocalTargetRepository(ExploreDbContext dbContext)
    : IWebhookLocalTargetRepository
{
    private const int MaximumBatchSize = 1000;

    public async Task<IReadOnlyList<Guid>> GetDueTenantIdsAsync(
        int tenantLimit,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (tenantLimit is < 1 or > 10_000)
        {
            return [];
        }

        return await dbContext.WebhookLocalTargetSnapshots
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookWorkerCrossTenantQueue)
            .AsNoTracking()
            .Where(target =>
                (target.DeliveryStatusId == (int)WebhookLocalDeliveryStatus.Pending ||
                 target.DeliveryStatusId == (int)WebhookLocalDeliveryStatus.RetryDue) &&
                target.NextActionAtUtc <= nowUtc &&
                target.WebhookEndpoint.StatusId == (int)WebhookEndpointStatus.Active)
            .GroupBy(target => target.TenantId)
            .Select(group => new
            {
                TenantId = group.Key,
                OldestDueAt = group.Min(target => target.NextActionAtUtc)
            })
            .OrderBy(candidate => candidate.OldestDueAt)
            .ThenBy(candidate => candidate.TenantId)
            .Take(tenantLimit)
            .Select(candidate => candidate.TenantId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WebhookLocalTargetClaim>> ClaimDueAsync(
        WebhookLocalTargetClaimRequest request,
        IReadOnlyDictionary<Guid, WebhookDeliveryClaimLimits> tenantLimits,
        CancellationToken cancellationToken)
    {
        if (request.BatchSize is < 1 or > MaximumBatchSize ||
            request.CandidateBatchSize < request.BatchSize ||
            request.GlobalInFlightLimit < 1 ||
            request.LeaseDuration <= TimeSpan.Zero ||
            request.TenantOrder.Count == 0 ||
            tenantLimits.Count == 0)
        {
            return [];
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            try
            {
                return await ClaimDueInTransactionAsync(request, tenantLimits, cancellationToken);
            }
            catch
            {
                dbContext.ChangeTracker.Clear();
                throw;
            }
        });
    }

    public Task<int> CountDueAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        dbContext.WebhookLocalTargetSnapshots
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookWorkerCrossTenantQueue)
            .AsNoTracking()
            .CountAsync(target =>
                (target.DeliveryStatusId == (int)WebhookLocalDeliveryStatus.Pending ||
                 target.DeliveryStatusId == (int)WebhookLocalDeliveryStatus.RetryDue) &&
                target.NextActionAtUtc <= nowUtc &&
                target.WebhookEndpoint.StatusId == (int)WebhookEndpointStatus.Active,
                cancellationToken);

    public Task<int> CountStaleDeliveringAsync(
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken) =>
        dbContext.WebhookLocalTargetSnapshots
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookWorkerCrossTenantQueue)
            .AsNoTracking()
            .CountAsync(target =>
                target.DeliveryStatusId == (int)WebhookLocalDeliveryStatus.Delivering &&
                target.ProcessingLeaseExpiresAtUtc != null &&
                target.ProcessingLeaseExpiresAtUtc <= observedAtUtc,
                cancellationToken);

    public async Task<int> RecoverExpiredClaimsAsync(
        DateTimeOffset recoveredAtUtc,
        string failureCategory,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (batchSize is < 1 or > MaximumBatchSize)
        {
            return 0;
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            try
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
                await AcquireClaimLockAsync(cancellationToken);
                var expired = await dbContext.WebhookLocalTargetSnapshots
                    .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookWorkerCrossTenantQueue)
                    .Where(target =>
                        target.DeliveryStatusId == (int)WebhookLocalDeliveryStatus.Delivering &&
                        target.ProcessingLeaseExpiresAtUtc != null &&
                        target.ProcessingLeaseExpiresAtUtc <= recoveredAtUtc)
                    .OrderBy(target => target.ProcessingLeaseExpiresAtUtc)
                    .ThenBy(target => target.Id)
                    .Take(batchSize)
                    .ToListAsync(cancellationToken);

                foreach (var target in expired)
                {
                    var startedAt = target.UpdatedAt ?? target.CreatedAt;
                    dbContext.WebhookDeliveryAttempts.Add(new WebhookDeliveryAttempt
                    {
                        Id = Guid.CreateVersion7(),
                        TenantId = target.TenantId,
                        MessageId = target.WebhookMessageId,
                        EndpointId = target.WebhookEndpointId,
                        AttemptNumber = checked((int)target.DeliveryFence),
                        Outcome = WebhookDeliveryAttemptOutcome.Failed,
                        ScheduledAt = startedAt,
                        SentAt = startedAt,
                        CompletedAt = recoveredAtUtc.UtcDateTime,
                        ProcessingFence = target.DeliveryFence,
                        FailureCategory = Truncate(failureCategory, 100),
                        CreatedAt = recoveredAtUtc.UtcDateTime
                    });
                    target.RecoverExpiredClaim(recoveredAtUtc);
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return expired.Count;
            }
            catch
            {
                dbContext.ChangeTracker.Clear();
                throw;
            }
        });
    }

    public Task<WebhookLocalTargetSnapshot?> GetActiveClaimAsync(
        Guid tenantId,
        Guid targetId,
        Guid leaseToken,
        long deliveryFence,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken) =>
        dbContext.WebhookLocalTargetSnapshots
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .Include(target => target.WebhookMessage)
            .Include(target => target.WebhookEndpoint)
            .FirstOrDefaultAsync(target =>
                target.TenantId == tenantId &&
                target.Id == targetId &&
                target.DeliveryStatusId == (int)WebhookLocalDeliveryStatus.Delivering &&
                target.ProcessingLeaseToken == leaseToken &&
                target.DeliveryFence == deliveryFence &&
                target.WebhookEndpoint.StatusId == (int)WebhookEndpointStatus.Active &&
                target.ProcessingLeaseExpiresAtUtc > observedAtUtc,
                cancellationToken);

    public Task<WebhookLocalTargetSnapshot?> GetByMessageAndEndpointForUpdateAsync(
        Guid tenantId,
        Guid messageId,
        Guid endpointId,
        CancellationToken cancellationToken) =>
        dbContext.WebhookLocalTargetSnapshots
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .Include(target => target.WebhookMessage)
            .Include(target => target.WebhookEndpoint)
            .SingleOrDefaultAsync(target =>
                target.TenantId == tenantId &&
                target.WebhookMessageId == messageId &&
                target.WebhookEndpointId == endpointId,
                cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    private async Task<IReadOnlyList<WebhookLocalTargetClaim>> ClaimDueInTransactionAsync(
        WebhookLocalTargetClaimRequest request,
        IReadOnlyDictionary<Guid, WebhookDeliveryClaimLimits> tenantLimits,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await AcquireClaimLockAsync(cancellationToken);

        var activeLeaseQuery = dbContext.WebhookLocalTargetSnapshots
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookWorkerCrossTenantQueue)
            .AsNoTracking()
            .Where(target =>
                target.DeliveryStatusId == (int)WebhookLocalDeliveryStatus.Delivering &&
                target.ProcessingLeaseExpiresAtUtc > request.ClaimedAtUtc);
        var currentGlobalInFlight = await activeLeaseQuery.CountAsync(cancellationToken);
        var remainingGlobalCapacity = Math.Min(
            request.BatchSize,
            request.GlobalInFlightLimit - currentGlobalInFlight);
        if (remainingGlobalCapacity <= 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return [];
        }

        var tenantOrder = request.TenantOrder
            .Where(tenantLimits.ContainsKey)
            .Distinct()
            .ToArray();
        if (tenantOrder.Length == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return [];
        }

        var tenantInFlight = await activeLeaseQuery
            .Where(target => tenantOrder.Contains(target.TenantId))
            .GroupBy(target => target.TenantId)
            .Select(group => new { TenantId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.TenantId, row => row.Count, cancellationToken);
        var endpointInFlight = (await activeLeaseQuery
                .Where(target => tenantOrder.Contains(target.TenantId))
                .GroupBy(target => new { target.TenantId, target.WebhookEndpointId })
                .Select(group => new
                {
                    group.Key.TenantId,
                    EndpointId = group.Key.WebhookEndpointId,
                    Count = group.Count()
                })
                .ToListAsync(cancellationToken))
            .ToDictionary(row => (row.TenantId, row.EndpointId), row => row.Count);

        var perTenantCandidateLimit = Math.Max(1, request.CandidateBatchSize / tenantOrder.Length);
        var queues = new Dictionary<Guid, Queue<WebhookLocalTargetSnapshot>>(tenantOrder.Length);
        foreach (var tenantId in tenantOrder)
        {
            var query = dbContext.WebhookLocalTargetSnapshots
                .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookWorkerCrossTenantQueue)
                .Include(target => target.WebhookMessage)
                .Where(target =>
                    target.TenantId == tenantId &&
                    (target.DeliveryStatusId == (int)WebhookLocalDeliveryStatus.Pending ||
                     target.DeliveryStatusId == (int)WebhookLocalDeliveryStatus.RetryDue) &&
                    target.NextActionAtUtc <= request.ClaimedAtUtc &&
                    target.WebhookEndpoint.StatusId == (int)WebhookEndpointStatus.Active);

            if (request.TargetId is { } targetId)
            {
                query = query.Where(target => target.Id == targetId);
            }

            var candidates = await query
                .OrderBy(target => target.NextActionAtUtc)
                .ThenBy(target => target.CapturedAtUtc)
                .ThenBy(target => target.Id)
                .Take(perTenantCandidateLimit)
                .ToListAsync(cancellationToken);
            queues[tenantId] = new Queue<WebhookLocalTargetSnapshot>(candidates);
        }

        var selected = SelectFairTargets(
            tenantOrder,
            tenantLimits,
            tenantInFlight,
            endpointInFlight,
            queues,
            remainingGlobalCapacity);
        var leaseExpiresAtUtc = request.ClaimedAtUtc.Add(request.LeaseDuration);
        var claims = new List<WebhookLocalTargetClaim>(selected.Count);
        foreach (var target in selected)
        {
            var leaseToken = Guid.CreateVersion7();
            target.ClaimForDelivery(
                "webhook-delivery-worker",
                leaseToken,
                leaseExpiresAtUtc,
                request.ClaimedAtUtc);
            claims.Add(new WebhookLocalTargetClaim(
                target,
                target.WebhookMessage,
                leaseToken,
                target.DeliveryFence,
                request.ClaimedAtUtc,
                leaseExpiresAtUtc));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return claims;
    }

    private static List<WebhookLocalTargetSnapshot> SelectFairTargets(
        IReadOnlyList<Guid> tenantOrder,
        IReadOnlyDictionary<Guid, WebhookDeliveryClaimLimits> tenantLimits,
        IReadOnlyDictionary<Guid, int> tenantInFlight,
        Dictionary<(Guid TenantId, Guid EndpointId), int> endpointInFlight,
        IReadOnlyDictionary<Guid, Queue<WebhookLocalTargetSnapshot>> queues,
        int capacity)
    {
        var selected = new List<WebhookLocalTargetSnapshot>(capacity);
        var selectedByTenant = new Dictionary<Guid, int>();
        while (selected.Count < capacity)
        {
            var added = false;
            foreach (var tenantId in tenantOrder)
            {
                var limits = tenantLimits[tenantId];
                var currentTenantInFlight = tenantInFlight.GetValueOrDefault(tenantId);
                var selectedTenantCount = selectedByTenant.GetValueOrDefault(tenantId);
                if (selectedTenantCount >= limits.MaxItemsPerClaimCycle ||
                    currentTenantInFlight + selectedTenantCount >= limits.MaxInFlightPerTenant)
                {
                    continue;
                }

                var queue = queues[tenantId];
                while (queue.TryDequeue(out var candidate))
                {
                    var endpointKey = (candidate.TenantId, candidate.WebhookEndpointId);
                    var currentEndpointInFlight = endpointInFlight.GetValueOrDefault(endpointKey);
                    if (currentEndpointInFlight >= limits.MaxInFlightPerEndpoint)
                    {
                        continue;
                    }

                    selected.Add(candidate);
                    selectedByTenant[tenantId] = selectedTenantCount + 1;
                    endpointInFlight[endpointKey] = currentEndpointInFlight + 1;
                    added = true;
                    break;
                }

                if (selected.Count == capacity)
                {
                    break;
                }
            }

            if (!added)
            {
                break;
            }
        }

        return selected;
    }

    private async Task AcquireClaimLockAsync(CancellationToken cancellationToken)
    {
        _ = await RelationalNamedLock.AcquireTransactionAsync(
            dbContext,
            "webhook-local-target-claim",
            cancellationToken);
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
