using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Federation;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class PdsSyncOutboxRepository : IPdsSyncOutboxRepository
{
    private const int MaximumCompensationLineageDepth = 32;
    private readonly ExploreDbContext _dbContext;

    public PdsSyncOutboxRepository(ExploreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(PdsSyncOutbox outbox, CancellationToken cancellationToken = default)
    {
        await _dbContext.PdsSyncOutbox.AddAsync(outbox, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> ExistsAsync(
        Guid tenantId,
        Guid outboxId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || outboxId == Guid.Empty)
        {
            throw new ArgumentException("Tenant and PDS outbox identifiers must be non-empty.");
        }

        return CrossTenantOutbox()
            .AsNoTracking()
            .AnyAsync(value => value.TenantId == tenantId && value.Id == outboxId, cancellationToken);
    }

    public async Task<IReadOnlyList<PdsSyncOutbox>> GetCurrentEventDeliveryStatesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> eventIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventIds);
        Guid[] normalizedIds = eventIds.Distinct().ToArray();
        if (tenantId == Guid.Empty || normalizedIds.Any(id => id == Guid.Empty))
            throw new ArgumentException("Tenant and event identifiers must be non-empty.");
        if (normalizedIds.Length > IPdsSyncOutboxRepository.MaximumEventDeliveryStateBatchSize)
            throw new ArgumentOutOfRangeException(nameof(eventIds));
        if (normalizedIds.Length == 0)
            return [];

        return await CrossTenantOutbox()
            .AsNoTracking()
            .Where(value =>
                value.TenantId == tenantId &&
                value.SourceEntityType == "Event" &&
                normalizedIds.Contains(value.SourceEntityId) &&
                value.SupersededAt == null)
            .GroupBy(value => value.SourceEntityId)
            .Select(group => group
                .OrderByDescending(value => value.CreatedAt)
                .ThenByDescending(value => value.Id)
                .First())
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PdsSyncClaim>> ClaimDueAsync(
        int batchSize,
        string leaseOwner,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        if (batchSize is < 1 or > 100 || claimedAt.Kind != DateTimeKind.Utc || leaseDuration <= TimeSpan.Zero)
        {
            return [];
        }

        var normalizedOwner = leaseOwner.Trim();
        if (normalizedOwner.Length is 0 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseOwner));
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            _ = await RelationalNamedLock.AcquireTransactionAsync(
                _dbContext,
                "atproto-pds-outbox-claim",
                cancellationToken);

            var candidates = await CrossTenantOutbox()
                .Where(value =>
                    (value.Status == PdsSyncStatus.Pending &&
                     (value.NextRetryAt == null || value.NextRetryAt <= claimedAt) ||
                     value.Status == PdsSyncStatus.Processing && value.LeaseExpiresAt <= claimedAt) &&
                    value.SupersededAt == null &&
                    (value.DependsOnAtprotoRecordId == null ||
                     _dbContext.AtprotoRecords.Any(record =>
                         record.Id == value.DependsOnAtprotoRecordId &&
                         record.Uri != null &&
                         record.Cid != null &&
                         record.TombstonedAt == null)))
                .OrderBy(value => value.CreatedAt)
                .ThenBy(value => value.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            var claims = new List<PdsSyncClaim>(candidates.Count);
            foreach (var outbox in candidates)
            {
                outbox.Status = PdsSyncStatus.Processing;
                outbox.LeaseOwner = normalizedOwner;
                outbox.LeaseToken = Guid.CreateVersion7();
                outbox.LeaseExpiresAt = claimedAt.Add(leaseDuration);
                outbox.LeaseFence = checked(outbox.LeaseFence + 1);
                claims.Add(new PdsSyncClaim(
                    outbox.Id,
                    outbox.TenantId,
                    outbox.UserId,
                    outbox.LeaseToken.Value,
                    outbox.LeaseFence));
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();
            return claims;
        });
    }

    public Task<PdsSyncOutbox?> GetActiveClaimAsync(
        PdsSyncClaim claim,
        DateTime observedAt,
        CancellationToken cancellationToken = default) =>
        CrossTenantOutbox()
            .AsNoTracking()
            .SingleOrDefaultAsync(value =>
                value.Id == claim.OutboxId &&
                value.TenantId == claim.TenantId &&
                value.UserId == claim.UserId &&
                value.Status == PdsSyncStatus.Processing &&
                value.LeaseToken == claim.LeaseToken &&
                value.LeaseFence == claim.LeaseFence &&
                value.LeaseExpiresAt > observedAt,
                cancellationToken);

    public Task<PdsSyncOutbox?> GetLatestUnsettledMutationAsync(
        Guid tenantId,
        string sourceEntityType,
        Guid sourceEntityId,
        string collection,
        CancellationToken cancellationToken = default) =>
        CrossTenantOutbox()
            .AsNoTracking()
            .Where(value =>
                value.TenantId == tenantId &&
                value.SourceEntityType == sourceEntityType &&
                value.SourceEntityId == sourceEntityId &&
                value.Collection == collection &&
                (value.Status == PdsSyncStatus.Pending || value.Status == PdsSyncStatus.Processing) &&
                value.SupersededAt == null)
            .OrderByDescending(value => value.CreatedAt)
            .ThenByDescending(value => value.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PdsSyncOutbox>> GetUnsettledEventMutationsForActorAsync(
        Guid actorId,
        string sourceEntityType,
        string collection,
        CancellationToken cancellationToken = default) =>
        await GlobalModerationOutbox()
            .Where(outbox =>
                outbox.SourceEntityType == sourceEntityType
                && outbox.Collection == collection
                && (outbox.Status == PdsSyncStatus.Pending || outbox.Status == PdsSyncStatus.Processing)
                && outbox.SupersededAt == null
                && EventsForGlobalModeration().Any(eventEntity =>
                    eventEntity.Id == outbox.SourceEntityId
                    && eventEntity.TenantId == outbox.TenantId
                    && eventEntity.ActorId == actorId))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PdsSyncOutbox>> GetUnsettledEventMutationsForActorAndDidAsync(
        Guid actorId,
        string did,
        string sourceEntityType,
        string collection,
        CancellationToken cancellationToken = default) =>
        await GlobalModerationOutbox()
            .Where(outbox =>
                outbox.Did == did
                && outbox.SourceEntityType == sourceEntityType
                && outbox.Collection == collection
                && (outbox.Status == PdsSyncStatus.Pending || outbox.Status == PdsSyncStatus.Processing)
                && outbox.SupersededAt == null
                && EventsForGlobalModeration().Any(eventEntity =>
                    eventEntity.Id == outbox.SourceEntityId
                    && eventEntity.TenantId == outbox.TenantId
                    && eventEntity.ActorId == actorId))
            .ToListAsync(cancellationToken);

    public async Task<PdsSyncCompensationEvidence> GetCompensationEvidenceAsync(
        PdsSyncOutbox successor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(successor);
        List<CompensationLineageNode> candidates = await CrossTenantOutbox()
            .AsNoTracking()
            .Where(value =>
                value.Status == PdsSyncStatus.Superseded
                && value.SupersededById != null
                && value.TenantId == successor.TenantId
                && value.UserId == successor.UserId
                && value.Did == successor.Did
                && value.Collection == successor.Collection
                && value.RecordKey == successor.RecordKey
                && value.SourceEntityType == successor.SourceEntityType)
            .OrderByDescending(value => value.CreatedAt)
            .Take(MaximumCompensationLineageDepth + 1)
            .Select(value => new CompensationLineageNode(
                value.Id,
                value.SupersededById!.Value,
                value.Payload,
                value.ExpectedCid))
            .ToListAsync(cancellationToken);
        if (candidates.Count > MaximumCompensationLineageDepth)
        {
            return new([], [], IsComplete: false);
        }

        var allowedPayloads = new List<string>();
        var allowedBaseCids = new List<string>();
        var frontier = new HashSet<Guid> { successor.Id };
        while (frontier.Count > 0)
        {
            CompensationLineageNode[] predecessors = candidates
                .Where(value => frontier.Contains(value.SupersededById))
                .ToArray();
            if (predecessors.Length == 0)
            {
                break;
            }

            frontier = predecessors.Select(value => value.Id).ToHashSet();
            allowedPayloads.AddRange(predecessors
                .Select(value => value.Payload)
                .OfType<string>()
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            allowedBaseCids.AddRange(predecessors
                .Select(value => value.ExpectedCid)
                .OfType<string>()
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            candidates.RemoveAll(value => frontier.Contains(value.Id));
        }

        if (!string.IsNullOrWhiteSpace(successor.ExpectedCid))
        {
            allowedBaseCids.Add(successor.ExpectedCid);
        }

        return new(
            allowedPayloads.Distinct(StringComparer.Ordinal).ToArray(),
            allowedBaseCids.Distinct(StringComparer.Ordinal).ToArray(),
            IsComplete: true);
    }

    public async Task<bool> TryRenewClaimAsync(
        PdsSyncClaim claim,
        DateTime observedAt,
        DateTime leaseExpiresAt,
        CancellationToken cancellationToken = default)
    {
        if (observedAt.Kind != DateTimeKind.Utc || leaseExpiresAt.Kind != DateTimeKind.Utc || leaseExpiresAt <= observedAt)
        {
            return false;
        }

        var affected = await CrossTenantOutbox()
            .Where(value =>
                value.Id == claim.OutboxId &&
                value.TenantId == claim.TenantId &&
                value.UserId == claim.UserId &&
                value.Status == PdsSyncStatus.Processing &&
                value.LeaseToken == claim.LeaseToken &&
                value.LeaseFence == claim.LeaseFence &&
                value.LeaseExpiresAt > observedAt)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.LeaseExpiresAt, leaseExpiresAt), cancellationToken);
        return affected == 1;
    }

    public async Task<bool> TrySettleAsync(
        PdsSyncClaim claim,
        string? uri,
        string? cid,
        DateTime settledAt,
        CancellationToken cancellationToken = default,
        string? observedBaseCid = null)
    {
        if (settledAt.Kind != DateTimeKind.Utc)
        {
            return false;
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var outbox = await ActiveClaimQuery(claim, settledAt).SingleOrDefaultAsync(cancellationToken);
            if (outbox is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            var record = await _dbContext.AtprotoRecords.SingleOrDefaultAsync(value =>
                value.Did == outbox.Did &&
                value.Collection == outbox.Collection &&
                value.RecordKey == outbox.RecordKey,
                cancellationToken);

            if (outbox.Operation == PdsSyncOperation.Delete)
            {
                if (record is null
                    && string.Equals(cid, AtprotoPdsDeliveryResult.AbsentRecordCid, StringComparison.Ordinal)
                    && outbox.AtprotoRecordId is null
                    && outbox.ExpectedCid is null
                    && string.Equals(uri, BuildAtUri(outbox), StringComparison.Ordinal)
                    && (await GetCompensationEvidenceAsync(outbox, cancellationToken)) is
                    { IsComplete: true } absentEvidence
                    && (absentEvidence.AllowedPayloads.Count > 0 || absentEvidence.AllowedBaseCids.Count > 0))
                {
                    CompleteOutbox(outbox, uri, cid, settledAt);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return true;
                }

                if (record?.Uri is null
                    || !string.Equals(record.Uri, uri, StringComparison.Ordinal)
                    || record.TombstonedAt is null && record.Cid is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return false;
                }

                if (record.TombstonedAt is null)
                {
                    if (outbox.ExpectedCid is not null
                        && !string.Equals(record.Cid, outbox.ExpectedCid, StringComparison.Ordinal))
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return false;
                    }

                    if (outbox.ExpectedCid is null
                        && !await CanonicalMatchesCompensationEvidenceAsync(outbox, record, cancellationToken))
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return false;
                    }

                    record.TombstonedAt = settledAt;
                    record.UpdatedAt = settledAt;
                }

                uri = record.Uri;
                cid = record.Cid ?? outbox.ExpectedCid ?? cid;
                if (string.IsNullOrWhiteSpace(cid))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return false;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(uri) || string.IsNullOrWhiteSpace(cid))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return false;
                }

                if (record is null)
                {
                    record = new AtprotoRecord
                    {
                        Id = Guid.CreateVersion7(),
                        Did = outbox.Did,
                        Collection = outbox.Collection,
                        RecordKey = outbox.RecordKey,
                        Direction = AtprotoRecordDirection.Outbound,
                        Provenance = AtprotoRecordProvenance.LocalLifecycle,
                        UpdatedAt = settledAt
                    };
                    await _dbContext.AtprotoRecords.AddAsync(record, cancellationToken);
                }
                else
                {
                    bool exactEcho = string.Equals(record.Cid, cid, StringComparison.Ordinal)
                        && JsonSemanticallyEquals(record.RecordJson, outbox.Payload);
                    bool expectedBase = outbox.Operation == PdsSyncOperation.Update
                        && outbox.ExpectedCid is not null
                        && string.Equals(record.Cid, outbox.ExpectedCid, StringComparison.Ordinal);
                    bool observedCompensationBase = outbox.Operation == PdsSyncOperation.Update
                        && outbox.ExpectedCid is null
                        && observedBaseCid is not null
                        && string.Equals(record.Cid, observedBaseCid, StringComparison.Ordinal)
                        && await CanonicalMatchesCompensationEvidenceAsync(outbox, record, cancellationToken);
                    bool immutablePredecessorEcho = outbox.Operation == PdsSyncOperation.Update
                        && outbox.ExpectedCid is null
                        && string.Equals(observedBaseCid, cid, StringComparison.Ordinal)
                        && await CanonicalMatchesCompensationEvidenceAsync(outbox, record, cancellationToken);
                    bool tombstonedRestore = outbox.Operation == PdsSyncOperation.Create
                        && record.TombstonedAt is not null
                        && record.Cid is null;
                    bool tombstonedCompensationRestore = outbox.Operation == PdsSyncOperation.Update
                        && outbox.ExpectedCid is null
                        && record.TombstonedAt is not null
                        && record.Cid is null
                        && outbox.AtprotoRecordId == record.Id
                        && (await GetCompensationEvidenceAsync(outbox, cancellationToken)) is
                        { IsComplete: true } restoreEvidence
                        && (restoreEvidence.AllowedPayloads.Count > 0 || restoreEvidence.AllowedBaseCids.Count > 0);
                    if (!exactEcho
                        && !expectedBase
                        && !observedCompensationBase
                        && !immutablePredecessorEcho
                        && !tombstonedRestore
                        && !tombstonedCompensationRestore)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return false;
                    }

                    record.Direction = record.Direction == AtprotoRecordDirection.Inbound
                        ? AtprotoRecordDirection.Reconciled
                        : AtprotoRecordDirection.Outbound;
                    record.Provenance = record.Direction == AtprotoRecordDirection.Reconciled
                        ? AtprotoRecordProvenance.JetstreamEcho
                        : AtprotoRecordProvenance.LocalLifecycle;
                }

                if (record.Uri is not null && !string.Equals(record.Uri, uri, StringComparison.Ordinal))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return false;
                }

                record.Uri = uri;
                record.Cid = cid;
                record.RecordJson = outbox.Payload;
                record.RecordHash = outbox.PayloadHash;
                record.IndexedAt = settledAt;
                record.UpdatedAt = settledAt;
                record.TombstonedAt = null;
            }

            var ownership = await _dbContext.AtprotoOutboundRecordOwnerships
                .IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoTenantOperation)
                .SingleOrDefaultAsync(value => value.AtprotoRecordId == record.Id, cancellationToken);
            if (ownership is null)
            {
                ownership = new AtprotoOutboundRecordOwnership
                {
                    AtprotoRecordId = record.Id,
                    TenantId = outbox.TenantId,
                    UserId = outbox.UserId,
                    SourceEntityType = outbox.SourceEntityType,
                    SourceEntityId = outbox.SourceEntityId,
                    SourceVersion = outbox.SourceVersion,
                    CreatedAt = settledAt,
                    UpdatedAt = settledAt
                };
                await _dbContext.AtprotoOutboundRecordOwnerships.AddAsync(ownership, cancellationToken);
            }
            else if (ownership.TenantId != outbox.TenantId
                     || ownership.UserId != outbox.UserId
                     || ownership.SourceEntityType != outbox.SourceEntityType)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
            else
            {
                if (ownership.SourceEntityId != outbox.SourceEntityId)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return false;
                }

                ownership.SourceVersion = outbox.SourceVersion;
                ownership.UpdatedAt = settledAt;
            }

            var presentation = await _dbContext.AtprotoRecordTenantPresentations
                .IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoTenantOperation)
                .SingleOrDefaultAsync(value =>
                    value.TenantId == outbox.TenantId && value.AtprotoRecordId == record.Id,
                    cancellationToken);
            if (presentation is null)
            {
                await _dbContext.AtprotoRecordTenantPresentations.AddAsync(
                    new AtprotoRecordTenantPresentation
                    {
                        TenantId = outbox.TenantId,
                        AtprotoRecordId = record.Id,
                        IsVisible = outbox.Operation != PdsSyncOperation.Delete,
                        SourceVersion = record.SourceVersion,
                        EvaluatedAt = settledAt
                    },
                    cancellationToken);
            }
            else
            {
                presentation.IsVisible = outbox.Operation != PdsSyncOperation.Delete;
                presentation.SourceVersion = record.SourceVersion;
                presentation.EvaluatedAt = settledAt;
            }

            outbox.AtprotoRecordId = record.Id;
            if (outbox.SourceEntityType == "Event" && outbox.Operation != PdsSyncOperation.Delete)
            {
                Event? sourceEvent = await _dbContext.Events
                    .IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoTenantOperation)
                    .SingleOrDefaultAsync(value =>
                        value.Id == outbox.SourceEntityId && value.TenantId == outbox.TenantId,
                        cancellationToken);
                if (sourceEvent is not null)
                {
                    sourceEvent.AtprotoRecordId = record.Id;
                }
            }
            CompleteOutbox(outbox, uri!, cid!, settledAt);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        });
    }

    public async Task<bool> TryFailAsync(
        PdsSyncClaim claim,
        string failureCode,
        bool retryable,
        DateTime failedAt,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default)
    {
        var normalizedFailure = failureCode.Trim();
        if (failedAt.Kind != DateTimeKind.Utc || normalizedFailure.Length is 0 or > 100 ||
            !normalizedFailure.All(character =>
                character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-') ||
            retryDelay < TimeSpan.Zero)
        {
            return false;
        }

        var outbox = await ActiveClaimQuery(claim, failedAt).SingleOrDefaultAsync(cancellationToken);
        if (outbox is null)
        {
            return false;
        }

        outbox.RetryCount++;
        outbox.LastError = normalizedFailure;
        if (retryable && outbox.RetryCount < outbox.MaxRetries)
        {
            outbox.Status = PdsSyncStatus.Pending;
            outbox.NextRetryAt = failedAt.Add(retryDelay);
        }
        else
        {
            outbox.Status = PdsSyncStatus.DeadLettered;
            outbox.DeadLetteredAt = failedAt;
            outbox.NextRetryAt = null;
        }

        ClearLease(outbox);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> SupersedePriorAsync(
        Guid tenantId,
        string sourceEntityType,
        Guid sourceEntityId,
        Guid supersedingOutboxId,
        DateTime supersededAt,
        CancellationToken cancellationToken = default)
    {
        var rows = await CrossTenantOutbox()
            .Where(value =>
                value.TenantId == tenantId &&
                value.SourceEntityType == sourceEntityType &&
                value.SourceEntityId == sourceEntityId &&
                value.Id != supersedingOutboxId &&
                value.Status != PdsSyncStatus.Completed &&
                value.Status != PdsSyncStatus.Superseded)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.Status = PdsSyncStatus.Superseded;
            row.SupersededById = supersedingOutboxId;
            row.SupersededAt = supersededAt;
            ClearLease(row);
        }

        return rows.Count;
    }

    private IQueryable<PdsSyncOutbox> CrossTenantOutbox() =>
        _dbContext.PdsSyncOutbox.IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoPdsWorkerCrossTenantQueue);

    private IQueryable<PdsSyncOutbox> GlobalModerationOutbox() =>
        _dbContext.PdsSyncOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoGlobalActorModeration)
            .AsNoTracking();

    private IQueryable<Explore.Domain.Event> EventsForGlobalModeration() =>
        _dbContext.Events
            .IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoGlobalActorModeration)
            .IncludeDeleted()
            .AsNoTracking();

    private IQueryable<PdsSyncOutbox> ActiveClaimQuery(PdsSyncClaim claim, DateTime observedAt) =>
        CrossTenantOutbox().Where(value =>
            value.Id == claim.OutboxId &&
            value.TenantId == claim.TenantId &&
            value.UserId == claim.UserId &&
            value.Status == PdsSyncStatus.Processing &&
            value.LeaseToken == claim.LeaseToken &&
            value.LeaseFence == claim.LeaseFence &&
            value.LeaseExpiresAt > observedAt &&
            value.SupersededAt == null);

    private async Task<bool> CanonicalMatchesCompensationEvidenceAsync(
        PdsSyncOutbox successor,
        AtprotoRecord canonical,
        CancellationToken cancellationToken)
    {
        PdsSyncCompensationEvidence evidence = await GetCompensationEvidenceAsync(successor, cancellationToken);
        return evidence.IsComplete
            && (canonical.Cid is not null
                && evidence.AllowedBaseCids.Contains(canonical.Cid, StringComparer.Ordinal)
                || evidence.AllowedPayloads.Any(payload => JsonSemanticallyEquals(payload, canonical.RecordJson)));
    }

    private static bool JsonSemanticallyEquals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            using JsonDocument leftDocument = JsonDocument.Parse(left);
            using JsonDocument rightDocument = JsonDocument.Parse(right);
            return JsonElement.DeepEquals(leftDocument.RootElement, rightDocument.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void ClearLease(PdsSyncOutbox outbox)
    {
        outbox.LeaseOwner = null;
        outbox.LeaseToken = null;
        outbox.LeaseExpiresAt = null;
    }

    private static void CompleteOutbox(PdsSyncOutbox outbox, string uri, string cid, DateTime settledAt)
    {
        outbox.SettledUri = uri;
        outbox.SettledCid = cid;
        outbox.Status = PdsSyncStatus.Completed;
        outbox.ProcessedAt = settledAt;
        outbox.LastError = null;
        ClearLease(outbox);
    }

    private static string BuildAtUri(PdsSyncOutbox outbox) =>
        $"at://{outbox.Did}/{outbox.Collection}/{outbox.RecordKey}";

    private sealed record CompensationLineageNode(
        Guid Id,
        Guid SupersededById,
        string? Payload,
        string? ExpectedCid);
}
