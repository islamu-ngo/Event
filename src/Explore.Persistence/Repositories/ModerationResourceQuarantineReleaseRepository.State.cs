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
    internal async Task<bool> DiscloseStateAsync(ModerationResourceQuarantineStateIntent intent,
        IPrivacyIdentityFenceAuthority privacy, ModerationResourceQuarantineStateOutput write, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (!new ModerationResourceQuarantineStateValidator().Validate(intent).IsValid
            || !Clean() || System.Transactions.Transaction.Current is not null)
            return false;
        var tenantId = context.TenantFilterTenantId!.Value;
        var entered = false;
        var outputStarted = false;
        try
        {
            return await privacy.ExecuteSerializedAsync(async cancellationToken =>
            {
                if (entered) return false;
                entered = true;
                if (context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId
                    || context.ModerationTrustDomainId is not null || context.ChangeTracker.HasChanges()
                    || context.Database.CurrentTransaction is not null && !context.IdentityFenceOwnsTransaction)
                    return false;
                await using IDbContextTransaction? owned = context.IdentityFenceOwnsTransaction
                    ? null : await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var transaction = context.Database.CurrentTransaction;
                if (transaction is null || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                    return false;

                // Opaque discovery cannot disclose anything. Every candidate is then qualified
                // against the fresh resource case, current human reviewer and actual native ancestry.
                var ids = await CandidatesAsync(cancellationToken);
                if (ids.Length == 0) return false;
                var hasNext = ids.Length > 64;
                var selected = ids.Take(64).ToArray();
                var humans = await DiscoverResourceSafetyHumansAsync(
                    intent.ResourceId, intent.CaseId, intent.ReviewerAssignmentId, [], cancellationToken);
                foreach (var human in humans)
                    await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, human, cancellationToken);
                await RequireInspectionHumansAsync(humans, privacy, cancellationToken);
                var acquiredAt = clock.GetUtcNow();
                var qualified = new List<QualifiedSafety>();
                foreach (var id in selected)
                {
                    var current = await QualifyAsync(id, intent.ResourceId, intent.MaterialVersionId, intent.CaseId,
                        intent.ExpectedCaseRevision, [], intent.ReviewerAssignmentId, acquiredAt, intent, cancellationToken);
                    RequireStateReviewer(current);
                    await RequireInspectionHumansAsync(current.Authority.FencedHumans.ToArray(), privacy, cancellationToken);
                    qualified.Add(current);
                }
                var until = qualified.Min(row => row.ValidUntil);
                var remaining = until - clock.GetUtcNow();
                if (remaining <= TimeSpan.Zero) return false;
                using var deadline = new CancellationTokenSource(remaining, clock);
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
                cancellationToken = lifetime.Token;
                var entries = new List<ModerationResourceQuarantineStateEntry>();
                foreach (var current in qualified)
                {
                    var cause = current.Cause;
                    await RelationalEntityRowFence.AcquireGlobalAsync<EventResourceModerationReviewDuty>(
                        context, cause.Id, cancellationToken);
                    var duty = await context.Set<EventResourceModerationReviewDuty>().AsNoTracking()
                        .SingleOrDefaultAsync(row => row.Id == cause.Id && row.TenantId == tenantId, cancellationToken);
                    duty?.ValidatePersistedState();
                    if (duty is not null && !duty.Owns(cause)) throw new SafetyUnavailable();
                    var completionId = await context.Set<EventResourceModerationReviewCompletion>().AsNoTracking()
                        .Where(row => row.QuarantineId == cause.Id && row.TenantId == tenantId)
                        .OrderByDescending(row => row.CompletedAt).ThenByDescending(row => row.Id)
                        .Select(row => (Guid?)row.Id).FirstOrDefaultAsync(cancellationToken);
                    EventResourceModerationReviewCompletion? completion = null;
                    if (completionId is { } reviewed)
                    {
                        await RelationalEntityRowFence.AcquireGlobalAsync<EventResourceModerationReviewCompletion>(
                            context, reviewed, cancellationToken);
                        completion = await context.Set<EventResourceModerationReviewCompletion>().AsNoTracking()
                            .SingleAsync(row => row.Id == reviewed && row.TenantId == tenantId, cancellationToken);
                        completion.ValidatePersistedState();
                        if (completion.QuarantineId != cause.Id || completion.EventId != cause.EventId
                            || completion.EventResourceId != cause.EventResourceId
                            || completion.MaterialVersionId != cause.MaterialVersionId
                            || completion.CompletedAt < cause.AppliedAt || completion.CompletedAt > acquiredAt)
                            throw new SafetyUnavailable();
                    }
                    EventResourceModerationQuarantineRelease? release = null;
                    if (cause.ReleaseId is { } released)
                    {
                        await RelationalEntityRowFence.AcquireGlobalAsync<EventResourceModerationQuarantineRelease>(
                            context, released, cancellationToken);
                        release = await context.Set<EventResourceModerationQuarantineRelease>().AsNoTracking()
                            .SingleOrDefaultAsync(row => row.Id == released && row.TenantId == tenantId, cancellationToken)
                            ?? throw new SafetyUnavailable();
                        release.ValidatePersistedState();
                        if (!release.Owns(cause) || release.ReleasedAt > acquiredAt) throw new SafetyUnavailable();
                    }
                    entries.Add(new(cause, duty, completion, release));
                }

                async Task<bool> CurrentAsync(CancellationToken ct)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!ReferenceEquals(context.Database.CurrentTransaction, transaction)
                        || context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId
                        || context.ModerationTrustDomainId != qualified[0].Authority.Binding.TrustDomainId
                        || machine.IsMachineCaller || context.ChangeTracker.HasChanges()
                        || clock.GetUtcNow() < acquiredAt || clock.GetUtcNow() >= until)
                        return false;
                    var currentIds = await CandidatesAsync(ct);
                    if (!ids.SequenceEqual(currentIds)) return false;
                    var currentHumans = await DiscoverResourceSafetyHumansAsync(
                        intent.ResourceId, intent.CaseId, intent.ReviewerAssignmentId, [], ct);
                    if (!humans.SequenceEqual(currentHumans)) return false;
                    await RequireInspectionHumansAsync(humans, privacy, ct);
                    for (var index = 0; index < qualified.Count; index++)
                    {
                        var original = qualified[index];
                        var current = await QualifyAsync(original.Cause.Id, intent.ResourceId, intent.MaterialVersionId,
                            intent.CaseId, intent.ExpectedCaseRevision, [], intent.ReviewerAssignmentId, clock.GetUtcNow(), intent, ct);
                        RequireStateReviewer(current);
                        if (current.Authority.UserId != original.Authority.UserId
                            || !original.Native.HasSameNativeVersion(current.Native)
                            || current.Cause.ReleaseId != original.Cause.ReleaseId
                            || !await RecheckResourceSafetyProvenanceAsync(original, current, ct))
                            return false;
                        await RequireInspectionHumansAsync(current.Authority.FencedHumans.ToArray(), privacy, ct);
                    }
                    return clock.GetUtcNow() >= acquiredAt && clock.GetUtcNow() < until;
                }

                if (!await CurrentAsync(cancellationToken)) return false;
                outputStarted = true;
                return await write(entries, hasNext, CurrentAsync, cancellationToken);
            }, token);
        }
        catch (SafetyUnavailable) when (!outputStarted) { return false; }
        catch (DbException) when (!outputStarted) { return false; }
        finally
        {
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = null;
        }

        Task<Guid[]> CandidatesAsync(CancellationToken ct) => context.EventResourceModerationQuarantines.AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.EventResourceId == intent.ResourceId
                && row.MaterialVersionId == intent.MaterialVersionId)
            .OrderBy(row => row.Id).Skip((intent.Page - 1) * 64).Take(65).Select(row => row.Id).ToArrayAsync(ct);

        void RequireStateReviewer(QualifiedSafety current)
        {
            var reviewer = current.Reviewers[intent.ReviewerAssignmentId];
            if (current.Authority.Epoch.Revision != intent.ExpectedQualificationRevision
                || reviewer.ReviewerHumanId != current.Authority.UserId
                || reviewer.Revision != intent.ExpectedReviewerRevision)
                throw new SafetyUnavailable();
        }
    }
}
