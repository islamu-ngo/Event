using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public class NotificationFanoutRunRepository : GenericRepository<NotificationFanoutRun, Guid>, INotificationFanoutRunRepository
{
    private const int MaximumClaimRoundSize = 1000;
    private const string ProcessorCode = "fanout";
    private readonly ExploreDbContext _dbContext;

    public NotificationFanoutRunRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<NotificationFanoutRun?> GetBySourceAsync(
        Guid tenantId,
        string fanoutKind,
        int notificationEntityTypeId,
        Guid entityId,
        Guid sourceActorId,
        bool trackChanges = false,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.NotificationFanoutRuns
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Include(run => run.NotificationEntityType)
            .Include(run => run.SourceActor)
                .ThenInclude(actor => actor.Pii)
            .Where(run => run.TenantId == tenantId
                && run.FanoutKind == fanoutKind
                && run.NotificationEntityTypeId == notificationEntityTypeId
                && run.EntityId == entityId
                && run.SourceActorId == sourceActorId);

        if (!trackChanges)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<NotificationFanoutRun>> GetPendingBatchAsync(
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (pageSize <= 0)
        {
            return [];
        }

        return await _dbContext.NotificationFanoutRuns
            .IgnoreTenantFilter(TenantFilterBypassReasons.NotificationFanoutWorkerCrossTenantQueue)
            .AsNoTracking()
            .Where(run => run.Status == "pending")
            .OrderBy(run => run.CreatedAt)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public Task<NotificationFanoutRun?> GetByOccurrenceAsync(
        Guid tenantId,
        Guid occurrenceId,
        bool trackChanges = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<NotificationFanoutRun> query = _dbContext.NotificationFanoutRuns
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Where(run => run.TenantId == tenantId && run.FanoutOccurrenceId == occurrenceId);
        if (!trackChanges)
        {
            query = query.AsNoTracking();
        }

        return query.SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<NotificationFanoutRun?> EnsurePendingOccurrenceRunAsync(
        Guid tenantId,
        Guid occurrenceId,
        Guid runId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || occurrenceId == Guid.Empty || runId == Guid.Empty)
        {
            throw new ArgumentException("Fanout run identifiers must be non-empty.");
        }

        Guid concurrencyStamp = Guid.CreateVersion7();
        DateTime settledAt = DateTime.UtcNow;
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            try
            {
                Guid? eventId = await LoadOccurrenceEventIdHintAsync(
                    tenantId,
                    occurrenceId,
                    cancellationToken);
                if (!eventId.HasValue)
                {
                    return null;
                }

                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                await using IAsyncDisposable globalClaimLease = await AcquireGlobalClaimLockAsync(cancellationToken);
                await using IAsyncDisposable eventPrecedenceLease = await NotificationFanoutPrecedenceLock.AcquireAsync(
                    _dbContext,
                    tenantId,
                    eventId.Value,
                    cancellationToken);
                await using IAsyncDisposable occurrenceLease = await AcquireOccurrenceLockAsync(
                    tenantId,
                    occurrenceId,
                    cancellationToken);

                var occurrence = await LoadPendingOccurrenceSourceAsync(
                    tenantId,
                    occurrenceId,
                    notAfter: null,
                    cancellationToken);
                if (occurrence is null || occurrence.EventId != eventId.Value)
                {
                    await SettleStalePendingOccurrenceRunAsync(
                        tenantId,
                        occurrenceId,
                        settledAt,
                        cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return null;
                }

                var existing = await _dbContext.NotificationFanoutRuns
                    .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        item => item.TenantId == tenantId && item.FanoutOccurrenceId == occurrenceId,
                        cancellationToken);
                if (existing is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return existing;
                }

                var run = CreateOccurrenceRun(
                    runId,
                    tenantId,
                    occurrenceId,
                    occurrence.EventId,
                    occurrence.ActorId,
                    concurrencyStamp);
                await _dbContext.NotificationFanoutRuns.AddAsync(run, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return run;
            }
            catch
            {
                _dbContext.ChangeTracker.Clear();
                throw;
            }
        });
    }

    public async Task<NotificationFanoutClaim?> TryClaimOccurrenceAsync(
        Guid tenantId,
        Guid occurrenceId,
        string leaseOwner,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        int maxActiveClaims,
        int maxActiveClaimsPerTenant,
        int optionalReminderBacklogHighWatermark,
        int optionalReminderBacklogLowWatermark,
        CancellationToken cancellationToken)
    {
        ClaimAttempt attempt = await TryClaimOccurrenceWithOutcomeAsync(
            tenantId,
            occurrenceId,
            leaseOwner,
            claimedAt,
            leaseDuration,
            maxActiveClaims,
            maxActiveClaimsPerTenant,
            optionalReminderBacklogHighWatermark,
            optionalReminderBacklogLowWatermark,
            cancellationToken);
        return attempt.Claim;
    }

    private async Task<ClaimAttempt> TryClaimOccurrenceWithOutcomeAsync(
        Guid tenantId,
        Guid occurrenceId,
        string leaseOwner,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        int maxActiveClaims,
        int maxActiveClaimsPerTenant,
        int optionalReminderBacklogHighWatermark,
        int optionalReminderBacklogLowWatermark,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || occurrenceId == Guid.Empty
            || claimedAt.Kind != DateTimeKind.Utc || leaseDuration <= TimeSpan.Zero
            || maxActiveClaims <= 0 || maxActiveClaimsPerTenant <= 0
            || maxActiveClaimsPerTenant > maxActiveClaims
            || optionalReminderBacklogHighWatermark <= 0
            || optionalReminderBacklogLowWatermark < 0
            || optionalReminderBacklogLowWatermark >= optionalReminderBacklogHighWatermark
            || string.IsNullOrWhiteSpace(leaseOwner))
        {
            return ClaimAttempt.Unavailable;
        }

        string normalizedOwner = leaseOwner.Trim();
        if (normalizedOwner.Length > NotificationFanoutRun.MaxLeaseOwnerLength)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseOwner));
        }

        Guid leaseToken = Guid.CreateVersion7();
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            try
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                await using IAsyncDisposable globalClaimLease = await AcquireGlobalClaimLockAsync(cancellationToken);
                bool optionalRemindersDeferred = await UpdateOptionalReminderHysteresisAsync(
                    claimedAt,
                    optionalReminderBacklogHighWatermark,
                    optionalReminderBacklogLowWatermark,
                    cancellationToken);
                await using IAsyncDisposable tenantClaimLease = await AcquireTenantClaimLockAsync(
                    tenantId,
                    cancellationToken);
                Guid? eventId = await LoadOccurrenceEventIdHintAsync(
                    tenantId,
                    occurrenceId,
                    cancellationToken);
                if (!eventId.HasValue)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return ClaimAttempt.Unavailable;
                }

                await using IAsyncDisposable eventPrecedenceLease = await NotificationFanoutPrecedenceLock.AcquireAsync(
                    _dbContext,
                    tenantId,
                    eventId.Value,
                    cancellationToken);
                await using IAsyncDisposable occurrenceLease = await AcquireOccurrenceLockAsync(
                    tenantId,
                    occurrenceId,
                    cancellationToken);

                var occurrence = await LoadPendingOccurrenceSourceAsync(
                    tenantId,
                    occurrenceId,
                    claimedAt,
                    cancellationToken);
                if (occurrence is null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return ClaimAttempt.Unavailable;
                }

                if (optionalRemindersDeferred
                    && occurrence.DeliveryPolicyId == (int)NotificationDeliveryPolicyEnum.ReminderOptional)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return ClaimAttempt.CapacityDeferred;
                }

                var run = await _dbContext.NotificationFanoutRuns
                    .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                    .SingleOrDefaultAsync(
                        item => item.TenantId == tenantId && item.FanoutOccurrenceId == occurrenceId,
                        cancellationToken);
                if (run is null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return ClaimAttempt.Unavailable;
                }

                bool hasActiveLease = run.Status == "processing" && run.ProcessingLeaseExpiresAt > claimedAt;
                bool ownsActiveLease = hasActiveLease
                    && run.ProcessingLeaseOwner == normalizedOwner
                    && run.ProcessingLeaseToken == leaseToken;
                bool hasExpiredLease = run.Status == "processing"
                    && run.ProcessingLeaseExpiresAt <= claimedAt;
                if ((hasActiveLease && !ownsActiveLease)
                    || (!ownsActiveLease && run.Status != "pending" && !hasExpiredLease))
                {
                    await transaction.CommitAsync(cancellationToken);
                    _dbContext.Entry(run).State = EntityState.Detached;
                    return hasActiveLease
                        ? ClaimAttempt.LeaseContention
                        : ClaimAttempt.Unavailable;
                }

                if (!ownsActiveLease)
                {
                    int globalActiveClaims = await _dbContext.NotificationFanoutRuns
                        .IgnoreTenantFilter(TenantFilterBypassReasons.NotificationFanoutWorkerCrossTenantQueue)
                        .AsNoTracking()
                        .CountAsync(item => item.FanoutOccurrenceId != null
                            && item.Status == "processing"
                            && item.ProcessingLeaseExpiresAt > claimedAt,
                            cancellationToken);
                    if (globalActiveClaims >= maxActiveClaims)
                    {
                        await transaction.CommitAsync(cancellationToken);
                        _dbContext.Entry(run).State = EntityState.Detached;
                        return ClaimAttempt.CapacityDeferred;
                    }

                    int activeClaims = await _dbContext.NotificationFanoutRuns
                        .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                        .AsNoTracking()
                        .CountAsync(item => item.TenantId == tenantId
                            && item.FanoutOccurrenceId != null
                            && item.Status == "processing"
                            && item.ProcessingLeaseExpiresAt > claimedAt,
                            cancellationToken);
                    if (activeClaims >= maxActiveClaimsPerTenant)
                    {
                        await transaction.CommitAsync(cancellationToken);
                        _dbContext.Entry(run).State = EntityState.Detached;
                        return ClaimAttempt.CapacityDeferred;
                    }

                    run.Status = "processing";
                    run.ProcessingLeaseOwner = normalizedOwner;
                    run.ProcessingLeaseToken = leaseToken;
                    run.ProcessingLeaseExpiresAt = claimedAt.Add(leaseDuration);
                    run.HeartbeatAt = claimedAt;
                    run.StartedAt ??= claimedAt;
                    run.ProcessingGeneration = checked(run.ProcessingGeneration + 1);
                    run.ProcessingFence = checked(run.ProcessingFence + 1);
                    run.FailedAt = null;
                    run.LastError = null;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);

                NotificationFanoutAudienceCursor? cursor = run.CursorFirstEligibleRegistrationCreatedAt.HasValue
                    && run.CursorUserId.HasValue
                    ? new NotificationFanoutAudienceCursor(
                        run.CursorFirstEligibleRegistrationCreatedAt.Value,
                        run.CursorUserId.Value)
                    : null;
                var claim = new NotificationFanoutClaim(
                    run.Id,
                    run.TenantId,
                    occurrenceId,
                    leaseToken,
                    run.ProcessingFence,
                    run.ProcessingGeneration,
                    cursor);
                _dbContext.Entry(run).State = EntityState.Detached;
                return new ClaimAttempt(claim, ClaimAttemptOutcome.Claimed);
            }
            catch
            {
                _dbContext.ChangeTracker.Clear();
                throw;
            }
        });
    }

    public async Task<NotificationFanoutClaimRoundResult> ClaimDueRoundAsync(
        NotificationFanoutClaimRoundRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ClaimedAt.Kind != DateTimeKind.Utc
            || request.LeaseDuration <= TimeSpan.Zero
            || request.MaxTenants is < 1 or > MaximumClaimRoundSize
            || request.MaxActiveClaims <= 0
            || request.MaxActiveClaimsPerTenant <= 0
            || request.MaxActiveClaimsPerTenant > request.MaxActiveClaims
            || request.OptionalReminderBacklogHighWatermark <= 0
            || request.OptionalReminderBacklogLowWatermark < 0
            || request.OptionalReminderBacklogLowWatermark >= request.OptionalReminderBacklogHighWatermark
            || string.IsNullOrWhiteSpace(request.LeaseOwner))
        {
            throw new ArgumentException("The notification fanout claim round is invalid.", nameof(request));
        }

        string normalizedOwner = request.LeaseOwner.Trim();
        if (normalizedOwner.Length > NotificationFanoutRun.MaxLeaseOwnerLength)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        if (_dbContext.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException("A notification fanout claim round cannot run inside an ambient transaction.");
        }

        RunnableRound runnableRound = await LoadRunnableRoundAsync(
            request.ClaimedAt,
            request.MaxTenants,
            request.MaxActiveClaims,
            request.MaxActiveClaimsPerTenant,
            request.OptionalReminderBacklogHighWatermark,
            request.OptionalReminderBacklogLowWatermark,
            cancellationToken);
        IReadOnlyList<RunnableOccurrenceCandidate> candidates = runnableRound.Candidates;
        var claims = new List<NotificationFanoutClaim>(candidates.Count);
        int leaseContentionCount = 0;
        int capacityDeferredCount = 0;
        int unavailableCount = 0;
        foreach (RunnableOccurrenceCandidate candidate in candidates)
        {
            ClaimAttempt attempt = await TryClaimOccurrenceWithOutcomeAsync(
                candidate.TenantId,
                candidate.OccurrenceId,
                normalizedOwner,
                request.ClaimedAt,
                request.LeaseDuration,
                request.MaxActiveClaims,
                request.MaxActiveClaimsPerTenant,
                request.OptionalReminderBacklogHighWatermark,
                request.OptionalReminderBacklogLowWatermark,
                cancellationToken);
            if (attempt.Claim is not null)
            {
                claims.Add(attempt.Claim);
            }
            else if (attempt.Outcome == ClaimAttemptOutcome.LeaseContention)
            {
                leaseContentionCount++;
            }
            else if (attempt.Outcome == ClaimAttemptOutcome.CapacityDeferred)
            {
                capacityDeferredCount++;
            }
            else
            {
                unavailableCount++;
            }
        }

        return new NotificationFanoutClaimRoundResult(
            claims,
            candidates.Count,
            leaseContentionCount,
            capacityDeferredCount,
            unavailableCount);
    }

    public async Task<bool> TryRenewClaimAsync(
        NotificationFanoutClaim claim,
        DateTime observedAt,
        DateTime leaseExpiresAt,
        CancellationToken cancellationToken)
    {
        if (observedAt.Kind != DateTimeKind.Utc || leaseExpiresAt.Kind != DateTimeKind.Utc
            || leaseExpiresAt <= observedAt)
        {
            return false;
        }

        int affected = await ActiveClaimQuery(claim, observedAt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    run => run.ProcessingLeaseExpiresAt,
                    run => run.ProcessingLeaseExpiresAt >= leaseExpiresAt
                        ? run.ProcessingLeaseExpiresAt
                        : leaseExpiresAt)
                .SetProperty(run => run.HeartbeatAt, observedAt)
                .SetProperty(run => run.UpdatedAt, observedAt), cancellationToken);
        return affected == 1;
    }

    public async Task<bool> TryCheckpointAsync(
        NotificationFanoutClaim claim,
        NotificationFanoutAudienceCursor? expectedCursor,
        NotificationFanoutAudienceCursor nextCursor,
        int processedDelta,
        int createdDelta,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        if (observedAt.Kind != DateTimeKind.Utc || processedDelta < 0 || createdDelta < 0
            || createdDelta > processedDelta || !IsAfter(nextCursor, expectedCursor))
        {
            return false;
        }

        DateTime? expectedTimestamp = expectedCursor?.FirstEligibleRegistrationCreatedAt;
        Guid? expectedUserId = expectedCursor?.UserId;
        int affected = await ActiveClaimQuery(claim, observedAt)
            .Where(run =>
                (expectedTimestamp == null
                    && run.CursorFirstEligibleRegistrationCreatedAt == null
                    && run.CursorUserId == null)
                || (expectedTimestamp != null
                    && run.CursorFirstEligibleRegistrationCreatedAt == expectedTimestamp
                    && run.CursorUserId == expectedUserId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.CursorFirstEligibleRegistrationCreatedAt,
                    nextCursor.FirstEligibleRegistrationCreatedAt)
                .SetProperty(run => run.CursorUserId, nextCursor.UserId)
                .SetProperty(run => run.ProcessedCount, run => run.ProcessedCount + processedDelta)
                .SetProperty(run => run.CreatedNotificationCount,
                    run => run.CreatedNotificationCount + createdDelta)
                .SetProperty(run => run.HeartbeatAt, observedAt)
                .SetProperty(run => run.UpdatedAt, observedAt), cancellationToken);
        return affected == 1;
    }

    public async Task<bool> TryCompleteAsync(
        NotificationFanoutClaim claim,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        if (observedAt.Kind != DateTimeKind.Utc)
        {
            return false;
        }

        int affected = await ActiveClaimQuery(claim, observedAt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, "completed")
                .SetProperty(run => run.CompletedAt, observedAt)
                .SetProperty(run => run.HeartbeatAt, observedAt)
                .SetProperty(run => run.ProcessingLeaseOwner, (string?)null)
                .SetProperty(run => run.ProcessingLeaseToken, (Guid?)null)
                .SetProperty(run => run.ProcessingLeaseExpiresAt, (DateTime?)null)
                .SetProperty(run => run.UpdatedAt, observedAt), cancellationToken);
        return affected == 1;
    }

    public async Task<NotificationFanoutProcessorSnapshot> GetProcessorSnapshotAsync(
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        if (observedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The fanout processor observation time must be UTC.", nameof(observedAt));
        }

        IQueryable<NotificationFanoutOccurrence> dueOccurrences =
            from occurrence in _dbContext.NotificationFanoutOccurrences
                .IgnoreTenantFilter(TenantFilterBypassReasons.NotificationFanoutWorkerCrossTenantQueue)
                .AsNoTracking()
            join run in _dbContext.NotificationFanoutRuns
                .IgnoreTenantFilter(TenantFilterBypassReasons.NotificationFanoutWorkerCrossTenantQueue)
                .AsNoTracking()
                on new { occurrence.TenantId, OccurrenceId = (Guid?)occurrence.Id }
                equals new { run.TenantId, OccurrenceId = run.FanoutOccurrenceId }
            where occurrence.State == NotificationFanoutOccurrenceState.Pending
                && occurrence.NotBefore <= observedAt
                && (run.Status == "pending"
                    || (run.Status == "processing" && run.ProcessingLeaseExpiresAt <= observedAt))
            select occurrence;

        int dueOccurrenceCount = await dueOccurrences.CountAsync(cancellationToken);
        int dueOptionalReminderCount = await dueOccurrences.CountAsync(
            item => item.DeliveryPolicyId == (int)NotificationDeliveryPolicyEnum.ReminderOptional,
            cancellationToken);
        DateTime? oldestDueAt = await dueOccurrences
            .Select(item => (DateTime?)item.OccurredAt)
            .MinAsync(cancellationToken);

        IQueryable<NotificationFanoutRun> occurrenceRuns = _dbContext.NotificationFanoutRuns
            .IgnoreTenantFilter(TenantFilterBypassReasons.NotificationFanoutWorkerCrossTenantQueue)
            .AsNoTracking()
            .Where(run => run.FanoutOccurrenceId != null);
        int activeClaimCount = await occurrenceRuns.CountAsync(
            run => run.Status == "processing" && run.ProcessingLeaseExpiresAt > observedAt,
            cancellationToken);
        int expiredClaimCount = await occurrenceRuns.CountAsync(
            run => run.Status == "processing" && run.ProcessingLeaseExpiresAt <= observedAt,
            cancellationToken);
        long processedRecipientCount = await occurrenceRuns
            .Where(run => run.Status == "pending" || run.Status == "processing")
            .Select(run => (long?)run.ProcessedCount)
            .SumAsync(cancellationToken) ?? 0L;
        int supersededOccurrenceCount = await _dbContext.NotificationFanoutOccurrences
            .IgnoreTenantFilter(TenantFilterBypassReasons.NotificationFanoutWorkerCrossTenantQueue)
            .AsNoTracking()
            .CountAsync(
                occurrence => occurrence.State == NotificationFanoutOccurrenceState.Superseded,
                cancellationToken);
        bool optionalRemindersDeferred = await _dbContext.NotificationFanoutProcessorStates
            .AsNoTracking()
            .Where(state => state.ProcessorCode == ProcessorCode)
            .Select(state => state.OptionalRemindersDeferred)
            .SingleOrDefaultAsync(cancellationToken);

        return new NotificationFanoutProcessorSnapshot(
            dueOccurrenceCount,
            dueOccurrenceCount - dueOptionalReminderCount,
            dueOptionalReminderCount,
            activeClaimCount,
            expiredClaimCount,
            supersededOccurrenceCount,
            processedRecipientCount,
            oldestDueAt,
            optionalRemindersDeferred);
    }

    private IQueryable<NotificationFanoutRun> ActiveClaimQuery(
        NotificationFanoutClaim claim,
        DateTime observedAt) =>
        _dbContext.NotificationFanoutRuns
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Where(run => run.TenantId == claim.TenantId
                && run.Id == claim.RunId
                && run.FanoutOccurrenceId == claim.OccurrenceId
                && _dbContext.NotificationFanoutOccurrences
                    .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                    .Any(occurrence => occurrence.TenantId == run.TenantId
                        && occurrence.Id == run.FanoutOccurrenceId
                        && occurrence.State == NotificationFanoutOccurrenceState.Pending)
                && run.Status == "processing"
                && run.ProcessingLeaseToken == claim.LeaseToken
                && run.ProcessingFence == claim.Fence
                && run.ProcessingGeneration == claim.Generation
                && run.ProcessingLeaseExpiresAt > observedAt);

    private Task<IAsyncDisposable> AcquireOccurrenceLockAsync(
        Guid tenantId,
        Guid occurrenceId,
        CancellationToken cancellationToken)
        => RelationalNamedLock.AcquireTransactionAsync(
            _dbContext,
            $"notification-fanout:{tenantId:N}:{occurrenceId:N}",
            cancellationToken);

    private Task<IAsyncDisposable> AcquireGlobalClaimLockAsync(CancellationToken cancellationToken) =>
        RelationalNamedLock.AcquireTransactionAsync(
            _dbContext,
            "notification-fanout-global-claim",
            cancellationToken);

    private Task<IAsyncDisposable> AcquireTenantClaimLockAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
        => RelationalNamedLock.AcquireTransactionAsync(
            _dbContext,
            $"notification-fanout-tenant-claim:{tenantId:N}",
            cancellationToken);

    private async Task<RunnableRound> LoadRunnableRoundAsync(
        DateTime claimedAt,
        int maxTenants,
        int maxActiveClaims,
        int maxActiveClaimsPerTenant,
        int optionalReminderBacklogHighWatermark,
        int optionalReminderBacklogLowWatermark,
        CancellationToken cancellationToken)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            try
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                await using IAsyncDisposable globalClaimLease = await AcquireGlobalClaimLockAsync(cancellationToken);
                bool optionalRemindersDeferred = await UpdateOptionalReminderHysteresisAsync(
                    claimedAt,
                    optionalReminderBacklogHighWatermark,
                    optionalReminderBacklogLowWatermark,
                    cancellationToken);
                IReadOnlyList<RunnableOccurrenceCandidate> candidates = await ExecuteRunnableRoundAsync(
                    transaction,
                    claimedAt,
                    maxTenants,
                    maxActiveClaims,
                    maxActiveClaimsPerTenant,
                    optionalRemindersDeferred,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();
                return new RunnableRound(candidates, optionalRemindersDeferred);
            }
            catch
            {
                _dbContext.ChangeTracker.Clear();
                throw;
            }
        });
    }

    private async Task<bool> UpdateOptionalReminderHysteresisAsync(
        DateTime claimedAt,
        int highWatermark,
        int lowWatermark,
        CancellationToken cancellationToken)
    {
        NotificationFanoutProcessorState? state = await _dbContext.NotificationFanoutProcessorStates
            .SingleOrDefaultAsync(item => item.ProcessorCode == ProcessorCode, cancellationToken);
        if (state is null)
        {
            state = new NotificationFanoutProcessorState
            {
                Id = Guid.CreateVersion7(),
                ProcessorCode = ProcessorCode,
                UpdatedAt = claimedAt
            };
            await _dbContext.NotificationFanoutProcessorStates.AddAsync(state, cancellationToken);
        }

        int coreBacklog = await CountActiveCoreBacklogAsync(claimedAt, cancellationToken);
        state.OptionalRemindersDeferred = coreBacklog >= highWatermark
            || (coreBacklog > lowWatermark && state.OptionalRemindersDeferred);
        state.UpdatedAt = claimedAt;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return state.OptionalRemindersDeferred;
    }

    private Task<int> CountActiveCoreBacklogAsync(
        DateTime claimedAt,
        CancellationToken cancellationToken) =>
        (from occurrence in _dbContext.NotificationFanoutOccurrences
                .IgnoreTenantFilter(TenantFilterBypassReasons.NotificationFanoutWorkerCrossTenantQueue)
                .AsNoTracking()
         join run in _dbContext.NotificationFanoutRuns
             .IgnoreTenantFilter(TenantFilterBypassReasons.NotificationFanoutWorkerCrossTenantQueue)
             .AsNoTracking()
             on new { occurrence.TenantId, OccurrenceId = (Guid?)occurrence.Id }
             equals new { run.TenantId, OccurrenceId = run.FanoutOccurrenceId }
         where occurrence.State == NotificationFanoutOccurrenceState.Pending
             && occurrence.NotBefore <= claimedAt
             && occurrence.DeliveryPolicyId != (int)NotificationDeliveryPolicyEnum.ReminderOptional
             && (run.Status == "pending" || run.Status == "processing")
         select occurrence.Id)
        .CountAsync(cancellationToken);

    private async Task<IReadOnlyList<RunnableOccurrenceCandidate>> ExecuteRunnableRoundAsync(
        IDbContextTransaction transaction,
        DateTime claimedAt,
        int maxTenants,
        int maxActiveClaims,
        int maxActiveClaimsPerTenant,
        bool deferOptionalReminders,
        CancellationToken cancellationToken)
    {
        string runsTable = GetTableIdentifier<NotificationFanoutRun>();
        string occurrencesTable = GetTableIdentifier<NotificationFanoutOccurrence>();
        string sql = $$"""
            WITH active_global AS (
                SELECT COUNT(*)::integer AS active_count
                FROM {{runsTable}}
                WHERE fanout_occurrence_id IS NOT NULL
                  AND status = 'processing'
                  AND processing_lease_expires_at > @claimed_at
            ),
            active_by_tenant AS (
                SELECT tenant_id, COUNT(*)::integer AS active_count
                FROM {{runsTable}}
                WHERE fanout_occurrence_id IS NOT NULL
                  AND status = 'processing'
                  AND processing_lease_expires_at > @claimed_at
                GROUP BY tenant_id
            ),
            ranked AS (
                SELECT occurrence.tenant_id,
                       occurrence.id AS occurrence_id,
                       occurrence.priority,
                       occurrence.occurred_at,
                       ROW_NUMBER() OVER (
                           PARTITION BY occurrence.tenant_id
                           ORDER BY occurrence.priority DESC,
                                    occurrence.occurred_at,
                                    occurrence.id) AS tenant_rank
                FROM {{occurrencesTable}} AS occurrence
                INNER JOIN {{runsTable}} AS run
                   ON run.tenant_id = occurrence.tenant_id
                  AND run.fanout_occurrence_id = occurrence.id
                LEFT JOIN active_by_tenant AS active
                  ON active.tenant_id = occurrence.tenant_id
                CROSS JOIN active_global AS global_active
                WHERE occurrence.state = 1
                  AND occurrence.not_before <= @claimed_at
                  AND global_active.active_count < @max_active_claims
                  AND COALESCE(active.active_count, 0) < @max_active_claims_per_tenant
                  AND (NOT @defer_optional_reminders
                       OR occurrence.delivery_policy_id <> @reminder_optional_policy_id)
                  AND (run.status = 'pending'
                       OR (run.status = 'processing'
                           AND run.processing_lease_expires_at <= @claimed_at))
            )
            SELECT tenant_id, occurrence_id
            FROM ranked
            WHERE tenant_rank = 1
            ORDER BY priority DESC, occurred_at, occurrence_id
            LIMIT LEAST(
                @max_tenants,
                GREATEST(0, @max_active_claims - (SELECT active_count FROM active_global)));
            """;

        await using DbCommand command = _dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = sql;
        AddParameter(command, "claimed_at", claimedAt);
        AddParameter(command, "max_tenants", maxTenants, DbType.Int32);
        AddParameter(command, "max_active_claims", maxActiveClaims, DbType.Int32);
        AddParameter(
            command,
            "max_active_claims_per_tenant",
            maxActiveClaimsPerTenant,
            DbType.Int32);
        AddParameter(command, "defer_optional_reminders", deferOptionalReminders, DbType.Boolean);
        AddParameter(
            command,
            "reminder_optional_policy_id",
            (int)NotificationDeliveryPolicyEnum.ReminderOptional,
            DbType.Int32);

        var candidates = new List<RunnableOccurrenceCandidate>(maxTenants);
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(new RunnableOccurrenceCandidate(reader.GetGuid(0), reader.GetGuid(1)));
        }

        return candidates;
    }

    private string GetTableIdentifier<TEntity>()
    {
        IEntityType entityType = _dbContext.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"The {typeof(TEntity).Name} mapping is unavailable.");
        string tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException($"The {typeof(TEntity).Name} table mapping is unavailable.");
        string? schema = entityType.GetSchema() ?? entityType.Model.GetDefaultSchema();
        return _dbContext.GetService<ISqlGenerationHelper>().DelimitIdentifier(tableName, schema);
    }

    private static void AddParameter(DbCommand command, string name, object value, DbType dbType)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        parameter.DbType = dbType;
        command.Parameters.Add(parameter);
    }

    private static void AddParameter(DbCommand command, string name, DateTime value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private Task<OccurrenceSource?> LoadPendingOccurrenceSourceAsync(
        Guid tenantId,
        Guid occurrenceId,
        DateTime? notAfter,
        CancellationToken cancellationToken) =>
        _dbContext.NotificationFanoutOccurrences
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId
                && item.Id == occurrenceId
                && item.State == NotificationFanoutOccurrenceState.Pending
                && (notAfter == null || item.NotBefore <= notAfter))
            .Join(
                _dbContext.Events
                    .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                    .AsNoTracking(),
                item => new { item.TenantId, item.EventId },
                item => new { item.TenantId, EventId = item.Id },
                (item, eventEntity) => new OccurrenceSource(
                    item.EventId,
                    eventEntity.ActorId,
                    item.DeliveryPolicyId))
            .SingleOrDefaultAsync(cancellationToken);

    private Task<Guid?> LoadOccurrenceEventIdHintAsync(
        Guid tenantId,
        Guid occurrenceId,
        CancellationToken cancellationToken) =>
        _dbContext.NotificationFanoutOccurrences
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.Id == occurrenceId)
            .Select(item => (Guid?)item.EventId)
            .SingleOrDefaultAsync(cancellationToken);

    private Task<int> SettleStalePendingOccurrenceRunAsync(
        Guid tenantId,
        Guid occurrenceId,
        DateTime settledAt,
        CancellationToken cancellationToken) =>
        _dbContext.NotificationFanoutRuns
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Where(run => run.TenantId == tenantId
                && run.FanoutOccurrenceId == occurrenceId
                && run.Status == "pending")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, "completed")
                .SetProperty(run => run.CompletedAt, settledAt)
                .SetProperty(run => run.UpdatedAt, settledAt),
                cancellationToken);

    private static NotificationFanoutRun CreateOccurrenceRun(
        Guid runId,
        Guid tenantId,
        Guid occurrenceId,
        Guid eventId,
        Guid sourceActorId,
        Guid concurrencyStamp) =>
        new()
        {
            Id = runId,
            TenantId = tenantId,
            Tenant = null!,
            FanoutOccurrenceId = occurrenceId,
            FanoutOccurrence = null,
            FanoutKind = "recipient_occurrence",
            NotificationEntityTypeId = (int)NotificationEntityTypeEnum.Event,
            NotificationEntityType = null!,
            EntityId = eventId,
            SourceActorId = sourceActorId,
            SourceActor = null!,
            Status = "pending",
            ConcurrencyStamp = concurrencyStamp
        };

    private static bool IsAfter(
        NotificationFanoutAudienceCursor next,
        NotificationFanoutAudienceCursor? current) =>
        current is null
        || next.FirstEligibleRegistrationCreatedAt > current.Value.FirstEligibleRegistrationCreatedAt
        || (next.FirstEligibleRegistrationCreatedAt == current.Value.FirstEligibleRegistrationCreatedAt
            && next.UserId.CompareTo(current.Value.UserId) > 0);

    private sealed record OccurrenceSource(Guid EventId, Guid ActorId, int DeliveryPolicyId);

    private sealed record RunnableOccurrenceCandidate(Guid TenantId, Guid OccurrenceId);

    private sealed record RunnableRound(
        IReadOnlyList<RunnableOccurrenceCandidate> Candidates,
        bool OptionalRemindersDeferred);

    private sealed record ClaimAttempt(
        NotificationFanoutClaim? Claim,
        ClaimAttemptOutcome Outcome)
    {
        public static ClaimAttempt LeaseContention { get; } = new(null, ClaimAttemptOutcome.LeaseContention);
        public static ClaimAttempt CapacityDeferred { get; } = new(null, ClaimAttemptOutcome.CapacityDeferred);
        public static ClaimAttempt Unavailable { get; } = new(null, ClaimAttemptOutcome.Unavailable);
    }

    private enum ClaimAttemptOutcome
    {
        Claimed = 1,
        LeaseContention = 2,
        CapacityDeferred = 3,
        Unavailable = 4
    }
}
