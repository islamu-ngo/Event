// ABOUTME: Implements the single global renewable Jetstream lease and fenced cursor ownership.
// ABOUTME: Atomically applies canonical records, tombstones, tenant presentations, or quarantine before cursor advance.

using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Models.Storage;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed class AtprotoJetstreamRepository : IAtprotoJetstreamRepository, IAtprotoPdsSnapshotRepository
{
    private const string EventCollection = "community.lexicon.calendar.event";
    private const string RsvpCollection = "community.lexicon.calendar.rsvp";
    private const string TidAlphabet = "234567abcdefghijklmnopqrstuvwxyz";
    private static readonly string[] SafeRasterExtensions = [".jpg", ".png", ".gif", ".webp", ".avif"];
    private static readonly EventScheduleProjectionCalculator ScheduleProjectionCalculator = new();
    private readonly ExploreDbContext _dbContext;

    public AtprotoJetstreamRepository(ExploreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AtprotoJetstreamClaim?> TryClaimAsync(
        string service,
        string leaseOwner,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        var normalizedService = service.Trim();
        var normalizedOwner = leaseOwner.Trim();
        if (normalizedService.Length is 0 or > 500 || normalizedOwner.Length is 0 or > 200 ||
            claimedAt.Kind != DateTimeKind.Utc || leaseDuration <= TimeSpan.Zero)
        {
            return null;
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await AcquireConsumerLockAsync(normalizedService, cancellationToken);

                var state = await _dbContext.AtprotoJetstreamConsumerStates
                    .SingleOrDefaultAsync(value => value.Service == normalizedService, cancellationToken);
                if (state is null)
                {
                    state = new AtprotoJetstreamConsumerState
                    {
                        Id = Guid.CreateVersion7(),
                        Service = normalizedService,
                        UpdatedAt = claimedAt
                    };
                    await _dbContext.AtprotoJetstreamConsumerStates.AddAsync(state, cancellationToken);
                }
                else if (state.LeaseExpiresAt > claimedAt)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return null;
                }

                state.LeaseOwner = normalizedOwner;
                state.LeaseToken = Guid.CreateVersion7();
                state.LeaseExpiresAt = claimedAt.Add(leaseDuration);
                state.LeaseFence = checked(state.LeaseFence + 1);
                state.UpdatedAt = claimedAt;
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                var claim = new AtprotoJetstreamClaim(
                    state.Id,
                    state.Service,
                    state.Cursor,
                    state.LeaseToken.Value,
                    state.LeaseFence);
                _dbContext.Entry(state).State = EntityState.Detached;
                return claim;
            }
            catch
            {
                await RollbackFailedAttemptAsync(transaction);
                throw;
            }
        });
    }

    public async Task<bool> TryRenewAsync(
        AtprotoJetstreamClaim claim,
        DateTime observedAt,
        DateTime leaseExpiresAt,
        CancellationToken cancellationToken = default)
    {
        if (observedAt.Kind != DateTimeKind.Utc || leaseExpiresAt.Kind != DateTimeKind.Utc || leaseExpiresAt <= observedAt)
        {
            return false;
        }

        var affected = await _dbContext.AtprotoJetstreamConsumerStates
            .Where(value =>
                value.Id == claim.ConsumerStateId &&
                value.Service == claim.Service &&
                value.LeaseToken == claim.LeaseToken &&
                value.LeaseFence == claim.LeaseFence &&
                value.LeaseExpiresAt > observedAt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.LeaseExpiresAt, leaseExpiresAt)
                .SetProperty(value => value.UpdatedAt, observedAt), cancellationToken);
        return affected == 1;
    }

    public async Task<bool> TryApplyAndAdvanceAsync(
        AtprotoJetstreamApplyRequest request,
        CancellationToken cancellationToken = default) =>
        (await TryApplyAndAdvanceWithResultAsync(request, cancellationToken)).Applied;

    public async Task<AtprotoPersistenceApplyResult> TryApplyAndAdvanceWithResultAsync(
        AtprotoJetstreamApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        // Exactly one effect per envelope: a materialised record, quarantine evidence, or an account purge.
        int effects = (request.Record is not null ? 1 : 0)
            + (request.Quarantine is not null ? 1 : 0)
            + (request.AccountPurge is not null ? 1 : 0);
        if (request.ObservedAt.Kind != DateTimeKind.Utc || request.NextCursor <= request.ExpectedCursor ||
            effects != 1 ||
            (!request.AdvanceCursor && request.Quarantine?.ReasonCode != "invalid_cursor"))
        {
            return AtprotoPersistenceApplyResult.Rejected;
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var consumed = new List<FileStorageWriteResult>();
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await AcquireConsumerLockAsync(request.Claim.Service, cancellationToken);
                var state = await _dbContext.AtprotoJetstreamConsumerStates.SingleOrDefaultAsync(value =>
                    value.Id == request.Claim.ConsumerStateId &&
                    value.Service == request.Claim.Service &&
                    value.Cursor == request.ExpectedCursor &&
                    value.LeaseToken == request.Claim.LeaseToken &&
                    value.LeaseFence == request.Claim.LeaseFence &&
                    value.LeaseExpiresAt > request.ObservedAt,
                    cancellationToken);
                if (state is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return AtprotoPersistenceApplyResult.Rejected;
                }

                if (request.Record is not null && !await ApplyRecordAsync(request, consumed, cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return AtprotoPersistenceApplyResult.Rejected;
                }

                if (request.EventProjectionInvalidation is not null)
                {
                    await InvalidateEventProjectionAsync(request, cancellationToken);
                }

                if (request.AccountPurge is not null)
                {
                    await PurgeAccountAsync(request, cancellationToken);
                }

                if (request.Quarantine is not null)
                {
                    bool alreadyQuarantined = await _dbContext.AtprotoJetstreamQuarantines.AnyAsync(value =>
                        value.ConsumerStateId == state.Id && value.Cursor == request.NextCursor,
                        cancellationToken);
                    if (!alreadyQuarantined)
                    {
                        request.Quarantine.ConsumerStateId = state.Id;
                        request.Quarantine.Cursor = request.NextCursor;
                        await _dbContext.AtprotoJetstreamQuarantines.AddAsync(request.Quarantine, cancellationToken);
                    }
                }

                state.Cursor = request.AdvanceCursor ? request.NextCursor : request.ExpectedCursor;
                state.LastEventAt = request.ObservedAt;
                state.UpdatedAt = request.ObservedAt;
                await _dbContext.SaveChangesAsync(cancellationToken);
                if (!await HasCurrentFenceAtCommitAsync(request.Claim, cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return AtprotoPersistenceApplyResult.Rejected;
                }

                await transaction.CommitAsync(cancellationToken);
                return new AtprotoPersistenceApplyResult(true, consumed);
            }
            catch
            {
                await RollbackFailedAttemptAsync(transaction);
                throw;
            }
        });
    }

    public async Task<bool> TryReconcileAsync(
        AtprotoPdsSnapshotApplyRequest request,
        CancellationToken cancellationToken) =>
        (await TryReconcileWithResultAsync(request, cancellationToken)).Applied;

    public async Task<AtprotoPersistenceApplyResult> TryReconcileWithResultAsync(
        AtprotoPdsSnapshotApplyRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsValidSnapshotRequest(request))
        {
            return AtprotoPersistenceApplyResult.Rejected;
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        bool firstAttempt = true;
        return await strategy.ExecuteAsync(async () =>
        {
            if (!firstAttempt)
            {
                _dbContext.ChangeTracker.Clear();
            }
            firstAttempt = false;
            var consumed = new List<FileStorageWriteResult>();

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await AcquireConsumerLockAsync(request.Claim.Service, cancellationToken);
                AtprotoJetstreamConsumerState? state = await _dbContext.AtprotoJetstreamConsumerStates
                    .SingleOrDefaultAsync(value =>
                        value.Id == request.Claim.ConsumerStateId
                        && value.Service == request.Claim.Service
                        && value.LeaseToken == request.Claim.LeaseToken
                        && value.LeaseFence == request.Claim.LeaseFence
                        && value.LeaseExpiresAt > request.ObservedAt,
                        cancellationToken);
                if (state is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return AtprotoPersistenceApplyResult.Rejected;
                }

                string[] scannedDids = request.ScannedDids.ToArray();
                Dictionary<string, AtprotoPdsSnapshot> snapshots = request.Snapshots
                    .ToDictionary(value => value.Did, StringComparer.Ordinal);
                HashSet<(string Did, string Collection, string RecordKey)> present = request.Snapshots
                    .SelectMany(snapshot => snapshot.PresentIdentities.Select(identity =>
                        (snapshot.Did, identity.Collection, identity.RecordKey)))
                    .ToHashSet();
                HashSet<(string Did, string Collection, string RecordKey)> accepted = request.Snapshots
                    .SelectMany(snapshot => snapshot.Items.Select(item =>
                        (snapshot.Did, item.Record.Collection, item.Record.RecordKey)))
                    .ToHashSet();
                List<AtprotoRecord> canonicalRecords = await _dbContext.AtprotoRecords
                    .Where(value =>
                        scannedDids.Contains(value.Did)
                        && (value.Collection == EventCollection || value.Collection == RsvpCollection))
                    .ToListAsync(cancellationToken);
                Dictionary<(string Did, string Collection, string RecordKey), AtprotoRecord> canonicalByIdentity =
                    canonicalRecords.ToDictionary(value => (value.Did, value.Collection, value.RecordKey));
                AtprotoRecord[] missing = canonicalRecords
                    .Where(value =>
                        value.Direction != AtprotoRecordDirection.Outbound
                        && value.SourceVersion < request.SnapshotVersion
                        && !present.Contains((value.Did, value.Collection, value.RecordKey)))
                    .ToArray();
                string[] missingEventUris = missing
                    .Where(value => value.Collection == EventCollection && value.Uri is not null)
                    .Select(value => value.Uri!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var dependentCandidates = missingEventUris.Length == 0
                    ? []
                    : await _dbContext.AtprotoRecords
                        .Where(value =>
                            value.Collection == RsvpCollection
                            && value.SubjectUri != null
                            && missingEventUris.Contains(value.SubjectUri)
                            && value.SourceVersion < request.SnapshotVersion)
                        .Select(value => new
                        {
                            value.Id,
                            value.Did,
                            value.Collection,
                            value.RecordKey
                        })
                        .ToArrayAsync(cancellationToken);
                Guid[] dependentRecordIds = dependentCandidates
                    .Where(value => !accepted.Contains((value.Did, value.Collection, value.RecordKey)))
                    .Select(value => value.Id)
                    .ToArray();
                Guid[] canonicalIds = canonicalRecords.Select(value => value.Id).ToArray();
                Dictionary<Guid, AtprotoEventProjection> projections = await _dbContext.AtprotoEventProjections
                    .Where(value => canonicalIds.Contains(value.AtprotoRecordId))
                    .ToDictionaryAsync(value => value.AtprotoRecordId, cancellationToken);
                Guid[] presentationRecordIds = canonicalIds.Concat(dependentRecordIds).Distinct().ToArray();
                List<AtprotoRecordTenantPresentation> presentations = await _dbContext
                    .AtprotoRecordTenantPresentations
                    .IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoPdsSnapshotGlobalReconciliation)
                    .Where(value => presentationRecordIds.Contains(value.AtprotoRecordId))
                    .ToListAsync(cancellationToken);
                Dictionary<(Guid TenantId, Guid RecordId), AtprotoRecordTenantPresentation> presentationByKey =
                    presentations.ToDictionary(value => (value.TenantId, value.AtprotoRecordId));
                HashSet<Guid> visibleTenantIds = request.PresentationTenantIds.ToHashSet();

                foreach (string did in scannedDids)
                {
                    foreach (AtprotoPdsSnapshotItem item in snapshots[did].Items)
                    {
                        var identity = (did, item.Record.Collection, item.Record.RecordKey);
                        AtprotoFederatedEventImportPlan[] eventImports = request.EventImports
                            .Where(value =>
                                string.Equals(value.Did, did, StringComparison.Ordinal)
                                && string.Equals(value.AtUri, item.Record.Uri, StringComparison.Ordinal))
                            .ToArray();
                        if (!canonicalByIdentity.TryGetValue(identity, out AtprotoRecord? canonical))
                        {
                            canonical = item.Record;
                            canonical.Id = canonical.Id == Guid.Empty ? Guid.CreateVersion7() : canonical.Id;
                            canonical.Direction = AtprotoRecordDirection.Inbound;
                            canonical.Provenance = AtprotoRecordProvenance.Jetstream;
                            canonicalByIdentity.Add(identity, canonical);
                            await _dbContext.AtprotoRecords.AddAsync(canonical, cancellationToken);
                        }
                        else if (canonical.SourceVersion > request.SnapshotVersion)
                        {
                            continue;
                        }
                        else if (canonical.SourceVersion == request.SnapshotVersion)
                        {
                            await ApplyEventImportsAsync(
                                canonical,
                                eventImports,
                                request.ObservedAt,
                                TenantFilterBypassReasons.AtprotoPdsSnapshotGlobalReconciliation,
                                updateExisting: false,
                                consumed,
                                cancellationToken);
                            continue;
                        }
                        else
                        {
                            ApplySnapshotRecord(canonical, item.Record);
                        }

                        canonical.SourceVersion = request.SnapshotVersion;
                        canonical.SourceCursor = null;
                        canonical.UpdatedAt = request.ObservedAt;
                        canonical.TombstonedAt = null;
                        ApplySnapshotProjection(canonical, item.EventProjection, projections, request);
                        ReconcilePresentations(
                            canonical,
                            visibleTenantIds,
                            presentations,
                            presentationByKey,
                            request);
                        await ApplyEventImportsAsync(
                            canonical,
                            eventImports,
                            request.ObservedAt,
                            TenantFilterBypassReasons.AtprotoPdsSnapshotGlobalReconciliation,
                            updateExisting: true,
                            consumed,
                            cancellationToken);
                    }
                }

                foreach ((string did, string collection, string recordKey) in present.Except(accepted))
                {
                    if (!canonicalByIdentity.TryGetValue((did, collection, recordKey), out AtprotoRecord? canonical)
                        || canonical.SourceVersion >= request.SnapshotVersion)
                    {
                        continue;
                    }

                    if (collection == EventCollection
                        && projections.Remove(canonical.Id, out AtprotoEventProjection? projection))
                    {
                        _dbContext.AtprotoEventProjections.Remove(projection);
                    }

                    HidePresentations(canonical.Id, presentations, request);
                    await ApplyEventImportsAsync(
                        canonical,
                        [],
                        request.ObservedAt,
                        TenantFilterBypassReasons.AtprotoPdsSnapshotGlobalReconciliation,
                        updateExisting: true,
                        consumed,
                        cancellationToken,
                        forceTombstone: true);
                }

                foreach (AtprotoRecord canonical in missing)
                {
                    canonical.Cid = null;
                    canonical.RecordJson = null;
                    canonical.RecordHash = null;
                    canonical.SubjectUri = null;
                    canonical.SubjectCid = null;
                    canonical.IndexedAt = request.ObservedAt;
                    canonical.SourceVersion = request.SnapshotVersion;
                    canonical.SourceCursor = null;
                    canonical.UpdatedAt = request.ObservedAt;
                    canonical.TombstonedAt = request.ObservedAt;
                    if (canonical.Collection == EventCollection
                        && projections.Remove(canonical.Id, out AtprotoEventProjection? projection))
                    {
                        _dbContext.AtprotoEventProjections.Remove(projection);
                    }

                    HidePresentations(canonical.Id, presentations, request);
                    await ApplyEventImportsAsync(
                        canonical,
                        [],
                        request.ObservedAt,
                        TenantFilterBypassReasons.AtprotoPdsSnapshotGlobalReconciliation,
                        updateExisting: true,
                        consumed,
                        cancellationToken);
                }

                foreach (Guid dependentId in dependentRecordIds)
                {
                    HidePresentations(dependentId, presentations, request);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                if (!await HasCurrentFenceAtCommitAsync(request.Claim, cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return AtprotoPersistenceApplyResult.Rejected;
                }

                await transaction.CommitAsync(cancellationToken);
                return new AtprotoPersistenceApplyResult(true, consumed);
            }
            catch
            {
                await RollbackFailedAttemptAsync(transaction);
                throw;
            }
        });
    }

    private static bool IsValidSnapshotRequest(AtprotoPdsSnapshotApplyRequest request)
    {
        if (request.ObservedAt.Kind != DateTimeKind.Utc
            || request.SnapshotVersion <= 0
            || request.Claim.ConsumerStateId == Guid.Empty
            || request.Claim.LeaseToken == Guid.Empty
            || request.Claim.LeaseFence <= 0
            || request.Claim.Service is not { Length: > 0 and <= 500 }
            || request.ScannedDids is not { Count: > 0 and <= 100 }
            || request.Snapshots is null
            || request.PresentationTenantIds is null
            || request.Snapshots.Count != request.ScannedDids.Count
            || request.PresentationTenantIds.Any(value => value == Guid.Empty)
            || request.PresentationTenantIds.Count != request.PresentationTenantIds.Distinct().Count())
        {
            return false;
        }

        var scannedDids = new HashSet<string>(StringComparer.Ordinal);
        foreach (string did in request.ScannedDids)
        {
            if (!IsValidDid(did) || !scannedDids.Add(did))
            {
                return false;
            }
        }

        var snapshotDids = new HashSet<string>(StringComparer.Ordinal);
        foreach (AtprotoPdsSnapshot snapshot in request.Snapshots)
        {
            if (!scannedDids.Contains(snapshot.Did)
                || !snapshotDids.Add(snapshot.Did)
                || snapshot.PresentIdentities is null
                || snapshot.Items is null)
            {
                return false;
            }

            var present = new HashSet<(string Collection, string RecordKey)>();
            foreach (AtprotoPdsSnapshotIdentity identity in snapshot.PresentIdentities)
            {
                if (!IsExactCollection(identity.Collection)
                    || !IsValidProtocolRecordKey(identity.RecordKey)
                    || !present.Add((identity.Collection, identity.RecordKey)))
                {
                    return false;
                }
            }

            var accepted = new HashSet<(string Collection, string RecordKey)>();
            foreach (AtprotoPdsSnapshotItem item in snapshot.Items)
            {
                AtprotoRecord record = item.Record;
                var identity = (record.Collection, record.RecordKey);
                if (!string.Equals(record.Did, snapshot.Did, StringComparison.Ordinal)
                    || !IsExactCollection(record.Collection)
                    || !IsValidTid(record.RecordKey)
                    || !accepted.Add(identity)
                    || !present.Contains(identity)
                    || record.TombstonedAt is not null
                    || (record.Collection == EventCollection) != (item.EventProjection is not null))
                {
                    return false;
                }
            }
        }

        return snapshotDids.SetEquals(scannedDids);
    }

    private static bool IsValidDid(string did) =>
        did is { Length: > 4 and <= 255 }
        && did.StartsWith("did:", StringComparison.Ordinal)
        && !did.Any(character => char.IsWhiteSpace(character) || char.IsControl(character));

    private static bool IsExactCollection(string collection) =>
        collection is EventCollection or RsvpCollection;

    private static bool IsValidProtocolRecordKey(string recordKey) =>
        recordKey is { Length: > 0 and <= 512 }
        && recordKey is not "." and not ".."
        && recordKey.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or ':' or '~');

    private static bool IsValidTid(string recordKey) =>
        recordKey is { Length: 13 }
        && TidAlphabet.IndexOf(recordKey[0], StringComparison.Ordinal) is >= 0 and <= 15
        && recordKey.All(TidAlphabet.Contains);

    private static void ApplySnapshotRecord(AtprotoRecord canonical, AtprotoRecord supplied)
    {
        bool isEcho = canonical.Direction is AtprotoRecordDirection.Outbound or AtprotoRecordDirection.Reconciled;
        canonical.Direction = isEcho ? AtprotoRecordDirection.Reconciled : AtprotoRecordDirection.Inbound;
        canonical.Provenance = isEcho
            ? AtprotoRecordProvenance.JetstreamEcho
            : AtprotoRecordProvenance.Jetstream;
        canonical.Cid = supplied.Cid;
        canonical.Uri = supplied.Uri;
        canonical.RecordJson = supplied.RecordJson;
        canonical.RecordHash = supplied.RecordHash;
        canonical.SubjectUri = supplied.SubjectUri;
        canonical.SubjectCid = supplied.SubjectCid;
        canonical.IndexedAt = supplied.IndexedAt;
    }

    private void ApplySnapshotProjection(
        AtprotoRecord canonical,
        AtprotoEventProjection? supplied,
        IDictionary<Guid, AtprotoEventProjection> projections,
        AtprotoPdsSnapshotApplyRequest request)
    {
        if (canonical.Collection != EventCollection || supplied is null)
        {
            return;
        }

        if (!projections.TryGetValue(canonical.Id, out AtprotoEventProjection? existing))
        {
            supplied.AtprotoRecordId = canonical.Id;
            supplied.SourceVersion = request.SnapshotVersion;
            supplied.MaterializedAt = request.ObservedAt;
            projections.Add(canonical.Id, supplied);
            _dbContext.AtprotoEventProjections.Add(supplied);
            return;
        }

        existing.Name = supplied.Name;
        existing.Description = supplied.Description;
        existing.CreatedAt = supplied.CreatedAt;
        existing.StartsAt = supplied.StartsAt;
        existing.EndsAt = supplied.EndsAt;
        existing.Mode = supplied.Mode;
        existing.Status = supplied.Status;
        existing.RsvpExpected = supplied.RsvpExpected;
        existing.LocationSummary = supplied.LocationSummary;
        existing.SourceUrl = supplied.SourceUrl;
        existing.SourceVersion = request.SnapshotVersion;
        existing.MaterializedAt = request.ObservedAt;
    }

    private void ReconcilePresentations(
        AtprotoRecord canonical,
        IReadOnlySet<Guid> visibleTenantIds,
        ICollection<AtprotoRecordTenantPresentation> presentations,
        IDictionary<(Guid TenantId, Guid RecordId), AtprotoRecordTenantPresentation> presentationByKey,
        AtprotoPdsSnapshotApplyRequest request)
    {
        foreach (AtprotoRecordTenantPresentation presentation in presentations
                     .Where(value => value.AtprotoRecordId == canonical.Id))
        {
            presentation.IsVisible = visibleTenantIds.Contains(presentation.TenantId);
            presentation.SourceVersion = request.SnapshotVersion;
            presentation.EvaluatedAt = request.ObservedAt;
        }

        foreach (Guid tenantId in visibleTenantIds)
        {
            if (presentationByKey.ContainsKey((tenantId, canonical.Id)))
            {
                continue;
            }

            var presentation = new AtprotoRecordTenantPresentation
            {
                TenantId = tenantId,
                AtprotoRecordId = canonical.Id,
                IsVisible = true,
                SourceVersion = request.SnapshotVersion,
                EvaluatedAt = request.ObservedAt
            };
            presentations.Add(presentation);
            presentationByKey.Add((tenantId, canonical.Id), presentation);
            _dbContext.AtprotoRecordTenantPresentations.Add(presentation);
        }
    }

    private static void HidePresentations(
        Guid recordId,
        IEnumerable<AtprotoRecordTenantPresentation> presentations,
        AtprotoPdsSnapshotApplyRequest request)
    {
        foreach (AtprotoRecordTenantPresentation presentation in presentations
                     .Where(value => value.AtprotoRecordId == recordId))
        {
            presentation.IsVisible = false;
            presentation.SourceVersion = request.SnapshotVersion;
            presentation.EvaluatedAt = request.ObservedAt;
        }
    }

    private async Task RollbackFailedAttemptAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (ObjectDisposedException)
        {
            // Preserve the original failure if the provider already disposed the transaction.
        }
        catch (InvalidOperationException)
        {
            // Preserve the original failure if the transaction is no longer rollback-ready.
        }
        catch (DbException)
        {
            // Cleanup must not replace the original operation failure.
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }

    private async Task AcquireConsumerLockAsync(string service, CancellationToken cancellationToken)
    {
        _ = await RelationalNamedLock.AcquireTransactionAsync(
            _dbContext,
            $"atproto-jetstream:{service}",
            cancellationToken);
    }

    private async Task<bool> HasCurrentFenceAtCommitAsync(
        AtprotoJetstreamClaim claim,
        CancellationToken cancellationToken) =>
        await AtprotoJetstreamCommitFence.IsCurrentAsync(
            _dbContext,
            claim,
            cancellationToken);

    private async Task<bool> ApplyRecordAsync(
        AtprotoJetstreamApplyRequest request,
        ICollection<FileStorageWriteResult> consumed,
        CancellationToken cancellationToken)
    {
        var incoming = request.Record!;
        var canonical = await _dbContext.AtprotoRecords.SingleOrDefaultAsync(value =>
            value.Did == incoming.Did &&
            value.Collection == incoming.Collection &&
            value.RecordKey == incoming.RecordKey,
            cancellationToken);
        if (canonical is not null && incoming.SourceVersion < canonical.SourceVersion)
        {
            return true;
        }

        if (canonical is not null && incoming.SourceVersion == canonical.SourceVersion)
        {
            if (canonical.Collection == EventCollection)
            {
                await ApplyEventImportsAsync(
                    canonical,
                    request.EventImports,
                    request.ObservedAt,
                    TenantFilterBypassReasons.AtprotoJetstreamGlobalMaterialization,
                    updateExisting: false,
                    consumed,
                    cancellationToken);
            }

            return true;
        }

        if (canonical is null)
        {
            canonical = incoming;
            if (canonical.Id == Guid.Empty)
            {
                canonical.Id = Guid.CreateVersion7();
            }
            canonical.Direction = AtprotoRecordDirection.Inbound;
            canonical.Provenance = AtprotoRecordProvenance.Jetstream;
            await _dbContext.AtprotoRecords.AddAsync(canonical, cancellationToken);
        }
        else
        {
            canonical.Direction = canonical.Direction is AtprotoRecordDirection.Outbound or AtprotoRecordDirection.Reconciled
                ? AtprotoRecordDirection.Reconciled
                : AtprotoRecordDirection.Inbound;
            canonical.Provenance = canonical.Direction == AtprotoRecordDirection.Reconciled
                ? AtprotoRecordProvenance.JetstreamEcho
                : AtprotoRecordProvenance.Jetstream;
            canonical.Cid = incoming.Cid;
            canonical.Uri = incoming.Uri;
            canonical.RecordJson = incoming.RecordJson;
            canonical.RecordHash = incoming.RecordHash;
            canonical.SubjectUri = incoming.SubjectUri;
            canonical.SubjectCid = incoming.SubjectCid;
            canonical.IndexedAt = incoming.IndexedAt;
            canonical.SourceVersion = incoming.SourceVersion;
            canonical.SourceCursor = request.NextCursor;
            canonical.UpdatedAt = request.ObservedAt;
            canonical.TombstonedAt = incoming.TombstonedAt;
        }

        canonical.SourceCursor = request.NextCursor;
        canonical.UpdatedAt = request.ObservedAt;

        if (canonical.Collection == "community.lexicon.calendar.event")
        {
            await ApplyEventProjectionAsync(canonical, request, cancellationToken);
            await ApplyEventImportsAsync(
                canonical,
                request.EventImports,
                request.ObservedAt,
                TenantFilterBypassReasons.AtprotoJetstreamGlobalMaterialization,
                updateExisting: true,
                consumed,
                cancellationToken);
        }

        foreach (var supplied in request.Presentations)
        {
            var presentation = await _dbContext.AtprotoRecordTenantPresentations
                .IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoJetstreamGlobalMaterialization)
                .SingleOrDefaultAsync(value =>
                    value.TenantId == supplied.TenantId && value.AtprotoRecordId == canonical.Id,
                    cancellationToken);
            if (presentation is null)
            {
                supplied.AtprotoRecordId = canonical.Id;
                supplied.SourceVersion = canonical.SourceVersion;
                supplied.EvaluatedAt = request.ObservedAt;
                await _dbContext.AtprotoRecordTenantPresentations.AddAsync(supplied, cancellationToken);
            }
            else
            {
                presentation.IsVisible = supplied.IsVisible && canonical.TombstonedAt is null;
                presentation.SourceVersion = canonical.SourceVersion;
                presentation.EvaluatedAt = request.ObservedAt;
            }
        }

        if (canonical.TombstonedAt is not null)
        {
            await _dbContext.AtprotoRecordTenantPresentations
                .IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoJetstreamGlobalMaterialization)
                .Where(value => value.AtprotoRecordId == canonical.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(value => value.IsVisible, false)
                    .SetProperty(value => value.EvaluatedAt, request.ObservedAt), cancellationToken);

            if (canonical.Uri is not null)
            {
                var dependentIds = _dbContext.AtprotoRecords
                    .Where(value => value.SubjectUri == canonical.Uri)
                    .Select(value => value.Id);
                await _dbContext.AtprotoRecordTenantPresentations
                    .IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoJetstreamGlobalMaterialization)
                    .Where(value => dependentIds.Contains(value.AtprotoRecordId))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(value => value.IsVisible, false)
                        .SetProperty(value => value.EvaluatedAt, request.ObservedAt), cancellationToken);
            }
        }

        return true;
    }

    private async Task ApplyEventImportsAsync(
        AtprotoRecord canonical,
        IReadOnlyList<AtprotoFederatedEventImportPlan> imports,
        DateTime observedAt,
        string filterBypassReason,
        bool updateExisting,
        ICollection<FileStorageWriteResult> consumed,
        CancellationToken cancellationToken,
        bool forceTombstone = false)
    {
        if (canonical.Collection != EventCollection)
        {
            return;
        }

        if (canonical.TombstonedAt is not null || forceTombstone)
        {
            DateTime deletedAt = canonical.TombstonedAt ?? observedAt;
            List<Explore.Domain.Event> importedEvents = await _dbContext.Events
                .IgnoreAllFilters(filterBypassReason)
                .Where(value => value.AtprotoRecordId == canonical.Id)
                .ToListAsync(cancellationToken);
            foreach (Explore.Domain.Event importedEvent in importedEvents)
            {
                List<StorageObject> images = await _dbContext.StorageObjects
                    .IgnoreAllFilters(filterBypassReason)
                    .Where(value =>
                        value.TenantId == importedEvent.TenantId
                        && (value.Id == importedEvent.FeaturedImageId
                            || (value.OwningResourceKind == ResourceKinds.Event
                                && value.OwningResourceId == importedEvent.Id)))
                    .ToListAsync(cancellationToken);
                foreach (StorageObject image in images)
                {
                    image.RequestDelete();
                    image.UpdatedAt = observedAt;
                }

                importedEvent.FeaturedImageId = null;
                importedEvent.IsDeleted = true;
                importedEvent.DeletedAt = deletedAt;
                importedEvent.UpdatedAt = observedAt;
                List<EventSession> sessions = await _dbContext.EventSessions
                    .IgnoreAllFilters(filterBypassReason)
                    .Where(value =>
                        value.TenantId == importedEvent.TenantId
                        && value.EventId == importedEvent.Id)
                    .ToListAsync(cancellationToken);
                foreach (EventSession session in sessions)
                {
                    session.IsDeleted = true;
                    session.DeletedAt = deletedAt;
                    session.UpdatedAt = observedAt;
                }
            }

            return;
        }

        foreach (AtprotoFederatedEventImportPlan import in imports)
        {
            AtprotoIdentity? identity = _dbContext.ChangeTracker.Entries<AtprotoIdentity>()
                .Where(entry => entry.State != EntityState.Deleted)
                .Select(entry => entry.Entity)
                .SingleOrDefault(value => string.Equals(value.Did, import.Did, StringComparison.Ordinal));
            identity ??= await _dbContext.AtprotoIdentities
                .IgnoreAllFilters(filterBypassReason)
                .Include(value => value.Actor)
                    .ThenInclude(actor => actor.Pii)
                .SingleOrDefaultAsync(value => value.Did == import.Did, cancellationToken);
            if (identity is null)
            {
                var externalSubject = new ExternalActorSubject
                {
                    Id = Guid.CreateVersion7(),
                    FirstObservedAt = observedAt,
                    LastObservedAt = observedAt,
                    CreatedAt = observedAt
                };
                var newActor = new Actor
                {
                    Id = Guid.CreateVersion7(),
                    ActorTypeId = (int)ActorTypeEnum.Bot,
                    ActorType = null!,
                    ExternalActorSubjectId = externalSubject.Id,
                    ExternalActorSubject = externalSubject,
                    Pii = new ActorPii
                    {
                        DisplayName = import.Did
                    },
                    CreatedAt = observedAt
                };
                identity = new AtprotoIdentity(AtprotoDid.Parse(import.Did));
                identity.Id = Guid.CreateVersion7();
                identity.ActorId = newActor.Id;
                identity.Actor = newActor;
                identity.PdsHost = string.Empty;
                identity.IsActive = false;
                identity.LastResolvedAt = observedAt;
                identity.LastSeenAt = observedAt;
                identity.CreatedAt = observedAt;
                await _dbContext.AtprotoIdentities.AddAsync(identity, cancellationToken);
            }
            else
            {
                identity.LastSeenAt = observedAt;
            }

            Actor actor = identity.Actor;

            Explore.Domain.Event? importedEvent = _dbContext.ChangeTracker.Entries<Explore.Domain.Event>()
                .Where(entry => entry.State != EntityState.Deleted)
                .Select(entry => entry.Entity)
                .SingleOrDefault(value =>
                    value.TenantId == import.TenantId
                    && value.AtprotoRecordId == canonical.Id);
            importedEvent ??= await _dbContext.Events
                .IgnoreAllFilters(filterBypassReason)
                .SingleOrDefaultAsync(value =>
                    value.TenantId == import.TenantId
                    && value.AtprotoRecordId == canonical.Id,
                    cancellationToken);
            bool preserveHealthyEvent = importedEvent is not null && !updateExisting;
            EventStatusEnum synchronizedEventStatus = MapEventStatus(import.Status);
            bool isNewEvent = importedEvent is null;
            if (importedEvent is null)
            {
                importedEvent = new Explore.Domain.Event(synchronizedEventStatus)
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = import.TenantId,
                    Tenant = null!,
                    ActorId = actor.Id,
                    Actor = actor,
                    EventProvenanceTypeId = (int)EventProvenanceTypeEnum.Federated,
                    Title = import.Name,
                    Slug = SlugGenerator.FromTitle(import.Name, "event"),
                    VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                    VisibilityType = null!,
                    EventStatus = null!,
                    EventFormatId = MapEventFormat(import.Mode),
                    EventFormat = null!,
                    AtprotoRecordId = canonical.Id,
                    AtprotoRecord = canonical,
                    EventTimeZoneId = import.TimeZoneId,
                    Timezone = import.TimeZoneId,
                    CreatedAt = import.CreatedAt.UtcDateTime
                };
                await _dbContext.Events.AddAsync(importedEvent, cancellationToken);
            }

            if (!preserveHealthyEvent)
            {
                importedEvent.ActorId = actor.Id;
                importedEvent.Actor = actor;
                importedEvent.EventProvenanceTypeId = (int)EventProvenanceTypeEnum.Federated;
                importedEvent.Title = import.Name;
                importedEvent.Content = import.Description;
                importedEvent.Description = SummarizeDescription(import.Description);
                importedEvent.EventFormatId = MapEventFormat(import.Mode);
                importedEvent.EventTimeZoneId = import.TimeZoneId;
                importedEvent.Timezone = import.TimeZoneId;
                importedEvent.AtprotoRecordId = canonical.Id;
                importedEvent.AtprotoRecord = canonical;
                importedEvent.ProvenanceSource = "atproto";
                importedEvent.ProvenanceExternalId = import.AtUri.Length <= 200
                    ? import.AtUri
                    : import.AtUri[..200];
                importedEvent.CreatedAt = import.CreatedAt.UtcDateTime;
                importedEvent.UpdatedAt = observedAt;
                importedEvent.IsDeleted = false;
                importedEvent.DeletedAt = null;
                importedEvent.DeletedBy = null;

                await ReconcileOriginalSourceActionAsync(
                    importedEvent,
                    import.SourceUrl,
                    observedAt,
                    filterBypassReason,
                    cancellationToken);
            }

            await EnsureParticipationConfigurationAsync(
                importedEvent,
                import,
                applyAuthoritativeRefresh: !preserveHealthyEvent,
                filterBypassReason,
                cancellationToken);

            await ApplyThumbnailAsync(
                import,
                importedEvent,
                actor,
                observedAt,
                filterBypassReason,
                updateExisting,
                consumed,
                cancellationToken);

            EventSession? session = _dbContext.ChangeTracker.Entries<EventSession>()
                .Where(entry => entry.State != EntityState.Deleted)
                .Select(entry => entry.Entity)
                .SingleOrDefault(value =>
                    value.TenantId == import.TenantId
                    && value.EventId == importedEvent.Id);
            session ??= await _dbContext.EventSessions
                .IgnoreAllFilters(filterBypassReason)
                .SingleOrDefaultAsync(value =>
                    value.TenantId == import.TenantId
                    && value.EventId == importedEvent.Id,
                    cancellationToken);
            EventSessionStatusEnum synchronizedSessionStatus = MapSessionStatus(import.Status);
            bool isNewSession = session is null;
            if (session is null)
            {
                session = CreateFederatedSession(importedEvent, import, synchronizedSessionStatus);
                await _dbContext.EventSessions.AddAsync(session, cancellationToken);
            }
            else if (preserveHealthyEvent && !session.IsDeleted)
            {
                continue;
            }

            session.Title = import.Name;
            session.Description = null;
            session.CreatedAt = import.CreatedAt.UtcDateTime;
            session.UpdatedAt = observedAt;
            session.IsDeleted = false;
            session.DeletedAt = null;
            session.DeletedBy = null;
            if (!isNewSession && IsSchedulableFederatedStatus(synchronizedSessionStatus))
            {
                session.SynchronizeFederatedLifecycle(synchronizedSessionStatus, observedAt);
                ApplyFederatedScheduleRefresh(session, import);
            }
            else if (!isNewSession)
            {
                ApplyNonSchedulableFederatedSchedule(session, import);
                session.SynchronizeFederatedLifecycle(synchronizedSessionStatus, observedAt);
            }

            if (!importedEvent.Sessions.Contains(session))
            {
                importedEvent.Sessions.Add(session);
            }
            importedEvent.RecalculateScheduleSummaryFromSessions();
            if (!isNewEvent)
            {
                importedEvent.SynchronizeFederatedLifecycle(synchronizedEventStatus, observedAt);
            }
        }
    }

    private static EventSession CreateFederatedSession(
        Explore.Domain.Event importedEvent,
        AtprotoFederatedEventImportPlan import,
        EventSessionStatusEnum status)
    {
        var session = new EventSession(status)
        {
            Id = Guid.CreateVersion7(),
            TenantId = import.TenantId,
            Tenant = null!,
            EventId = importedEvent.Id,
            Event = importedEvent,
            Slug = SlugGenerator.FromTitle($"{import.Name}-session-1", "session"),
            CreatedAt = import.CreatedAt.UtcDateTime
        };
        if (IsSchedulableFederatedStatus(status))
        {
            ApplyFederatedScheduleRefresh(session, import);
        }
        else
        {
            ApplyNonSchedulableFederatedSchedule(session, import);
        }
        return session;
    }

    private static void ApplyFederatedScheduleRefresh(
        EventSession session,
        AtprotoFederatedEventImportPlan import)
    {
        if (import.StartsAt is null)
        {
            session.EndTimeType = SessionEndTimeType.Fixed;
            session.Unschedule();
            return;
        }

        if (import.EndsAt is null)
        {
            session.ScheduleOpenEnded(
                import.StartsAt.Value,
                import.TimeZoneId,
                ScheduleProjectionCalculator);
            return;
        }

        session.Reschedule(
            UtcInstantRange.Create(import.StartsAt.Value, import.EndsAt.Value),
            import.TimeZoneId,
            ScheduleProjectionCalculator);
    }

    private static void ApplyNonSchedulableFederatedSchedule(
        EventSession session,
        AtprotoFederatedEventImportPlan import)
    {
        if (import.StartsAt is null)
        {
            if (import.EndsAt is not null)
            {
                throw new ArgumentException("Federated event end time requires a start time.", nameof(import));
            }

            session.StartTime = null;
            session.EndTime = null;
            session.EndTimeType = SessionEndTimeType.Fixed;
            session.ReprojectLocalTimes(import.TimeZoneId, ScheduleProjectionCalculator);
            return;
        }

        if (import.EndsAt is not null && import.EndsAt <= import.StartsAt)
        {
            throw new ArgumentException("Federated event end time must be after its start time.", nameof(import));
        }

        session.StartTime = import.StartsAt.Value.ToUniversalTime();
        session.EndTime = import.EndsAt?.ToUniversalTime();
        session.EndTimeType = import.EndsAt is null
            ? SessionEndTimeType.OpenEnded
            : SessionEndTimeType.Fixed;
        session.ReprojectLocalTimes(import.TimeZoneId, ScheduleProjectionCalculator);
    }

    private static bool IsSchedulableFederatedStatus(EventSessionStatusEnum status) => status is
        EventSessionStatusEnum.Draft or
        EventSessionStatusEnum.Published;

    private async Task EnsureParticipationConfigurationAsync(
        Explore.Domain.Event importedEvent,
        AtprotoFederatedEventImportPlan import,
        bool applyAuthoritativeRefresh,
        string filterBypassReason,
        CancellationToken cancellationToken)
    {
        EventParticipationConfiguration? configuration = _dbContext.ChangeTracker
            .Entries<EventParticipationConfiguration>()
            .Where(entry => entry.State != EntityState.Deleted)
            .Select(entry => entry.Entity)
            .SingleOrDefault(value => value.Id == importedEvent.Id && value.TenantId == importedEvent.TenantId);
        configuration ??= await _dbContext.EventParticipationConfigurations
            .IgnoreAllFilters(filterBypassReason)
            .SingleOrDefaultAsync(
                value => value.Id == importedEvent.Id && value.TenantId == importedEvent.TenantId,
                cancellationToken);

        if (configuration is not null && !applyAuthoritativeRefresh)
        {
            return;
        }

        if (configuration is null)
        {
            configuration = EventParticipationConfiguration.Create(
                importedEvent.Id,
                importedEvent.TenantId,
                import.ParticipationConfiguration.ParticipationHandlingModeId,
                import.ParticipationConfiguration.AdvanceRegistrationObligationId,
                import.ParticipationConfiguration.IdentityAccessModeId,
                import.ParticipationConfiguration.GuestRecoveryPolicy,
                now: import.CreatedAt.UtcDateTime);
            await _dbContext.EventParticipationConfigurations.AddAsync(configuration, cancellationToken);
            return;
        }

        configuration.Reconfigure(
            import.ParticipationConfiguration.ParticipationHandlingModeId,
            import.ParticipationConfiguration.AdvanceRegistrationObligationId,
            import.ParticipationConfiguration.IdentityAccessModeId,
            import.ParticipationConfiguration.GuestRecoveryPolicy);
    }

    private async Task ReconcileOriginalSourceActionAsync(
        Explore.Domain.Event importedEvent,
        string? sourceUrl,
        DateTime observedAt,
        string filterBypassReason,
        CancellationToken cancellationToken)
    {
        EventPublicAction? action = _dbContext.ChangeTracker.Entries<EventPublicAction>()
            .Where(entry => entry.State != EntityState.Deleted)
            .Select(entry => entry.Entity)
            .SingleOrDefault(value =>
                value.TenantId == importedEvent.TenantId
                && value.EventId == importedEvent.Id
                && value.EventPublicActionKindId == (int)EventPublicActionKindEnum.OriginalSource);
        action ??= await _dbContext.EventPublicActions
            .IgnoreAllFilters(filterBypassReason)
            .SingleOrDefaultAsync(value =>
                value.TenantId == importedEvent.TenantId
                && value.EventId == importedEvent.Id
                && value.EventPublicActionKindId == (int)EventPublicActionKindEnum.OriginalSource,
                cancellationToken);

        if (sourceUrl is null)
        {
            if (action is not null)
            {
                action.IsDeleted = true;
                action.DeletedAt = observedAt;
                action.UpdatedAt = observedAt;
            }

            return;
        }

        ExternalActionUrl destination = ExternalActionUrl.Create(sourceUrl);
        if (action is null)
        {
            action = new EventPublicAction
            {
                Id = Guid.CreateVersion7(),
                TenantId = importedEvent.TenantId,
                EventId = importedEvent.Id,
                Event = importedEvent,
                EventPublicActionKindId = (int)EventPublicActionKindEnum.OriginalSource,
                HealthStateId = (int)EventPublicActionHealthStateEnum.PendingReview,
                SortOrder = 0,
                IsPrimary = false,
                CreatedAt = observedAt,
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            action.SetDestination(destination);
            await _dbContext.EventPublicActions.AddAsync(action, cancellationToken);
            return;
        }

        if (!string.Equals(action.Url, destination.Value, StringComparison.Ordinal))
        {
            action.SetDestination(destination);
            action.HealthStateId = (int)EventPublicActionHealthStateEnum.PendingReview;
        }

        action.IsDeleted = false;
        action.DeletedAt = null;
        action.DeletedBy = null;
        action.UpdatedAt = observedAt;
    }

    private async Task ApplyThumbnailAsync(
        AtprotoFederatedEventImportPlan import,
        Explore.Domain.Event importedEvent,
        Actor actor,
        DateTime observedAt,
        string filterBypassReason,
        bool updateExisting,
        ICollection<FileStorageWriteResult> consumed,
        CancellationToken cancellationToken)
    {
        StorageObject? existing = importedEvent.FeaturedImageId is null
            ? null
            : await _dbContext.StorageObjects
                .IgnoreAllFilters(filterBypassReason)
                .SingleOrDefaultAsync(
                    value => value.TenantId == import.TenantId
                        && value.Id == importedEvent.FeaturedImageId,
                    cancellationToken);

        if (import.Thumbnail is null)
        {
            if (updateExisting && existing is not null)
            {
                existing.RequestDelete();
                existing.UpdatedAt = observedAt;
                importedEvent.FeaturedImageId = null;
            }

            return;
        }

        if (import.StagedThumbnail is null)
        {
            return;
        }

        FileStorageWriteResult staged = import.StagedThumbnail;
        if (!TryValidateStagedThumbnail(import.Thumbnail, staged, out string? mimeType, out string? extension))
        {
            return;
        }

        string provenanceUri = $"at://{import.Thumbnail.Did}/blob/{import.Thumbnail.Cid}";
        if (existing is not null
            && existing.LifecycleState == StorageObjectLifecycleStates.Active
            && string.Equals(existing.Uri, provenanceUri, StringComparison.Ordinal))
        {
            return;
        }

        if (existing is not null)
        {
            existing.RequestDelete();
            existing.UpdatedAt = observedAt;
        }

        string displayName = $"{import.Thumbnail.Cid}{extension}";
        var image = new StorageObject
        {
            Id = Guid.CreateVersion7(),
            FileTypeId = (int)FileTypeEnum.Image,
            FileType = null!,
            Uri = provenanceUri,
            ObjectKey = staged.ObjectKey,
            Provider = staged.Provider,
            FullName = displayName,
            SafeDisplayName = displayName,
            Extension = extension,
            ContentType = mimeType,
            Sha256Checksum = staged.Sha256Checksum
                ?? throw new InvalidOperationException("Staged thumbnail checksum is missing."),
            Size = staged.SizeBytes,
            Visibility = StorageObjectVisibilities.PublicImage,
            Purpose = StorageObjectPurposes.EventImage,
            LifecycleState = StorageObjectLifecycleStates.Active,
            OwningResourceKind = ResourceKinds.Event,
            OwningResourceId = importedEvent.Id,
            TenantId = import.TenantId,
            Tenant = null!,
            ActorId = actor.Id,
            Actor = actor,
            CreatedAt = observedAt
        };
        if (!SafeRasterContentPolicy.IsSafePublicImageMetadata(image))
        {
            return;
        }

        await _dbContext.StorageObjects.AddAsync(image, cancellationToken);
        importedEvent.FeaturedImageId = image.Id;
        consumed.Add(staged);
    }

    private static bool TryValidateStagedThumbnail(
        AtprotoThumbnailBlobCandidate thumbnail,
        FileStorageWriteResult staged,
        [NotNullWhen(true)] out string? mimeType,
        [NotNullWhen(true)] out string? extension)
    {
        mimeType = null;
        extension = null;
        if (!SafeRasterContentPolicy.TryNormalizeMimeType(thumbnail.MimeType, out string? normalizedMimeType)
            || !string.Equals(thumbnail.MimeType, normalizedMimeType, StringComparison.Ordinal)
            || !string.Equals(staged.ContentType, normalizedMimeType, StringComparison.Ordinal)
            || thumbnail.Size <= 0
            || staged.SizeBytes != thumbnail.Size
            || string.IsNullOrWhiteSpace(staged.Provider)
            || string.IsNullOrWhiteSpace(staged.ObjectKey)
            || !TryReadSha256Checksum(thumbnail.Cid, out string? expectedChecksum)
            || !string.Equals(staged.Sha256Checksum, expectedChecksum, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        extension = SafeRasterExtensions.FirstOrDefault(candidate =>
            SafeRasterContentPolicy.MatchesExtension(normalizedMimeType, candidate));
        if (extension is null)
        {
            return false;
        }

        mimeType = normalizedMimeType;
        return true;
    }

    private static bool TryReadSha256Checksum(string cid, out string? checksum)
    {
        checksum = null;
        if (cid.Length != 59 || cid[0] != 'b')
        {
            return false;
        }

        Span<byte> decoded = stackalloc byte[36];
        int decodedLength = 0;
        int bitBuffer = 0;
        int bitCount = 0;
        foreach (char character in cid.AsSpan(1))
        {
            int value = character switch
            {
                >= 'a' and <= 'z' => character - 'a',
                >= '2' and <= '7' => character - '2' + 26,
                _ => -1
            };
            if (value < 0)
            {
                return false;
            }

            bitBuffer = (bitBuffer << 5) | value;
            bitCount += 5;
            if (bitCount < 8)
            {
                continue;
            }

            bitCount -= 8;
            if (decodedLength >= decoded.Length)
            {
                return false;
            }

            decoded[decodedLength++] = (byte)(bitBuffer >> bitCount);
            bitBuffer &= (1 << bitCount) - 1;
        }

        if (decodedLength != decoded.Length
            || bitBuffer != 0
            || decoded[0] != 0x01
            || decoded[1] is not (0x55 or 0x71)
            || decoded[2] != 0x12
            || decoded[3] != 0x20)
        {
            return false;
        }

        checksum = Convert.ToHexStringLower(decoded[4..]);
        return true;
    }

    private static int MapEventFormat(string? mode) =>
        mode switch
        {
            "#virtual" => (int)EventFormatEnum.Digital,
            "#hybrid" => (int)EventFormatEnum.Hybrid,
            _ => (int)EventFormatEnum.Local
        };

    private static EventStatusEnum MapEventStatus(string? status) =>
        status switch
        {
            null or "#scheduled" or "#rescheduled" => EventStatusEnum.Published,
            "#cancelled" => EventStatusEnum.Cancelled,
            _ => EventStatusEnum.Draft
        };

    private static EventSessionStatusEnum MapSessionStatus(string? status) =>
        status switch
        {
            null or "#scheduled" or "#rescheduled" => EventSessionStatusEnum.Published,
            "#cancelled" => EventSessionStatusEnum.Cancelled,
            _ => EventSessionStatusEnum.Draft
        };

    private static string? SummarizeDescription(string? description) =>
        description is null
            ? null
            : string.Concat(description.EnumerateRunes().Take(150).Select(rune => rune.ToString()));

    private async Task ApplyEventProjectionAsync(
        AtprotoRecord canonical,
        AtprotoJetstreamApplyRequest request,
        CancellationToken cancellationToken)
    {
        AtprotoEventProjection? existing = await _dbContext.AtprotoEventProjections
            .SingleOrDefaultAsync(value => value.AtprotoRecordId == canonical.Id, cancellationToken);
        if (canonical.TombstonedAt is not null)
        {
            if (existing is not null)
            {
                _dbContext.AtprotoEventProjections.Remove(existing);
            }
            return;
        }

        AtprotoEventProjection? supplied = request.EventProjection;
        if (supplied is null || supplied.SourceVersion != canonical.SourceVersion)
        {
            return;
        }

        if (existing is null)
        {
            supplied.AtprotoRecordId = canonical.Id;
            await _dbContext.AtprotoEventProjections.AddAsync(supplied, cancellationToken);
            return;
        }

        existing.Name = supplied.Name;
        existing.Description = supplied.Description;
        existing.CreatedAt = supplied.CreatedAt;
        existing.StartsAt = supplied.StartsAt;
        existing.EndsAt = supplied.EndsAt;
        existing.Mode = supplied.Mode;
        existing.Status = supplied.Status;
        existing.RsvpExpected = supplied.RsvpExpected;
        existing.LocationSummary = supplied.LocationSummary;
        existing.SourceUrl = supplied.SourceUrl;
        existing.SourceVersion = supplied.SourceVersion;
        existing.MaterializedAt = supplied.MaterializedAt;
    }

    /// <summary>
    /// Retires every record federated in from a deleted or deactivated upstream account.
    /// <para>
    /// Scoped to <see cref="AtprotoRecordDirection.Inbound"/> so locally authored outbound records are
    /// never touched by a remote account signal. Records are tombstoned rather than deleted, keeping the
    /// canonical row available for idempotent replay, while projections are removed and presentations
    /// hidden so nothing stays publicly visible.
    /// </para>
    /// </summary>
    private async Task PurgeAccountAsync(
        AtprotoJetstreamApplyRequest request,
        CancellationToken cancellationToken)
    {
        AtprotoAccountPurge purge = request.AccountPurge!;
        List<AtprotoRecord> records = await _dbContext.AtprotoRecords
            .Where(value => value.Did == purge.Did && value.Direction == AtprotoRecordDirection.Inbound)
            .ToListAsync(cancellationToken);
        if (records.Count == 0)
        {
            return;
        }

        Guid[] recordIds = [.. records.Select(value => value.Id)];
        List<AtprotoEventProjection> projections = await _dbContext.AtprotoEventProjections
            .Where(value => recordIds.Contains(value.AtprotoRecordId))
            .ToListAsync(cancellationToken);
        _dbContext.AtprotoEventProjections.RemoveRange(projections);

        await _dbContext.AtprotoRecordTenantPresentations
            .IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoJetstreamGlobalMaterialization)
            .Where(value => recordIds.Contains(value.AtprotoRecordId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.IsVisible, false)
                .SetProperty(value => value.SourceVersion, purge.SourceVersion)
                .SetProperty(value => value.EvaluatedAt, request.ObservedAt), cancellationToken);

        foreach (AtprotoRecord record in records)
        {
            record.TombstonedAt ??= request.ObservedAt;
            record.UpdatedAt = request.ObservedAt;
        }
    }

    private async Task InvalidateEventProjectionAsync(
        AtprotoJetstreamApplyRequest request,
        CancellationToken cancellationToken)
    {
        AtprotoEventProjectionInvalidation invalidation = request.EventProjectionInvalidation!;
        AtprotoRecord? canonical = await _dbContext.AtprotoRecords.SingleOrDefaultAsync(value =>
            value.Did == invalidation.Did
            && value.Collection == invalidation.Collection
            && value.RecordKey == invalidation.RecordKey,
            cancellationToken);
        if (canonical is null || invalidation.SourceVersion <= canonical.SourceVersion)
        {
            return;
        }

        AtprotoEventProjection? projection = await _dbContext.AtprotoEventProjections
            .SingleOrDefaultAsync(value => value.AtprotoRecordId == canonical.Id, cancellationToken);
        if (projection is not null)
        {
            _dbContext.AtprotoEventProjections.Remove(projection);
        }

        await _dbContext.AtprotoRecordTenantPresentations
            .IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoJetstreamGlobalMaterialization)
            .Where(value => value.AtprotoRecordId == canonical.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.IsVisible, false)
                .SetProperty(value => value.SourceVersion, invalidation.SourceVersion)
                .SetProperty(value => value.EvaluatedAt, request.ObservedAt), cancellationToken);
    }
}
