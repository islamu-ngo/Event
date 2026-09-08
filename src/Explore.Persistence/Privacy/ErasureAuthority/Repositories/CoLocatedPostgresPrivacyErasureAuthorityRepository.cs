using System.Data;
using Explore.Application.Configuration;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Explore.Persistence.Privacy.ErasureAuthority.Repositories;

public sealed class CoLocatedPostgresPrivacyErasureAuthorityRepository(
    CoLocatedPrivacyErasureAuthorityDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<PrivacyErasureOptions> options)
    : IPrivacyErasureAuthority, IPrivacyErasureAuthorityMaintenance
{
    public const int MaximumReadBatchSize = 500;

    public async Task<PrivacyErasureAuthorityState> GetStateAsync(
        CancellationToken cancellationToken = default)
    {
        PrivacyErasureCounter? counter = await dbContext.AuthorityCounters
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        return counter?.GetState() ?? new PrivacyErasureAuthorityState(0, 0);
    }

    public async Task<PrivacyErasureIntent> AppendAsync(
        PrivacyErasureRequest intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        _ = await RelationalNamedLock.AcquireTransactionAsync(
            dbContext,
            "privacy-erasure-authority-counter",
            cancellationToken);
        PrivacyErasureCounter? counter = await dbContext.AuthorityCounters
            .SingleOrDefaultAsync(cancellationToken);
        if (counter is null)
        {
            counter = PrivacyErasureCounter.Start();
            dbContext.AuthorityCounters.Add(counter);
        }
        PrivacyErasureIntent? existing = await dbContext.ErasureIntents
            .SingleOrDefaultAsync(item => item.IntentId == intent.IntentId, cancellationToken);
        if (existing is not null)
        {
            EnsureSamePayload(existing, intent);
            await transaction.CommitAsync(cancellationToken);
            return existing;
        }

        long sequence = counter.AllocateNext();
        DateTime recordedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var fact = PrivacyErasureIntent.Record(
            intent.IntentId,
            sequence,
            intent.SubjectKind,
            intent.SubjectId,
            intent.ReasonCode,
            intent.PolicyVersion,
            recordedAtUtc,
            recordedAtUtc,
            recordedAtUtc + options.Value.AuthorityRetention);
        dbContext.ErasureIntents.Add(fact);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return fact;
    }

    public async Task<IReadOnlyList<PrivacyErasureIntent>> ReadAfterAsync(
        long authoritySequence,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(authoritySequence);
        if (limit is < 1 or > MaximumReadBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        PrivacyErasureCounter? counter = await dbContext.AuthorityCounters
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        if (authoritySequence < (counter?.RetainedFloorSequence ?? 0))
        {
            throw new Explore.Application.Exceptions.StaleRestoreBelowRetainedFloorException();
        }

        return await dbContext.ErasureIntents
            .AsNoTracking()
            .Where(item => item.AuthoritySequence > authoritySequence)
            .OrderBy(item => item.AuthoritySequence)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<PrivacyErasureRetentionEvaluation> EvaluateRetentionAsync(
        PrivacyErasureRetentionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureCutoffIsNotInFuture(request);
        PrivacyErasureCounter? counter = await dbContext.AuthorityCounters
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        if (counter is null)
        {
            return new PrivacyErasureRetentionEvaluation(0, 0, 0, 0);
        }

        List<PrivacyErasureIntent> candidates = await LoadMaintenanceCandidatesAsync(
            counter.RetainedFloorSequence,
            request.BatchSize,
            tracking: false,
            cancellationToken);
        PrivacyErasureRetentionEvaluation evaluation = Evaluate(
            candidates,
            counter.RetainedFloorSequence,
            request,
            out long expectedSequence);
        await EnsureNextSequenceExistsAsync(
            expectedSequence,
            counter.LastSequence,
            cancellationToken);
        return evaluation;
    }

    public async Task<PrivacyErasureCompactionResult> CompactExpiredIntentsAsync(
        PrivacyErasureRetentionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureCutoffIsNotInFuture(request);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        _ = await RelationalNamedLock.AcquireTransactionAsync(
            dbContext,
            "privacy-erasure-authority-counter",
            cancellationToken);
        PrivacyErasureCounter? counter = await dbContext.AuthorityCounters
            .SingleOrDefaultAsync(cancellationToken);
        if (counter is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new PrivacyErasureCompactionResult(
                0,
                0,
                new PrivacyErasureAuthorityState(0, 0));
        }
        List<PrivacyErasureIntent> candidates = await LoadMaintenanceCandidatesAsync(
            counter.RetainedFloorSequence,
            request.BatchSize,
            tracking: true,
            cancellationToken);
        var deleted = 0;
        var pseudonymized = 0;
        long expectedSequence = counter.RetainedFloorSequence;
        foreach (PrivacyErasureIntent candidate in candidates)
        {
            EnsureContiguous(candidate, ref expectedSequence);
            if (candidate.RetentionExpiresAtUtc > request.AsOfUtc)
            {
                break;
            }

            if (request.HeldAuthoritySequences.Contains(candidate.AuthoritySequence))
            {
                if (!candidate.IsLegalHoldPseudonymized)
                {
                    Guid intentAuditToken = Guid.NewGuid();
                    Guid subjectAuditToken = Guid.NewGuid();
                    candidate.PseudonymizeForLegalHold(intentAuditToken, subjectAuditToken);
                    pseudonymized++;
                }

                if (candidate.AuthoritySequence > counter.RetainedFloorSequence)
                {
                    counter.AdvanceRetainedFloorTo(candidate.AuthoritySequence);
                }

                break;
            }

            dbContext.ErasureIntents.Remove(candidate);
            deleted++;
            if (candidate.AuthoritySequence > counter.RetainedFloorSequence)
            {
                counter.AdvanceRetainedFloorTo(candidate.AuthoritySequence);
            }
        }

        await EnsureNextSequenceExistsAsync(
            expectedSequence,
            counter.LastSequence,
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PrivacyErasureCompactionResult(deleted, pseudonymized, counter.GetState());
    }

    private void EnsureCutoffIsNotInFuture(PrivacyErasureRetentionRequest request)
    {
        if (request.AsOfUtc > timeProvider.GetUtcNow().UtcDateTime)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }
    }

    private Task<List<PrivacyErasureIntent>> LoadMaintenanceCandidatesAsync(
        long retainedFloorSequence,
        int batchSize,
        bool tracking,
        CancellationToken cancellationToken)
    {
        IQueryable<PrivacyErasureIntent> query = dbContext.ErasureIntents
            .Where(intent => intent.AuthoritySequence > retainedFloorSequence
                || (intent.AuthoritySequence == retainedFloorSequence
                    && intent.IsLegalHoldPseudonymized));
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return query
            .OrderBy(intent => intent.AuthoritySequence)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    private static PrivacyErasureRetentionEvaluation Evaluate(
        IReadOnlyList<PrivacyErasureIntent> candidates,
        long retainedFloorSequence,
        PrivacyErasureRetentionRequest request,
        out long expectedSequence)
    {
        var eligible = 0;
        var held = 0;
        long projectedFloor = retainedFloorSequence;
        expectedSequence = retainedFloorSequence;
        foreach (PrivacyErasureIntent candidate in candidates)
        {
            EnsureContiguous(candidate, ref expectedSequence);
            if (candidate.RetentionExpiresAtUtc > request.AsOfUtc)
            {
                break;
            }

            projectedFloor = Math.Max(projectedFloor, candidate.AuthoritySequence);
            if (request.HeldAuthoritySequences.Contains(candidate.AuthoritySequence))
            {
                held++;
                break;
            }

            eligible++;
        }

        return new PrivacyErasureRetentionEvaluation(
            eligible,
            held,
            retainedFloorSequence,
            projectedFloor);
    }

    private async Task EnsureNextSequenceExistsAsync(
        long expectedSequence,
        long highWaterSequence,
        CancellationToken cancellationToken)
    {
        if (expectedSequence < highWaterSequence
            && !await dbContext.ErasureIntents.AnyAsync(
                intent => intent.AuthoritySequence == expectedSequence + 1,
                cancellationToken))
        {
            throw new Explore.Application.Exceptions.PrivacyErasureSequenceGapException();
        }
    }

    private static void EnsureContiguous(
        PrivacyErasureIntent candidate,
        ref long expectedSequence)
    {
        if (candidate.AuthoritySequence == expectedSequence
            && candidate.IsLegalHoldPseudonymized)
        {
            return;
        }

        expectedSequence++;
        if (candidate.AuthoritySequence != expectedSequence)
        {
            throw new Explore.Application.Exceptions.PrivacyErasureSequenceGapException();
        }
    }

    private static void EnsureSamePayload(
        PrivacyErasureIntent existing,
        PrivacyErasureRequest requested)
    {
        if (existing.SubjectKind != requested.SubjectKind
            || existing.SubjectId != requested.SubjectId
            || existing.ReasonCode != requested.ReasonCode
            || existing.PolicyVersion != requested.PolicyVersion)
        {
            throw new InvalidOperationException(
                "The erasure authority rejected the append payload for this IntentId.");
        }
    }
}
