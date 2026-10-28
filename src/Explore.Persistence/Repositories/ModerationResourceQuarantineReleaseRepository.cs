using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Native fresh safety review and per-cause release. No original notice, historical finding,
/// personal sanction, effect lease or cleanup marker participates in this independent purpose.
/// Each immutable review row is its own exact request/principal-bound mutation receipt.
/// </summary>
public sealed partial class ModerationResourceQuarantineReleaseRepository(
    ExploreDbContext context, IAdminContext admin, IMachinePrincipalAccessor machine, TimeProvider clock)
    : IModerationResourceQuarantineReleaseRepository
{
    public async Task<ModerationResourceSafetyReviewResult> ReviewAsync(
        ModerationResourceSafetyReviewIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (!new ModerationResourceSafetyReviewValidator().Validate(intent).IsValid)
            return new(ModerationResourceSafetyOutcome.Denied);
        if (!Clean()) return new(ModerationResourceSafetyOutcome.Unavailable);
        var digest = Digest(intent);
        DateTimeOffset? recordedAt = null;
        try
        {
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                context.ChangeTracker.Clear();
                context.ModerationTrustDomainId = null;
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var existing = await context.Set<EventResourceModerationQuarantineReview>().AsNoTracking()
                    .SingleOrDefaultAsync(row => row.Id == intent.OperationId
                        && row.TenantId == context.TenantFilterTenantId, cancellationToken);
                if (existing is not null)
                {
                    var replayAuthority = await AcquireHumansAsync(intent.ResourceId, null, [], null, cancellationToken);
                    await RelationalEntityRowFence.AcquireGlobalAsync<EventResourceModerationQuarantineReview>(
                        context, intent.OperationId, cancellationToken);
                    existing = await context.Set<EventResourceModerationQuarantineReview>().AsNoTracking()
                        .SingleOrDefaultAsync(row => row.Id == intent.OperationId
                            && row.TenantId == replayAuthority.Tenant.Id, cancellationToken)
                        ?? throw new SafetyUnavailable();
                    existing.ValidatePersistedState();
                    if (existing.RequestDigest != digest || existing.HumanId != replayAuthority.UserId)
                        return new ModerationResourceSafetyReviewResult(ModerationResourceSafetyOutcome.Conflict);
                    await transaction.CommitAsync(cancellationToken);
                    return new ModerationResourceSafetyReviewResult(ModerationResourceSafetyOutcome.Known, existing);
                }
                var chain = await ReadChainAsync(intent.PredecessorId, cancellationToken);
                recordedAt ??= clock.GetUtcNow();
                var qualified = await QualifyAsync(intent.QuarantineId, intent.ResourceId, intent.MaterialVersionId,
                    intent.CaseId, intent.ExpectedCaseRevision, chain, intent.ReviewerAssignmentId, recordedAt.Value, intent, cancellationToken);
                await using var operation = await RelationalNamedLock.AcquireTransactionAsync(context,
                    $"moderation.resource-safety-operation:{intent.OperationId:N}", cancellationToken);
                if (await context.Set<EventResourceModerationQuarantineReview>().AnyAsync(row => row.Id == intent.OperationId, cancellationToken)
                    || await context.Set<EventResourceModerationQuarantineRelease>().AnyAsync(row => row.Id == intent.OperationId, cancellationToken))
                    return new ModerationResourceSafetyReviewResult(ModerationResourceSafetyOutcome.Conflict);
                var reviewer = qualified.Reviewers[intent.ReviewerAssignmentId];
                if (qualified.Cause.ReleaseId is not null || reviewer.ReviewerHumanId != qualified.Authority.UserId
                    || reviewer.Revision != intent.ExpectedReviewerRevision || intent.ValidUntil <= recordedAt
                    || intent.ValidUntil > qualified.ValidUntil)
                    throw new SafetyUnavailable();
                RequireNextStep(intent.Step, chain, qualified, intent.ReviewerAssignmentId, recordedAt.Value);
                var purpose = chain.Count == 0 ? null : chain[0];
                if (intent.Step == ResourceSafetyReviewStep.AttestMaterialSafety
                    && intent.ValidUntil - recordedAt.Value > TimeSpan.FromTicks(purpose!.MaximumEvidenceLifetimeTicks))
                    throw new SafetyUnavailable();
                var receipt = new EventResourceModerationQuarantineReview
                {
                    Id = intent.OperationId,
                    TenantId = qualified.Authority.Tenant.Id,
                    HostBindingId = qualified.Authority.Binding.Id,
                    TrustDomainId = qualified.Authority.Binding.TrustDomainId,
                    QuarantineId = qualified.Cause.Id,
                    EventId = qualified.Native.EventId,
                    EventResourceId = intent.ResourceId,
                    MaterialVersionId = intent.MaterialVersionId,
                    CaseId = intent.CaseId,
                    CaseRevision = intent.ExpectedCaseRevision,
                    Step = intent.Step,
                    PurposeId = purpose?.Id ?? intent.OperationId,
                    PredecessorId = intent.PredecessorId,
                    HumanId = qualified.Authority.UserId,
                    ReviewerAssignmentId = reviewer.Id,
                    ReviewerRevision = reviewer.Revision,
                    ControllerHumanId = reviewer.ControllerHumanId!.Value,
                    RequiredApprovals = purpose?.RequiredApprovals ?? intent.RequiredApprovals,
                    MaximumEvidenceLifetimeTicks = purpose?.MaximumEvidenceLifetimeTicks ?? intent.MaximumEvidenceLifetime.Ticks,
                    Attestation = intent.Attestation,
                    NativeDigest = NativeDigest(qualified.Native),
                    ResourceStamp = qualified.Native.ResourceStamp,
                    RequestDigest = digest,
                    QualificationRevision = qualified.Authority.Epoch.Revision,
                    RecordedAt = recordedAt.Value,
                    ValidUntil = intent.ValidUntil
                };
                receipt.ValidatePersistedState();
                context.Set<EventResourceModerationQuarantineReview>().Add(receipt);
                // A completed review is not a release: an honestly unsafe disposition keeps
                // every cause/count intact, but fulfills the independently approved review duty.
                if (receipt.Step is ResourceSafetyReviewStep.ApproveSafety or ResourceSafetyReviewStep.ApproveContinuedQuarantine
                    && chain.Count + 1 == 3 + receipt.RequiredApprovals)
                {
                    var humans = qualified.Authority.FencedHumans.ToArray();
                    if (await context.PrivacyErasureSagas.AsNoTracking()
                        .AnyAsync(row => humans.Contains(row.SubjectId), cancellationToken))
                        throw new SafetyUnavailable();
                    context.Set<EventResourceModerationReviewCompletion>().Add(
                        EventResourceModerationReviewCompletion.Complete(qualified.Cause, [.. chain, receipt]));
                }
                await context.SaveChangesAsync(cancellationToken);
                await context.FlushDisclosureAsync(cancellationToken);
                var final = await QualifyAsync(intent.QuarantineId, intent.ResourceId, intent.MaterialVersionId,
                    intent.CaseId, intent.ExpectedCaseRevision, chain, intent.ReviewerAssignmentId,
                    clock.GetUtcNow(), intent, cancellationToken,
                    ownWithdrawalId: intent.Step == ResourceSafetyReviewStep.WithdrawPurpose ? receipt.Id : null);
                var finishedAt = clock.GetUtcNow();
                RequireNextStep(intent.Step, chain, final, intent.ReviewerAssignmentId, finishedAt);
                var finalReviewer = final.Reviewers[intent.ReviewerAssignmentId];
                if (final.Authority.UserId != qualified.Authority.UserId || final.Cause.ReleaseId is not null
                    || finalReviewer.Revision != receipt.ReviewerRevision || finalReviewer.ControllerHumanId != receipt.ControllerHumanId
                    || !qualified.Native.HasSameNativeVersion(final.Native)
                    || finishedAt < recordedAt || finishedAt >= receipt.ValidUntil
                    || finishedAt >= qualified.ValidUntil || finishedAt >= final.ValidUntil)
                    throw new SafetyUnavailable();
                var finalHumans = final.Authority.FencedHumans.ToArray();
                if (await context.PrivacyErasureSagas.AsNoTracking()
                    .AnyAsync(row => finalHumans.Contains(row.SubjectId), cancellationToken)
                    || clock.GetUtcNow() < recordedAt || clock.GetUtcNow() >= receipt.ValidUntil
                    || clock.GetUtcNow() >= qualified.ValidUntil || clock.GetUtcNow() >= final.ValidUntil
                    || !await RecheckResourceSafetyProvenanceAsync(qualified, final, cancellationToken)
                    || clock.GetUtcNow() < recordedAt || clock.GetUtcNow() >= receipt.ValidUntil)
                    throw new SafetyUnavailable();
                await transaction.CommitAsync(cancellationToken);
                return new ModerationResourceSafetyReviewResult(ModerationResourceSafetyOutcome.Known, receipt);
            });
        }
        catch (SafetyUnavailable) { return new(ModerationResourceSafetyOutcome.Unavailable); }
        catch (DbUpdateConcurrencyException) { return new(ModerationResourceSafetyOutcome.Conflict); }
        catch (DbException) { return new(ModerationResourceSafetyOutcome.Unavailable); }
        catch (DbUpdateException exception) when (exception.InnerException is DbException)
        { return new(ModerationResourceSafetyOutcome.Unavailable); }
        finally { context.ChangeTracker.Clear(); context.ModerationTrustDomainId = null; }
    }

    public async Task<ModerationResourceQuarantineReleaseResult> ReleaseAsync(
        ModerationResourceQuarantineReleaseIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (!new ModerationResourceQuarantineReleaseValidator().Validate(intent).IsValid)
            return new(ModerationResourceSafetyOutcome.Denied);
        if (!Clean()) return new(ModerationResourceSafetyOutcome.Unavailable);
        var digest = Digest(intent);
        DateTimeOffset? releasedAt = null;
        try
        {
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                context.ChangeTracker.Clear();
                context.ModerationTrustDomainId = null;
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var existing = await context.Set<EventResourceModerationQuarantineRelease>().AsNoTracking()
                    .SingleOrDefaultAsync(row => row.Id == intent.OperationId
                        && row.TenantId == context.TenantFilterTenantId, cancellationToken);
                if (existing is not null)
                {
                    // Opaque receipt replay needs current native human authority, not expired
                    // safety evidence, and cannot perform another material/count transition.
                    var replayAuthority = await AcquireHumansAsync(intent.ResourceId, null, [], null, cancellationToken);
                    await RelationalEntityRowFence.AcquireGlobalAsync<EventResourceModerationQuarantine>(context, intent.QuarantineId, cancellationToken);
                    var cause = await context.EventResourceModerationQuarantines.AsNoTracking()
                        .SingleOrDefaultAsync(row => row.Id == intent.QuarantineId
                            && row.TenantId == replayAuthority.Tenant.Id, cancellationToken);
                    await RelationalEntityRowFence.AcquireGlobalAsync<EventResourceModerationQuarantineRelease>(
                        context, intent.OperationId, cancellationToken);
                    existing = await context.Set<EventResourceModerationQuarantineRelease>().AsNoTracking()
                        .SingleOrDefaultAsync(row => row.Id == intent.OperationId
                            && row.TenantId == replayAuthority.Tenant.Id, cancellationToken)
                        ?? throw new SafetyUnavailable();
                    existing.ValidatePersistedState();
                    if (existing.RequestDigest != digest || existing.ReleasedByHumanId != replayAuthority.UserId
                        || cause is null || !existing.Owns(cause) || cause.ReleaseId != existing.Id)
                        return new ModerationResourceQuarantineReleaseResult(ModerationResourceSafetyOutcome.Conflict);
                    await transaction.CommitAsync(cancellationToken);
                    return new ModerationResourceQuarantineReleaseResult(ModerationResourceSafetyOutcome.Known, existing);
                }
                var chain = await ReadChainAsync(intent.ApprovalId, cancellationToken);
                if (chain.Count == 0 || chain.Count != 3 + chain[0].RequiredApprovals
                    || chain[^1].Step != ResourceSafetyReviewStep.ApproveSafety
                    || chain[2].Attestation != ResourceSafetyAttestation.IndependentlyInspectedSafeForCurrentAudience)
                    throw new SafetyUnavailable();
                releasedAt ??= clock.GetUtcNow();
                var qualified = await QualifyAsync(intent.QuarantineId, intent.ResourceId, intent.MaterialVersionId,
                    chain[0].CaseId, chain[0].CaseRevision, chain, null, releasedAt.Value, intent, cancellationToken);
                RequireIndependentChain(chain, qualified, releasedAt.Value);
                if (qualified.Cause.ReleaseId is not null)
                    return new ModerationResourceQuarantineReleaseResult(ModerationResourceSafetyOutcome.Conflict);
                if (qualified.Authority.UserId != chain[^1].HumanId) throw new SafetyUnavailable();
                await using var operation = await RelationalNamedLock.AcquireTransactionAsync(context,
                    $"moderation.resource-safety-operation:{intent.OperationId:N}", cancellationToken);
                if (await context.Set<EventResourceModerationQuarantineReview>().AnyAsync(row => row.Id == intent.OperationId, cancellationToken)
                    || await context.Set<EventResourceModerationQuarantineRelease>()
                        .AnyAsync(row => row.Id == intent.OperationId || row.QuarantineId == intent.QuarantineId, cancellationToken))
                    return new ModerationResourceQuarantineReleaseResult(ModerationResourceSafetyOutcome.Conflict);
                var resource = await context.EventResources.Include(row => row.ModerationQuarantines)
                    .SingleOrDefaultAsync(row => row.Id == intent.ResourceId
                        && row.TenantId == qualified.Authority.Tenant.Id, cancellationToken)
                    ?? throw new SafetyUnavailable();
                var receipt = new EventResourceModerationQuarantineRelease
                {
                    Id = intent.OperationId,
                    TenantId = qualified.Cause.TenantId,
                    QuarantineId = qualified.Cause.Id,
                    EventId = qualified.Cause.EventId,
                    EventResourceId = qualified.Cause.EventResourceId,
                    MaterialVersionId = qualified.Cause.MaterialVersionId,
                    OriginalEffectId = qualified.Cause.EffectId,
                    OriginalBindingOrdinal = qualified.Cause.BindingOrdinal,
                    OriginallyAppliedAt = qualified.Cause.AppliedAt,
                    ApprovalId = intent.ApprovalId,
                    ReleasedByHumanId = qualified.Authority.UserId,
                    ReleasedAt = releasedAt.Value,
                    RequestDigest = digest
                };
                resource.ReleaseModerationQuarantine(receipt, qualified.Native.ResourceStamp);
                var remainingCount = resource.ModerationQuarantineCount;
                context.Set<EventResourceModerationQuarantineRelease>().Add(receipt);
                await context.SaveChangesAsync(cancellationToken);
                await context.FlushDisclosureAsync(cancellationToken);
                var final = await QualifyAsync(intent.QuarantineId, intent.ResourceId, intent.MaterialVersionId,
                    chain[0].CaseId, chain[0].CaseRevision, chain, null, clock.GetUtcNow(), intent,
                    cancellationToken, ownReleasedStamp: resource.ConcurrencyStamp);
                RequireIndependentChain(chain, final, clock.GetUtcNow());
                var countMatches = await context.EventResources.AsNoTracking().AnyAsync(row =>
                    row.Id == resource.Id && row.TenantId == resource.TenantId
                    && row.ModerationQuarantineCount == remainingCount, cancellationToken)
                    && await context.EventResourceModerationQuarantines.AsNoTracking().CountAsync(row =>
                        row.TenantId == resource.TenantId && row.EventResourceId == resource.Id
                        && row.ReleaseId == null, cancellationToken) == remainingCount;
                var finishedAt = clock.GetUtcNow();
                if (final.Authority.UserId != qualified.Authority.UserId || final.Cause.ReleaseId != receipt.Id
                    || finishedAt < releasedAt || finishedAt >= qualified.ValidUntil
                    || finishedAt >= final.ValidUntil || !countMatches
                    || !await RecheckResourceSafetyProvenanceAsync(qualified, final, cancellationToken))
                    throw new SafetyUnavailable();
                await transaction.CommitAsync(cancellationToken);
                return new ModerationResourceQuarantineReleaseResult(ModerationResourceSafetyOutcome.Known, receipt);
            });
        }
        catch (SafetyUnavailable) { return new(ModerationResourceSafetyOutcome.Unavailable); }
        catch (DbUpdateConcurrencyException) { return new(ModerationResourceSafetyOutcome.Conflict); }
        catch (DbException) { return new(ModerationResourceSafetyOutcome.Unavailable); }
        catch (DbUpdateException exception) when (exception.InnerException is DbException)
        { return new(ModerationResourceSafetyOutcome.Unavailable); }
        finally { context.ChangeTracker.Clear(); context.ModerationTrustDomainId = null; }
    }

    private bool Clean() => context.Database.IsRelational() && context.Database.CurrentTransaction is null
        && !context.IsTenantFilterBypassed && context.TenantFilterTenantId is { } tenantId && tenantId != Guid.Empty
        && context.ModerationTrustDomainId is null && !context.ChangeTracker.Entries().Any();

    private async Task<IReadOnlyList<EventResourceModerationQuarantineReview>> ReadChainAsync(Guid? tip, CancellationToken token)
    {
        var rows = new List<EventResourceModerationQuarantineReview>();
        while (tip is { } id)
        {
            if (rows.Count == 5 || rows.Any(row => row.Id == id)) throw new SafetyUnavailable();
            var row = await context.Set<EventResourceModerationQuarantineReview>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == id && row.TenantId == context.TenantFilterTenantId, token)
                ?? throw new SafetyUnavailable();
            row.ValidatePersistedState();
            rows.Add(row);
            tip = row.PredecessorId;
        }
        rows.Reverse();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var root = rows[0];
            var expectedStep = index switch
            {
                0 => ResourceSafetyReviewStep.ProposePurpose,
                1 => ResourceSafetyReviewStep.AdoptPurpose,
                2 => ResourceSafetyReviewStep.AttestMaterialSafety,
                _ => rows[2].Attestation == ResourceSafetyAttestation.IndependentlyInspectedUnsafeForCurrentAudience
                    ? ResourceSafetyReviewStep.ApproveContinuedQuarantine : ResourceSafetyReviewStep.ApproveSafety
            };
            if (row.Step != expectedStep || row.PurposeId != root.Id || row.QuarantineId != root.QuarantineId
                || row.CaseId != root.CaseId || row.CaseRevision != root.CaseRevision
                || row.RequiredApprovals != root.RequiredApprovals
                || row.MaximumEvidenceLifetimeTicks != root.MaximumEvidenceLifetimeTicks
                || index > 0 && (row.RecordedAt < rows[index - 1].RecordedAt
                    || row.ValidUntil > rows[index - 1].ValidUntil)
                || row.RequestDigest != Digest(new ModerationResourceSafetyReviewIntent
                {
                    OperationId = row.Id,
                    QuarantineId = row.QuarantineId,
                    ResourceId = row.EventResourceId,
                    MaterialVersionId = row.MaterialVersionId,
                    CaseId = row.CaseId,
                    ExpectedCaseRevision = row.CaseRevision,
                    ReviewerAssignmentId = row.ReviewerAssignmentId,
                    ExpectedReviewerRevision = row.ReviewerRevision,
                    Step = row.Step,
                    PredecessorId = row.PredecessorId,
                    ValidUntil = row.ValidUntil,
                    RequiredApprovals = index == 0 ? row.RequiredApprovals : 0,
                    MaximumEvidenceLifetime = index == 0 ? TimeSpan.FromTicks(row.MaximumEvidenceLifetimeTicks) : TimeSpan.Zero,
                    Attestation = row.Attestation
                }))
                throw new SafetyUnavailable();
        }
        return rows;
    }

    private static void RequireNextStep(ResourceSafetyReviewStep step,
        IReadOnlyList<EventResourceModerationQuarantineReview> chain, QualifiedSafety qualified,
        Guid reviewerId, DateTimeOffset now)
    {
        RequireIndependentChain(chain, qualified, now);
        var reviewer = qualified.Reviewers[reviewerId];
        var valid = step switch
        {
            ResourceSafetyReviewStep.ProposePurpose => chain.Count == 0,
            ResourceSafetyReviewStep.AdoptPurpose => chain.Count == 1
                && reviewer.IsIndependentOf(qualified.Reviewers[chain[0].ReviewerAssignmentId], now),
            ResourceSafetyReviewStep.AttestMaterialSafety => chain.Count == 2,
            ResourceSafetyReviewStep.ApproveSafety => chain.Count >= 3
                && chain[2].Attestation == ResourceSafetyAttestation.IndependentlyInspectedSafeForCurrentAudience
                && chain.Count < 3 + chain[0].RequiredApprovals
                && chain.Skip(2).All(row => reviewer.IsIndependentOf(qualified.Reviewers[row.ReviewerAssignmentId], now)),
            ResourceSafetyReviewStep.ApproveContinuedQuarantine => chain.Count >= 3
                && chain[2].Attestation == ResourceSafetyAttestation.IndependentlyInspectedUnsafeForCurrentAudience
                && chain.Count < 3 + chain[0].RequiredApprovals
                && chain.Skip(2).All(row => reviewer.IsIndependentOf(qualified.Reviewers[row.ReviewerAssignmentId], now)),
            ResourceSafetyReviewStep.WithdrawPurpose => chain.Count == 1,
            _ => false
        };
        if (!valid) throw new SafetyUnavailable();
    }

    private static void RequireIndependentChain(
        IReadOnlyList<EventResourceModerationQuarantineReview> chain, QualifiedSafety qualified, DateTimeOffset now)
    {
        if (chain.Count >= 2 && !qualified.Reviewers[chain[0].ReviewerAssignmentId]
                .IsIndependentOf(qualified.Reviewers[chain[1].ReviewerAssignmentId], now))
            throw new SafetyUnavailable();
        for (var index = 3; index < chain.Count; index++)
            if (chain.Skip(2).Take(index - 2).Any(row => !qualified.Reviewers[chain[index].ReviewerAssignmentId]
                    .IsIndependentOf(qualified.Reviewers[row.ReviewerAssignmentId], now)))
                throw new SafetyUnavailable();
    }

    private sealed class SafetyUnavailable : Exception;
}
