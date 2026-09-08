using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class WebhookBulkReplayRepository(ExploreDbContext dbContext)
    : IWebhookBulkReplayRepository
{
    private const string ProviderLookupConflict = "provider_lookup_conflict";

    public async Task<WebhookBulkReplayPreviewSnapshot> PreviewAsync(
        Guid tenantId,
        WebhookBulkReplayFilter filter,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        ValidateScopeAndWindow(tenantId, filter, observedAt);

        var localTargets = LocalTargets(tenantId, filter).AsNoTracking();
        var terminalStatuses = TerminalLocalStatuses();
        var terminalTargets = localTargets.Where(target => terminalStatuses.Contains(target.DeliveryStatusId));
        var ineligibleLocalStateCount = await localTargets.CountAsync(
            target => !terminalStatuses.Contains(target.DeliveryStatusId),
            cancellationToken);

        var heldTargets = HeldTargets(tenantId, terminalTargets, observedAt);
        var heldCount = await heldTargets.CountAsync(cancellationToken);
        var notHeldTargets = terminalTargets.Where(target => !heldTargets.Any(held => held.Id == target.Id));

        var payloadUnavailableTargets = notHeldTargets.Where(target =>
            target.WebhookMessage.PayloadClearedAt != null ||
            target.WebhookMessage.PayloadRetentionUntil <= observedAt ||
            EF.Property<byte[]?>(target.WebhookMessage, "_payloadBytes") == null);
        var payloadUnavailableCount = await payloadUnavailableTargets.CountAsync(cancellationToken);
        var payloadAvailableTargets = notHeldTargets.Where(target =>
            target.WebhookMessage.PayloadClearedAt == null &&
            target.WebhookMessage.PayloadRetentionUntil > observedAt &&
            EF.Property<byte[]?>(target.WebhookMessage, "_payloadBytes") != null);

        var endpointUnavailableCount = await payloadAvailableTargets.CountAsync(
            target => target.WebhookEndpoint.StatusId != (int)WebhookEndpointStatus.Active,
            cancellationToken);
        var eligibleCount = await payloadAvailableTargets.CountAsync(
            target => target.WebhookEndpoint.StatusId == (int)WebhookEndpointStatus.Active,
            cancellationToken);

        var publications = ProviderPublications(tenantId, filter).AsNoTracking();
        var providerConflictCount = await publications.CountAsync(
            publication => publication.FailureCategory == ProviderLookupConflict,
            cancellationToken);
        var providerUnknownCount = await publications.CountAsync(
            publication =>
                publication.FailureCategory != ProviderLookupConflict &&
                publication.StatusId == (int)WebhookProviderPublicationStatus.PublicationUnknown,
            cancellationToken);
        var providerManualReconciliationCount = await publications.CountAsync(
            publication =>
                publication.FailureCategory != ProviderLookupConflict &&
                publication.StatusId == (int)WebhookProviderPublicationStatus.ManualReconciliation,
            cancellationToken);
        var providerIneligibleCount = await publications.CountAsync(
            publication =>
                publication.FailureCategory != ProviderLookupConflict &&
                publication.StatusId != (int)WebhookProviderPublicationStatus.PublicationUnknown &&
                publication.StatusId != (int)WebhookProviderPublicationStatus.ManualReconciliation,
            cancellationToken);

        return new WebhookBulkReplayPreviewSnapshot(
            eligibleCount,
            heldCount,
            payloadUnavailableCount,
            endpointUnavailableCount,
            ineligibleLocalStateCount,
            providerConflictCount,
            providerUnknownCount,
            providerManualReconciliationCount,
            providerIneligibleCount);
    }

    public Task AcquireTenantScheduleLockAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        return AcquireTenantLockAsync("schedule", tenantId, cancellationToken);
    }

    public Task<WebhookBulkReplayOperation?> GetByOperationKeyAsync(
        Guid tenantId,
        Guid operationKey,
        CancellationToken cancellationToken) =>
        TenantOperations(tenantId)
            .SingleOrDefaultAsync(operation => operation.OperationKey == operationKey, cancellationToken);

    public Task<WebhookBulkReplayOperation?> GetByTenantAndIdAsync(
        Guid tenantId,
        Guid operationId,
        CancellationToken cancellationToken) =>
        TenantOperations(tenantId)
            .SingleOrDefaultAsync(operation => operation.Id == operationId, cancellationToken);

    public async Task<IReadOnlyList<WebhookBulkReplayOperation>> ListByTenantAsync(
        Guid tenantId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        return await TenantOperations(tenantId)
            .AsNoTracking()
            .OrderByDescending(operation => operation.QueuedAt)
            .ThenByDescending(operation => operation.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountReservedItemsAsync(
        Guid tenantId,
        CancellationToken cancellationToken) =>
        await TenantOperations(tenantId)
            .Where(operation =>
                operation.StatusId == (int)WebhookBulkReplayStatus.Queued ||
                operation.StatusId == (int)WebhookBulkReplayStatus.Executing)
            .SumAsync(operation => (int?)operation.RequestedMaxItems, cancellationToken) ?? 0;

    public async Task<WebhookBulkReplayOperation> CreateAsync(
        WebhookBulkReplayOperation operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        dbContext.WebhookBulkReplayOperations.Add(operation);
        await dbContext.SaveChangesAsync(cancellationToken);
        return operation;
    }

    public Task<WebhookBulkReplayOperation?> GetNextQueuedForUpdateAsync(
        CancellationToken cancellationToken) =>
        RelationalSkipLockedQuery.LoadNextWebhookBulkReplayAsync(
            dbContext,
            cancellationToken);

    public async Task<int> ScheduleEligibleLocalTargetsAsync(
        WebhookBulkReplayOperation operation,
        DateTime scheduledAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Status != WebhookBulkReplayStatus.Executing)
        {
            throw new InvalidOperationException("Only an executing bulk replay can schedule Local targets.");
        }

        if (scheduledAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Scheduled time must use UTC kind.", nameof(scheduledAt));
        }

        await AcquireTenantLockAsync("execute", operation.TenantId, cancellationToken);
        var filter = new WebhookBulkReplayFilter(
            operation.FromUtc,
            operation.ToUtc,
            operation.WebhookConsumerId,
            operation.WebhookEndpointId,
            operation.EventType);
        var terminalTargets = LocalTargets(operation.TenantId, filter)
            .Where(target =>
                target.DeliveryStatusId == (int)WebhookLocalDeliveryStatus.DeadLettered ||
                target.DeliveryStatusId == (int)WebhookLocalDeliveryStatus.Abandoned);
        var heldTargets = HeldTargets(operation.TenantId, terminalTargets, scheduledAt);
        var eligibleTargets = await terminalTargets
            .Where(target => !heldTargets.Any(held => held.Id == target.Id))
            .Where(target =>
                target.WebhookMessage.PayloadClearedAt == null &&
                target.WebhookMessage.PayloadRetentionUntil > scheduledAt &&
                EF.Property<byte[]?>(target.WebhookMessage, "_payloadBytes") != null &&
                target.WebhookEndpoint.StatusId == (int)WebhookEndpointStatus.Active)
            .OrderBy(target => target.CapturedAtUtc)
            .ThenBy(target => target.Id)
            .Take(operation.RequestedMaxItems)
            .ToListAsync(cancellationToken);

        var retryAt = new DateTimeOffset(scheduledAt, TimeSpan.Zero);
        foreach (var target in eligibleTargets)
        {
            target.ScheduleManualRetry(retryAt);
        }

        return eligibleTargets.Count;
    }

    public async Task<WebhookBulkReplayOperation> UpdateAsync(
        WebhookBulkReplayOperation operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await dbContext.SaveChangesAsync(cancellationToken);
        return operation;
    }

    private IQueryable<WebhookLocalTargetSnapshot> LocalTargets(
        Guid tenantId,
        WebhookBulkReplayFilter filter)
    {
        var query = TenantRows(dbContext.WebhookLocalTargetSnapshots, tenantId)
            .Where(target =>
                target.WebhookMessage.MaterializedAt >= filter.FromUtc &&
                target.WebhookMessage.MaterializedAt < filter.ToUtc);
        if (filter.WebhookConsumerId is { } consumerId)
        {
            query = query.Where(target => target.DeliveryPlanSnapshot.WebhookConsumerId == consumerId);
        }

        if (filter.WebhookEndpointId is { } endpointId)
        {
            query = query.Where(target => target.WebhookEndpointId == endpointId);
        }

        if (filter.EventType is { } eventType)
        {
            query = query.Where(target => target.WebhookMessage.EventType == eventType);
        }

        return query;
    }

    private IQueryable<WebhookProviderPublication> ProviderPublications(
        Guid tenantId,
        WebhookBulkReplayFilter filter)
    {
        var query = TenantRows(dbContext.WebhookProviderPublications, tenantId)
            .Where(publication =>
                publication.WebhookMessage!.MaterializedAt >= filter.FromUtc &&
                publication.WebhookMessage.MaterializedAt < filter.ToUtc);
        if (filter.WebhookEndpointId is not null)
        {
            return query.Where(_ => false);
        }

        if (filter.WebhookConsumerId is { } consumerId)
        {
            query = query.Where(publication =>
                publication.WebhookDeliveryPlanSnapshot!.WebhookConsumerId == consumerId);
        }

        if (filter.EventType is { } eventType)
        {
            query = query.Where(publication => publication.WebhookMessage!.EventType == eventType);
        }

        return query;
    }

    private IQueryable<WebhookLocalTargetSnapshot> HeldTargets(
        Guid tenantId,
        IQueryable<WebhookLocalTargetSnapshot> targets,
        DateTime observedAt)
    {
        var holds = TenantRows(dbContext.WebhookRetentionHolds, tenantId)
            .Where(hold => hold.ReleasedAt == null && (hold.ExpiresAt == null || hold.ExpiresAt > observedAt));
        var attempts = TenantRows(dbContext.WebhookDeliveryAttempts, tenantId);
        return targets.Where(target => holds.Any(hold =>
            (hold.SubjectKindId == (int)WebhookRetentionSubjectKind.OutgoingMessage &&
             hold.SubjectId == target.WebhookMessageId) ||
            (hold.SubjectKindId == (int)WebhookRetentionSubjectKind.DeliveryAttempt &&
             attempts.Any(attempt =>
                 attempt.Id == hold.SubjectId &&
                 attempt.MessageId == target.WebhookMessageId &&
                 attempt.EndpointId == target.WebhookEndpointId))));
    }

    private IQueryable<WebhookBulkReplayOperation> TenantOperations(Guid tenantId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        return TenantRows(dbContext.WebhookBulkReplayOperations, tenantId);
    }

    private async Task AcquireTenantLockAsync(
        string purpose,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        _ = await RelationalNamedLock.AcquireTransactionAsync(
            dbContext,
            $"webhook-bulk-replay-{purpose}:{tenantId:D}",
            cancellationToken);
    }

    private static IQueryable<TEntity> TenantRows<TEntity>(DbSet<TEntity> set, Guid tenantId)
        where TEntity : class =>
        set.IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .Where(entity => EF.Property<Guid>(entity, "TenantId") == tenantId);

    private static int[] TerminalLocalStatuses() =>
    [
        (int)WebhookLocalDeliveryStatus.DeadLettered,
        (int)WebhookLocalDeliveryStatus.Abandoned
    ];

    private static void ValidateScopeAndWindow(
        Guid tenantId,
        WebhookBulkReplayFilter filter,
        DateTime observedAt)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.FromUtc.Kind != DateTimeKind.Utc ||
            filter.ToUtc.Kind != DateTimeKind.Utc ||
            observedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Replay timestamps must use UTC kind.");
        }

        if (filter.ToUtc <= filter.FromUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(filter));
        }
    }
}
