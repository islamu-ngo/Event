using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationResourceQuarantineDutyRepository
{
    public async Task<ModerationScheduleRiskReconciliationBatch> ReconcileScheduleRiskAsync(
        Guid? afterReviewId, int limit, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (afterReviewId == Guid.Empty) throw new ArgumentException("A cursor must be a native identity.", nameof(afterReviewId));
        if (!Clean() || machine.Current is not { CredentialSource: ApiKeyCredentialSource.Persisted } principal)
            return new(false, afterReviewId, []);
        var tenantId = context.TenantFilterTenantId!.Value;
        Guid[] ids;
        try
        {
            ids = await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var authority = await AcquireAuthorityAsync(tenantId, principal, cancellationToken);
                var found = await context.ModerationScheduleRiskReviews.AsNoTracking()
                    .Where(row => row.EventScopeSolutionId == authority.Host.SolutionId
                        && row.EventScopeInstanceId == authority.Host.InstanceId && row.EventScopeTenantId == tenantId
                        && row.State == ModerationScheduleRiskReviewState.Pending
                        && (afterReviewId == null || row.Id.CompareTo(afterReviewId.Value) > 0))
                    .OrderBy(row => row.Id).Take(limit).Select(row => row.Id).ToArrayAsync(cancellationToken);
                await RequireCurrentAsync(authority, tenantId, principal, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return found;
            });
        }
        catch (DutyUnavailable) { return new(false, afterReviewId, []); }
        catch (DbException) { return new(false, afterReviewId, []); }
        finally { context.ChangeTracker.Clear(); context.ModerationTrustDomainId = null; }

        var results = ImmutableArray.CreateBuilder<ModerationScheduleRiskReconciliationResult>(ids.Length);
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ReconcileScheduleRiskOneAsync(id, tenantId, principal, cancellationToken));
        }
        return new(true, ids.Length == limit ? ids[^1] : null, results.ToImmutable());
    }

    private async Task<ModerationScheduleRiskReconciliationResult> ReconcileScheduleRiskOneAsync(
        Guid id, Guid tenantId, ApiKeyPrincipalContext principal, CancellationToken token)
    {
        try
        {
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                context.ChangeTracker.Clear();
                context.ModerationTrustDomainId = null;
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                var authority = await AcquireAuthorityAsync(tenantId, principal, token);
                await RelationalEntityRowFence.AcquireGlobalAsync<ModerationScheduleRiskReview>(context, id, token);
                var review = await context.ModerationScheduleRiskReviews.SingleOrDefaultAsync(row =>
                    row.Id == id && row.EventScopeSolutionId == authority.Host.SolutionId
                    && row.EventScopeInstanceId == authority.Host.InstanceId && row.EventScopeTenantId == tenantId, token)
                    ?? throw new DutyUnavailable();
                review.ValidatePersistedState();
                var observedAt = clock.GetUtcNow();
                if (observedAt < review.UpdatedAt) throw new DutyUnavailable();
                var outcome = review.State != ModerationScheduleRiskReviewState.Pending
                    ? ModerationScheduleRiskReconciliationOutcome.Terminal
                    : review.Reconcile(review.Revision, observedAt)
                        ? ModerationScheduleRiskReconciliationOutcome.Overdue
                        : ModerationScheduleRiskReconciliationOutcome.AwaitingDeadline;
                var expected = new
                {
                    review.Revision, review.State, review.Coordinates, review.OriginalCoordinates,
                    review.SourceSanctionId, review.SourceSanctionRevision, review.SourceDecisionId,
                    review.SourceScope, review.SourceRule, review.Owner, review.EscalationOwner,
                    review.OriginalSanctionStartsAt, review.OriginalSanctionEndsAt,
                    review.DueAt, review.OriginalDueAt, review.CreatedAt, review.UpdatedAt,
                    review.AcceptedIntentId, review.AcceptedFindingId, review.AcceptedFindingRevision,
                    review.AcceptedReviewerAssignmentId, review.AcceptedReviewerAssignmentRevision,
                    review.WithdrawalIntentId, review.WithdrawnBy
                };
                await context.SaveChangesAsync(token);
                await RequireCurrentAsync(authority, tenantId, principal, token);
                var retained = await context.ModerationScheduleRiskReviews.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.Id == id, token) ?? throw new DutyUnavailable();
                retained.ValidatePersistedState();
                if (retained.Revision != expected.Revision || retained.State != expected.State
                    || retained.Coordinates != expected.Coordinates || retained.DueAt != expected.DueAt
                    || retained.OriginalCoordinates != expected.OriginalCoordinates
                    || retained.SourceSanctionId != expected.SourceSanctionId
                    || retained.SourceSanctionRevision != expected.SourceSanctionRevision
                    || retained.SourceDecisionId != expected.SourceDecisionId || retained.SourceScope != expected.SourceScope
                    || retained.SourceRule != expected.SourceRule || retained.Owner != expected.Owner
                    || retained.EscalationOwner != expected.EscalationOwner
                    || retained.OriginalSanctionStartsAt != expected.OriginalSanctionStartsAt
                    || retained.OriginalSanctionEndsAt != expected.OriginalSanctionEndsAt
                    || retained.OriginalDueAt != expected.OriginalDueAt
                    || retained.CreatedAt != expected.CreatedAt || retained.UpdatedAt != expected.UpdatedAt
                    || retained.AcceptedIntentId != expected.AcceptedIntentId
                    || retained.AcceptedFindingId != expected.AcceptedFindingId
                    || retained.AcceptedFindingRevision != expected.AcceptedFindingRevision
                    || retained.AcceptedReviewerAssignmentId != expected.AcceptedReviewerAssignmentId
                    || retained.AcceptedReviewerAssignmentRevision != expected.AcceptedReviewerAssignmentRevision
                    || retained.WithdrawalIntentId != expected.WithdrawalIntentId || retained.WithdrawnBy != expected.WithdrawnBy
                    || clock.GetUtcNow() < observedAt || context.Database.CurrentTransaction != transaction)
                    throw new DutyUnavailable();
                await transaction.CommitAsync(token);
                return new ModerationScheduleRiskReconciliationResult(outcome, retained);
            });
        }
        catch (DutyUnavailable) { return new(ModerationScheduleRiskReconciliationOutcome.Unavailable); }
        catch (DbUpdateConcurrencyException) { return new(ModerationScheduleRiskReconciliationOutcome.Unavailable); }
        catch (DbException) { return new(ModerationScheduleRiskReconciliationOutcome.Unavailable); }
        catch (DbUpdateException exception) when (exception.InnerException is DbException)
        { return new(ModerationScheduleRiskReconciliationOutcome.Unavailable); }
        catch (ArgumentException) { return new(ModerationScheduleRiskReconciliationOutcome.Unavailable); }
        finally { context.ChangeTracker.Clear(); context.ModerationTrustDomainId = null; }
    }
}
