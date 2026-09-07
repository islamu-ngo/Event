// ABOUTME: EF Core repository for durable Web Push dispatch claims and terminal transitions.
// ABOUTME: Mirrors email outbox affected-row updates while keeping stale-subscription cleanup transactional.

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Explore.Persistence.Repositories;

public sealed class WebPushDispatchOutboxRepository : IWebPushDispatchOutboxRepository
{
    private const string UniqueViolationSqlState = "23505";
    private const int MaxErrorLength = 2000;

    private readonly ExploreDbContext _dbContext;

    public WebPushDispatchOutboxRepository(ExploreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<WebPushDispatchOutbox> Create(WebPushDispatchOutbox entity, CancellationToken cancellationToken = default)
    {
        await _dbContext.WebPushDispatchOutbox.AddAsync(entity, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<bool> CreateIfNotExistsAsync(WebPushDispatchOutbox entity, CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.WebPushDispatchOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebPushTenantOperation)
            .AsNoTracking()
            .AnyAsync(row => row.TenantId == entity.TenantId
                && row.NotificationId == entity.NotificationId
                && row.SubscriptionId == entity.SubscriptionId,
                cancellationToken);
        if (exists)
        {
            return false;
        }

        await _dbContext.WebPushDispatchOutbox.AddAsync(entity, cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsDuplicateNotificationSubscriptionViolation(ex))
        {
            _dbContext.Entry(entity).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<IReadOnlyList<WebPushDispatchOutbox>> GetPendingBatch(
        int batchSize,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.WebPushDispatchOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebPushWorkerCrossTenantQueue)
            .AsNoTracking()
            .Include(row => row.Category)
            .Where(row => (row.Status == WebPushDispatchStatus.Pending || row.Status == WebPushDispatchStatus.RetryScheduled)
                && (row.NextAttemptAt == null || row.NextAttemptAt <= now))
            .OrderBy(row => row.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountDueDispatchAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        return _dbContext.WebPushDispatchOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebPushWorkerCrossTenantQueue)
            .CountAsync(row => (row.Status == WebPushDispatchStatus.Pending || row.Status == WebPushDispatchStatus.RetryScheduled)
                && (row.NextAttemptAt == null || row.NextAttemptAt <= now), cancellationToken);
    }

    public Task<int> CountRetryScheduledAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.WebPushDispatchOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebPushWorkerCrossTenantQueue)
            .CountAsync(row => row.Status == WebPushDispatchStatus.RetryScheduled, cancellationToken);
    }

    public Task<int> CountStaleProcessingAsync(DateTime processingStartedBefore, CancellationToken cancellationToken = default)
    {
        return _dbContext.WebPushDispatchOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebPushWorkerCrossTenantQueue)
            .CountAsync(row => row.Status == WebPushDispatchStatus.Processing
                && row.ProcessingStartedAt != null
                && row.ProcessingStartedAt <= processingStartedBefore, cancellationToken);
    }

    public Task<int> CountTerminalFailureAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.WebPushDispatchOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebPushWorkerCrossTenantQueue)
            .CountAsync(row => row.Status == WebPushDispatchStatus.DeadLettered
                || row.Status == WebPushDispatchStatus.PermanentFailed, cancellationToken);
    }

    public async Task<bool> TryMarkAsProcessing(
        Guid id,
        Guid leaseToken,
        DateTime startedAt,
        CancellationToken cancellationToken = default)
    {
        var updated = await _dbContext.WebPushDispatchOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebPushWorkerCrossTenantQueue)
            .Where(row => row.Id == id
                && (row.Status == WebPushDispatchStatus.Pending || row.Status == WebPushDispatchStatus.RetryScheduled)
                && (row.NextAttemptAt == null || row.NextAttemptAt <= startedAt))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Status, WebPushDispatchStatus.Processing)
                .SetProperty(row => row.ProcessingStartedAt, startedAt)
                .SetProperty(row => row.ProcessingLeaseToken, leaseToken)
                .SetProperty(row => row.AttemptCount, row => row.AttemptCount + 1)
                .SetProperty(row => row.UpdatedAt, startedAt), cancellationToken);

        return updated > 0;
    }

    public Task<WebPushDispatchOutbox?> GetActiveClaimAsync(
        Guid tenantId,
        Guid dispatchId,
        Guid leaseToken,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.WebPushDispatchOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebPushWorkerCrossTenantQueue)
            .AsNoTracking()
            .Include(row => row.Category)
            .Include(row => row.User)
            .FirstOrDefaultAsync(row =>
                row.TenantId == tenantId &&
                row.Id == dispatchId &&
                row.Status == WebPushDispatchStatus.Processing &&
                row.ProcessingLeaseToken == leaseToken &&
                row.User != null &&
                !row.User.IsDeleted,
                cancellationToken);
    }

    public async Task<bool> MarkAsDelivered(Guid id, Guid leaseToken, DateTime deliveredAt, CancellationToken cancellationToken = default)
    {
        var updated = await _dbContext.WebPushDispatchOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebPushWorkerCrossTenantQueue)
            .Where(row => row.Id == id
                && row.Status == WebPushDispatchStatus.Processing
                && row.ProcessingLeaseToken == leaseToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Status, WebPushDispatchStatus.Delivered)
                .SetProperty(row => row.DeliveredAt, deliveredAt)
                .SetProperty(row => row.ProcessingStartedAt, (DateTime?)null)
                .SetProperty(row => row.ProcessingLeaseToken, (Guid?)null)
                .SetProperty(row => row.NextAttemptAt, (DateTime?)null)
                .SetProperty(row => row.LastFailureCategory, (string?)null)
                .SetProperty(row => row.LastError, (string?)null)
                .SetProperty(row => row.LastFailureAt, (DateTime?)null)
                .SetProperty(row => row.UpdatedAt, deliveredAt), cancellationToken);

        return updated > 0;
    }

    public async Task<bool> MarkAsFailed(
        Guid id,
        Guid leaseToken,
        string failureCategory,
        string errorMessage,
        bool isRetryable,
        TimeSpan retryDelay,
        int maxAttempts,
        DateTime failedAt,
        CancellationToken cancellationToken = default)
    {
        var attemptState = await _dbContext.WebPushDispatchOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebPushWorkerCrossTenantQueue)
            .AsNoTracking()
            .Where(row => row.Id == id
                && row.Status == WebPushDispatchStatus.Processing
                && row.ProcessingLeaseToken == leaseToken)
            .Select(row => new { row.AttemptCount, row.MaxAttempts })
            .SingleOrDefaultAsync(cancellationToken);
        if (attemptState is null)
        {
            return false;
        }

        var exhausted = !isRetryable || attemptState.AttemptCount >= Math.Min(attemptState.MaxAttempts, maxAttempts);
        var updated = await _dbContext.WebPushDispatchOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebPushWorkerCrossTenantQueue)
            .Where(row => row.Id == id
                && row.Status == WebPushDispatchStatus.Processing
                && row.ProcessingLeaseToken == leaseToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Status, exhausted ? WebPushDispatchStatus.DeadLettered : WebPushDispatchStatus.RetryScheduled)
                .SetProperty(row => row.DeadLetteredAt, exhausted ? failedAt : (DateTime?)null)
                .SetProperty(row => row.NextAttemptAt, exhausted ? null : failedAt.Add(retryDelay))
                .SetProperty(row => row.ProcessingStartedAt, (DateTime?)null)
                .SetProperty(row => row.ProcessingLeaseToken, (Guid?)null)
                .SetProperty(row => row.LastFailureCategory, Truncate(failureCategory, 100))
                .SetProperty(row => row.LastError, Truncate(errorMessage, MaxErrorLength))
                .SetProperty(row => row.LastFailureAt, failedAt)
                .SetProperty(row => row.UpdatedAt, failedAt), cancellationToken);

        return updated > 0;
    }

    public async Task<bool> MarkAsSkipped(
        Guid id,
        Guid leaseToken,
        string reasonCategory,
        string reasonMessage,
        DateTime skippedAt,
        CancellationToken cancellationToken = default)
    {
        var updated = await _dbContext.WebPushDispatchOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebPushWorkerCrossTenantQueue)
            .Where(row => row.Id == id
                && row.Status == WebPushDispatchStatus.Processing
                && row.ProcessingLeaseToken == leaseToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Status, WebPushDispatchStatus.Skipped)
                .SetProperty(row => row.SkippedAt, skippedAt)
                .SetProperty(row => row.ProcessingStartedAt, (DateTime?)null)
                .SetProperty(row => row.ProcessingLeaseToken, (Guid?)null)
                .SetProperty(row => row.NextAttemptAt, (DateTime?)null)
                .SetProperty(row => row.LastFailureCategory, Truncate(reasonCategory, 100))
                .SetProperty(row => row.LastError, Truncate(reasonMessage, MaxErrorLength))
                .SetProperty(row => row.LastFailureAt, skippedAt)
                .SetProperty(row => row.UpdatedAt, skippedAt), cancellationToken);

        return updated > 0;
    }

    public async Task<int> RecoverStaleProcessing(
        DateTime processingStartedBefore,
        DateTime recoveredAt,
        string failureCategory,
        string errorMessage,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var outboxIds = await _dbContext.WebPushDispatchOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebPushWorkerCrossTenantQueue)
            .AsNoTracking()
            .Where(row => row.Status == WebPushDispatchStatus.Processing
                && row.ProcessingStartedAt != null
                && row.ProcessingStartedAt <= processingStartedBefore)
            .OrderBy(row => row.ProcessingStartedAt)
            .Take(batchSize)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);

        if (outboxIds.Count == 0)
        {
            return 0;
        }

        return await _dbContext.WebPushDispatchOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebPushWorkerCrossTenantQueue)
            .Where(row => outboxIds.Contains(row.Id)
                && row.Status == WebPushDispatchStatus.Processing
                && row.ProcessingStartedAt != null
                && row.ProcessingStartedAt <= processingStartedBefore)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Status, WebPushDispatchStatus.RetryScheduled)
                .SetProperty(row => row.NextAttemptAt, recoveredAt)
                .SetProperty(row => row.ProcessingStartedAt, (DateTime?)null)
                .SetProperty(row => row.ProcessingLeaseToken, (Guid?)null)
                .SetProperty(row => row.LastFailureCategory, Truncate(failureCategory, 100))
                .SetProperty(row => row.LastError, Truncate(errorMessage, MaxErrorLength))
                .SetProperty(row => row.LastFailureAt, recoveredAt)
                .SetProperty(row => row.UpdatedAt, recoveredAt), cancellationToken);
    }

    public async Task<bool> MarkPermanentFailureAndDeactivateSubscription(
        Guid tenantId,
        Guid dispatchId,
        Guid leaseToken,
        Guid subscriptionId,
        string failureCategory,
        string errorMessage,
        DateTime failedAt,
        CancellationToken cancellationToken = default)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async cancellationToken =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var updatedDispatch = await _dbContext.WebPushDispatchOutbox
                .IgnoreTenantFilter(TenantFilterBypassReasons.WebPushTenantOperation)
                .Where(row => row.TenantId == tenantId
                    && row.Id == dispatchId
                    && row.SubscriptionId == subscriptionId
                    && row.Status == WebPushDispatchStatus.Processing
                    && row.ProcessingLeaseToken == leaseToken)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.Status, WebPushDispatchStatus.PermanentFailed)
                    .SetProperty(row => row.PermanentFailedAt, failedAt)
                    .SetProperty(row => row.ProcessingStartedAt, (DateTime?)null)
                    .SetProperty(row => row.ProcessingLeaseToken, (Guid?)null)
                    .SetProperty(row => row.NextAttemptAt, (DateTime?)null)
                    .SetProperty(row => row.LastFailureCategory, Truncate(failureCategory, 100))
                    .SetProperty(row => row.LastError, Truncate(errorMessage, MaxErrorLength))
                    .SetProperty(row => row.LastFailureAt, failedAt)
                    .SetProperty(row => row.UpdatedAt, failedAt), cancellationToken);

            if (updatedDispatch == 0)
            {
                return false;
            }

            var updatedSubscription = await _dbContext.WebPushSubscriptions
                .IgnoreTenantFilter(TenantFilterBypassReasons.WebPushTenantOperation)
                .Where(subscription => subscription.TenantId == tenantId
                    && subscription.Id == subscriptionId
                    && subscription.IsActive)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(subscription => subscription.IsActive, false)
                    .SetProperty(subscription => subscription.DeactivatedAt, failedAt)
                    .SetProperty(subscription => subscription.DeactivationReason, Truncate(failureCategory, 100))
                    .SetProperty(subscription => subscription.UpdatedAt, failedAt), cancellationToken);

            if (updatedSubscription == 0 && !await SameTenantInactiveSubscriptionExists(tenantId, subscriptionId, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    private async Task<bool> SameTenantInactiveSubscriptionExists(Guid tenantId, Guid subscriptionId, CancellationToken cancellationToken)
    {
        return await _dbContext.WebPushSubscriptions
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebPushTenantOperation)
            .AsNoTracking()
            .AnyAsync(subscription => subscription.TenantId == tenantId
                && subscription.Id == subscriptionId
                && !subscription.IsActive, cancellationToken);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        return value is null || value.Length <= maxLength ? value : value[..maxLength];
    }

    private bool IsDuplicateNotificationSubscriptionViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException
        {
            SqlState: UniqueViolationSqlState,
            ConstraintName: { } constraintName
        } &&
        constraintName == RelationalConstraintDescriptorResolver.UniqueIndex<WebPushDispatchOutbox>(
            _dbContext,
            nameof(WebPushDispatchOutbox.TenantId),
            nameof(WebPushDispatchOutbox.NotificationId),
            nameof(WebPushDispatchOutbox.SubscriptionId)).Name;
    }
}
