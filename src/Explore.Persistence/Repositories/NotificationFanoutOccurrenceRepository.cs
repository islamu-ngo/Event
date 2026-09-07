using System.Linq.Expressions;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Models.InternalEvents;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class NotificationFanoutOccurrenceRepository
    : GenericRepository<NotificationFanoutOccurrence, Guid>, INotificationFanoutOccurrenceRepository
{
    private readonly ExploreDbContext dbContext;

    public NotificationFanoutOccurrenceRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        this.dbContext = dbContext;
    }

    public async Task<bool> AcquireEventPrecedenceLockAndHasHeavyAuthorityAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        EnsureCoordinationTransaction();
        await using IAsyncDisposable eventPrecedenceLease = await NotificationFanoutPrecedenceLock.AcquireAsync(
            dbContext,
            tenantId,
            eventId,
            cancellationToken);
        return await dbContext.EventModerationRecords
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .AnyAsync(record => record.TenantId == tenantId
                && record.EventId == eventId
                && record.ActionKind == EventModerationActionKind.HeavyRedacted
                && record.IsIrreversible,
                cancellationToken);
    }

    public async Task AcquireSourceThenEventCoordinationLocksAsync(
        Guid tenantId,
        string sourceType,
        Guid sourceId,
        Guid aggregateVersion,
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        EnsureCoordinationTransaction();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceType);
        if (tenantId == Guid.Empty
            || sourceId == Guid.Empty
            || aggregateVersion == Guid.Empty
            || eventId == Guid.Empty)
        {
            throw new ArgumentException("Fanout coordination requires non-empty source, tenant, and event identifiers.");
        }

        string normalizedSourceType = sourceType.Trim();
        string sourceKey = $"notification-fanout-source-identity:{tenantId:N}:{normalizedSourceType.Length}:{normalizedSourceType}:{sourceId:N}:{aggregateVersion:N}";
        await using IAsyncDisposable sourceLease = await AcquireTransactionLockAsync(sourceKey, cancellationToken);
        await using IAsyncDisposable eventPrecedenceLease = await NotificationFanoutPrecedenceLock.AcquireAsync(
            dbContext,
            tenantId,
            eventId,
            cancellationToken);
    }

    private Task<IAsyncDisposable> AcquireTransactionLockAsync(string key, CancellationToken cancellationToken) =>
        RelationalNamedLock.AcquireTransactionAsync(dbContext, key, cancellationToken);

    public async Task<NotificationFanoutOccurrence?> GetBySourceIdentityForCoordinationAsync(
        Guid tenantId,
        string sourceType,
        Guid sourceId,
        Guid aggregateVersion,
        CancellationToken cancellationToken = default)
    {
        EnsureCoordinationTransaction();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceType);

        return await dbContext.NotificationFanoutOccurrences
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .SingleOrDefaultAsync(occurrence => occurrence.TenantId == tenantId
                && occurrence.SourceType == sourceType.Trim()
                && occurrence.SourceId == sourceId
                && occurrence.AggregateVersion == aggregateVersion,
                cancellationToken);
    }

    public async Task<bool> SessionBelongsToEventForCoordinationAsync(
        Guid tenantId,
        Guid eventId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        EnsureCoordinationTransaction();
        return await dbContext.EventSessions
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .AnyAsync(session => session.TenantId == tenantId
                && session.EventId == eventId
                && session.Id == sessionId,
                cancellationToken);
    }

    public async Task<NotificationFanoutOccurrence?> GetByIdForCoordinationAsync(
        Guid tenantId,
        Guid occurrenceId,
        CancellationToken cancellationToken = default)
    {
        EnsureCoordinationTransaction();
        return await dbContext.NotificationFanoutOccurrences
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .SingleOrDefaultAsync(occurrence => occurrence.TenantId == tenantId
                && occurrence.Id == occurrenceId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationFanoutOccurrence>> GetPendingForEventCoordinationAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        EnsureCoordinationTransaction();
        return await ExactEventQuery(tenantId, eventId)
            .AsNoTracking()
            .Where(occurrence => occurrence.State == NotificationFanoutOccurrenceState.Pending)
            .OrderBy(occurrence => occurrence.OccurredAt)
            .ThenBy(occurrence => occurrence.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationFanoutOccurrence>> GetDirectPredecessorsForCoordinationAsync(
        Guid tenantId,
        Guid eventId,
        Guid replacementOccurrenceId,
        CancellationToken cancellationToken = default)
    {
        EnsureCoordinationTransaction();
        return await ExactEventQuery(tenantId, eventId)
            .AsNoTracking()
            .Where(occurrence => occurrence.SupersededByOccurrenceId == replacementOccurrenceId)
            .OrderBy(occurrence => occurrence.OccurredAt)
            .ThenBy(occurrence => occurrence.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryPersistSupersessionAsync(
        NotificationFanoutOccurrence occurrence,
        CancellationToken cancellationToken = default)
    {
        EnsureCoordinationTransaction();
        ArgumentNullException.ThrowIfNull(occurrence);
        if (occurrence.State != NotificationFanoutOccurrenceState.Superseded
            || !occurrence.SupersededByOccurrenceId.HasValue
            || string.IsNullOrWhiteSpace(occurrence.SuppressionReason)
            || !occurrence.SupersededAt.HasValue)
        {
            throw new InvalidOperationException("A complete superseded occurrence transition is required.");
        }

        int changed = await ExactEventQuery(occurrence.TenantId, occurrence.EventId)
            .Where(row => row.Id == occurrence.Id
                && row.State == NotificationFanoutOccurrenceState.Pending)
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.State, NotificationFanoutOccurrenceState.Superseded)
                    .SetProperty(row => row.SupersededByOccurrenceId, occurrence.SupersededByOccurrenceId)
                    .SetProperty(row => row.SuppressionReason, occurrence.SuppressionReason)
                    .SetProperty(row => row.SupersededAt, occurrence.SupersededAt),
                cancellationToken);
        return changed == 1;
    }

    public async Task<int> SettleNonTerminalRunsForSupersededOccurrenceAsync(
        Guid tenantId,
        Guid occurrenceId,
        DateTime settledAt,
        CancellationToken cancellationToken = default)
    {
        EnsureCoordinationTransaction();
        if (tenantId == Guid.Empty || occurrenceId == Guid.Empty)
        {
            throw new ArgumentException("Fanout run settlement requires non-empty tenant and occurrence identifiers.");
        }

        if (settledAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Fanout run settlement time must be UTC.", nameof(settledAt));
        }

        Expression<Func<NotificationFanoutRun, DateTime?>> terminalTimestamp = run =>
            run.UpdatedAt.HasValue &&
            run.UpdatedAt.Value >= settledAt &&
            (!run.StartedAt.HasValue || run.UpdatedAt.Value >= run.StartedAt.Value) &&
            run.UpdatedAt.Value >= run.CreatedAt
                ? run.UpdatedAt
                : run.StartedAt.HasValue &&
                  run.StartedAt.Value >= settledAt &&
                  run.StartedAt.Value >= run.CreatedAt
                    ? run.StartedAt
                    : run.CreatedAt >= settledAt
                        ? run.CreatedAt
                        : settledAt;

        return await dbContext.NotificationFanoutRuns
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Where(run => run.TenantId == tenantId
                && run.FanoutOccurrenceId == occurrenceId
                && (run.Status == "pending" || run.Status == "processing"))
            .Where(run => dbContext.NotificationFanoutOccurrences
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .Any(occurrence => occurrence.TenantId == run.TenantId
                    && occurrence.Id == run.FanoutOccurrenceId
                    && occurrence.State == NotificationFanoutOccurrenceState.Superseded))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, "completed")
                .SetProperty(run => run.CompletedAt, terminalTimestamp)
                .SetProperty(run => run.ProcessingLeaseOwner, (string?)null)
                .SetProperty(run => run.ProcessingLeaseToken, (Guid?)null)
                .SetProperty(run => run.ProcessingLeaseExpiresAt, (DateTime?)null)
                .SetProperty(run => run.UpdatedAt, terminalTimestamp), cancellationToken);
    }

    public async Task<NotificationFanoutOccurrence?> GetByPointerAsync(
        NotificationFanoutOccurrenceRequested pointer,
        bool trackChanges = false,
        CancellationToken cancellationToken = default)
    {
        if (pointer.Version != NotificationFanoutOccurrenceRequested.CurrentVersion)
        {
            return null;
        }

        IQueryable<NotificationFanoutOccurrence> query = dbContext.NotificationFanoutOccurrences
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Where(occurrence => occurrence.TenantId == pointer.TenantId
                && occurrence.Id == pointer.OccurrenceId);

        if (!trackChanges)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(cancellationToken);
    }

    private IQueryable<NotificationFanoutOccurrence> ExactEventQuery(Guid tenantId, Guid eventId) =>
        dbContext.NotificationFanoutOccurrences
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Where(occurrence => occurrence.TenantId == tenantId
                && occurrence.EventId == eventId);

    private void EnsureCoordinationTransaction()
    {
        NotificationFanoutPrecedenceLock.EnsureActiveTransaction(dbContext);
    }
}
