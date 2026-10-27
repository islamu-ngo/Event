using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Features.Moderation.Inspection;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationResourceQuarantineReleaseRepository
{
    internal async Task<bool> DiscloseReviewStateAsync(ModerationResourceQuarantineReviewStateIntent intent,
        IPrivacyIdentityFenceAuthority privacy, ModerationResourceQuarantineReviewStateOutput write, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (!new ModerationResourceQuarantineReviewStateValidator().Validate(intent).IsValid
            || !Clean() || System.Transactions.Transaction.Current is not null)
            return false;
        var tenantId = context.TenantFilterTenantId!.Value;
        var entered = false;
        var outputStarted = false;
        try
        {
            return await privacy.ExecuteSerializedAsync(async ct =>
            {
                if (entered) return false;
                entered = true;
                if (context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId
                    || context.ModerationTrustDomainId is not null || context.ChangeTracker.HasChanges()
                    || context.Database.CurrentTransaction is not null && !context.IdentityFenceOwnsTransaction)
                    return false;
                await using IDbContextTransaction? owned = context.IdentityFenceOwnsTransaction
                    ? null : await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var transaction = context.Database.CurrentTransaction;
                if (transaction is null || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                    return false;
                var chain = await ReadChainAsync(intent.TipId, ct);
                if (chain.Count == 0 || chain[0].CaseId != intent.CaseId
                    || chain[0].CaseRevision != intent.ExpectedCaseRevision)
                    return false;
                var humans = await DiscoverResourceSafetyHumansAsync(
                    intent.ResourceId, intent.CaseId, intent.ReviewerAssignmentId, chain, ct);
                foreach (var human in humans)
                    await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, human, ct);
                await RequireInspectionHumansAsync(humans, privacy, ct);
                var acquiredAt = clock.GetUtcNow();
                var qualified = await QualifyAsync(intent.QuarantineId, intent.ResourceId, intent.MaterialVersionId,
                    intent.CaseId, intent.ExpectedCaseRevision, chain, intent.ReviewerAssignmentId, acquiredAt, intent, ct);
                if (await HasChildAsync(ct)) return false;
                RequireReviewState(qualified, chain, acquiredAt);
                var entry = Actions(qualified, chain, acquiredAt);
                var remaining = qualified.ValidUntil - clock.GetUtcNow();
                if (remaining <= TimeSpan.Zero) return false;
                using var deadline = new CancellationTokenSource(remaining, clock);
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
                ct = lifetime.Token;

                async Task<bool> CurrentAsync(CancellationToken currentToken)
                {
                    currentToken.ThrowIfCancellationRequested();
                    if (!ReferenceEquals(context.Database.CurrentTransaction, transaction)
                        || context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId
                        || context.ModerationTrustDomainId != qualified.Authority.Binding.TrustDomainId
                        || machine.IsMachineCaller || context.ChangeTracker.HasChanges()
                        || clock.GetUtcNow() < acquiredAt || clock.GetUtcNow() >= qualified.ValidUntil)
                        return false;
                    var currentChain = await ReadChainAsync(intent.TipId, currentToken);
                    if (currentChain.Count != chain.Count
                        || currentChain.Where((row, index) => Digest(row) != Digest(chain[index])).Any())
                        return false;
                    var currentHumans = await DiscoverResourceSafetyHumansAsync(
                        intent.ResourceId, intent.CaseId, intent.ReviewerAssignmentId, currentChain, currentToken);
                    if (!humans.SequenceEqual(currentHumans)) return false;
                    await RequireInspectionHumansAsync(humans, privacy, currentToken);
                    var current = await QualifyAsync(intent.QuarantineId, intent.ResourceId, intent.MaterialVersionId,
                        intent.CaseId, intent.ExpectedCaseRevision, currentChain, intent.ReviewerAssignmentId,
                        clock.GetUtcNow(), intent, currentToken);
                    if (await HasChildAsync(currentToken)) return false;
                    RequireReviewState(current, currentChain, clock.GetUtcNow());
                    var currentEntry = Actions(current, currentChain, clock.GetUtcNow());
                    return current.Authority.UserId == qualified.Authority.UserId
                        && qualified.Native.HasSameNativeVersion(current.Native)
                        && entry.AllowedSteps.SequenceEqual(currentEntry.AllowedSteps)
                        && entry.CanInspect == currentEntry.CanInspect && entry.CanRelease == currentEntry.CanRelease
                        && clock.GetUtcNow() >= acquiredAt && clock.GetUtcNow() < qualified.ValidUntil
                        && await RecheckResourceSafetyProvenanceAsync(qualified, current, currentToken);
                }

                if (!await CurrentAsync(ct)) return false;
                outputStarted = true;
                return await write(entry, CurrentAsync, ct);
            }, token);
        }
        catch (SafetyUnavailable) when (!outputStarted) { return false; }
        catch (DbException) when (!outputStarted) { return false; }
        finally
        {
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = null;
        }

        Task<bool> HasChildAsync(CancellationToken ct) => context.Set<EventResourceModerationQuarantineReview>()
            .AsNoTracking().AnyAsync(row => row.TenantId == tenantId && row.PredecessorId == intent.TipId, ct);

        void RequireReviewState(QualifiedSafety current,
            IReadOnlyList<EventResourceModerationQuarantineReview> rows, DateTimeOffset now)
        {
            RequireIndependentChain(rows, current, now);
            var reviewer = current.Reviewers[intent.ReviewerAssignmentId];
            if (current.Cause.ReleaseId is not null || current.Authority.Epoch.Revision != intent.ExpectedQualificationRevision
                || reviewer.ReviewerHumanId != current.Authority.UserId
                || reviewer.Revision != intent.ExpectedReviewerRevision)
                throw new SafetyUnavailable();
        }

        ModerationResourceQuarantineReviewStateEntry Actions(QualifiedSafety current,
            IReadOnlyList<EventResourceModerationQuarantineReview> rows, DateTimeOffset now)
        {
            var steps = new List<ResourceSafetyReviewStep>();
            foreach (var step in Enum.GetValues<ResourceSafetyReviewStep>())
            {
                try
                {
                    RequireNextStep(step, rows, current, intent.ReviewerAssignmentId, now);
                    steps.Add(step);
                }
                catch (SafetyUnavailable) { }
            }
            var canInspect = false;
            if (rows.Count >= 2)
            {
                try
                {
                    RequireInspectionReviewer(new(intent.QuarantineId, intent.ResourceId, intent.MaterialVersionId,
                        intent.CaseId, intent.ExpectedCaseRevision, rows[1].Id, intent.ReviewerAssignmentId,
                        intent.ExpectedReviewerRevision, intent.ExpectedQualificationRevision), rows.Take(2).ToArray(), current, now);
                    canInspect = true;
                }
                catch (SafetyUnavailable) { }
            }
            var canRelease = rows.Count == 3 + rows[0].RequiredApprovals
                && rows[^1].Step == ResourceSafetyReviewStep.ApproveSafety
                && rows[2].Attestation == ResourceSafetyAttestation.IndependentlyInspectedSafeForCurrentAudience
                && current.Authority.UserId == rows[^1].HumanId;
            return new(rows, steps.ToArray(), canInspect, canRelease, current.ValidUntil);
        }
    }
}
