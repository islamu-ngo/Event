using System.Globalization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationOriginalEffectCompletionRepository
{
    private async Task<DateTimeOffset> AccountAsync(ModerationSourceAuthority authority,
        ModerationEffectIntent effect, ModerationNoticeSnapshot notice, ModerationContributionSelection selection,
        DateTimeOffset observedAt, DateTimeOffset until, List<string> facts, CancellationToken token)
    {
        var causes = await context.EventResourceModerationQuarantines.AsNoTracking()
            .Where(row => row.EffectId == effect.Id).OrderBy(row => row.BindingOrdinal).ToListAsync(token);
        if (causes.Count != notice.Effects.Length
            || !causes.Select(row => row.BindingOrdinal).SequenceEqual(Enumerable.Range(0, notice.Effects.Length)))
            throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalCauseAccounting);
        for (var ordinal = 0; ordinal < notice.Effects.Length; ordinal++)
        {
            var original = notice.Effects[ordinal];
            var cause = causes[ordinal];
            cause.ValidatePersistedState();
            if (original.Effect is not
                {
                    Kind: ModerationReferenceEffectKind.UnsafeResourceVersion,
                    Mode: ModerationReferenceEffectMode.Quarantine,
                    Target: ModerationReferenceTarget.SelectedResourceVersion,
                    Capability: ModerationCapability.ReadResource,
                    Execution: ModerationReferenceExecution.NativeOwner
                }
                || original.Target.Owner != "event.resource-generation" || original.Target.Source is not { } stamp
                || original.Target.Scope != notice.Scope || original.Target.SubjectId != notice.SubjectId
                || original.Target.SubjectKind != ModerationSubjectKind.Content
                || original.Interval.Kind != ModerationReferenceTimingKind.ObjectReviewWithinDay
                || original.Interval.ReviewAt is not { } reviewAt
                || cause.TenantId != authority.Tenant.Id || cause.EventId != notice.SubjectId
                || cause.EventResourceId != original.Target.TargetId
                || cause.AppliedAt < effect.CreatedAt || cause.AppliedAt > observedAt
                || cause.AppliedAt >= reviewAt || !original.Interval.IsEffectiveAt(cause.AppliedAt))
                throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalCauseAccounting);
            await using var adoptionSlot = await RelationalNamedLock.AcquireTransactionAsync(
                context, $"moderation.resource-target:{selection.Id:N}:{ordinal}", token);
            var adoption = await context.ModerationResourceTargetAdoptions.AsNoTracking()
                .SingleOrDefaultAsync(row => row.SelectionId == selection.Id && row.BindingOrdinal == ordinal, token);
            var generation = await context.ModerationResourceGenerations.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == stamp.Id, token);
            if (adoption is null || generation is null)
                throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalCauseAccounting);
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationResourceTargetAdoption>(context, adoption.Id, token);
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationResourceGeneration>(context, generation.Id, token);
            adoption.ValidatePersistedState();
            generation.ValidatePersistedState();
            if (generation.HostBindingId != authority.Binding.Id || generation.TenantId != authority.Tenant.Id
                || generation.TrustDomainId != effect.TrustDomainId || generation.ResourceId != cause.EventResourceId
                || generation.EventId != cause.EventId || generation.MaterialVersionId != cause.MaterialVersionId
                || generation.Revision != original.Target.TargetRevision || generation.Revision != stamp.Revision
                || generation.WriterGeneration.ToString(CultureInfo.InvariantCulture) != stamp.WriterGeneration
                || generation.ObservedAt > effect.CreatedAt || generation.ValidUntil <= observedAt
                || generation.WriterGeneration > effect.QualificationRevision
                || adoption.HostBindingId != authority.Binding.Id || adoption.TenantId != authority.Tenant.Id
                || adoption.TrustDomainId != effect.TrustDomainId || adoption.SelectionRevision != selection.Revision
                || adoption.GenerationId != generation.Id || adoption.GenerationRevision != generation.Revision
                || adoption.GenerationWriterGeneration != generation.WriterGeneration || adoption.RevokedAt is not null
                || adoption.Revision != 1 || adoption.CaseId != selection.CaseId
                || adoption.CaseRevision != selection.CaseRevision || adoption.AdoptedAt > effect.CreatedAt
                || adoption.ValidUntil <= observedAt || adoption.ValidUntil > generation.ValidUntil
                || adoption.ValidUntil > selection.ValidUntil || adoption.WriterGeneration > effect.QualificationRevision
                || !authority.Administrators.Contains(adoption.AdoptedByHumanId)
                || !authority.Administrators.Contains(generation.ObservedByHumanId)
                || adoption.AdoptedByHumanId == selection.AuthoredByHumanId)
                throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalCauseAccounting);
            until = new[] { until, generation.ValidUntil, adoption.ValidUntil }.Min();
            await RelationalEntityRowFence.AcquireGlobalAsync<EventResource>(context, cause.EventResourceId, token);
            await RelationalEntityRowFence.AcquireGlobalAsync<EventResourceModerationQuarantine>(context, cause.Id, token);
            var resource = await context.EventResources.AsNoTracking().Include(row => row.ModerationQuarantines)
                .SingleOrDefaultAsync(row => row.Id == cause.EventResourceId, token);
            if (resource is null || resource.TenantId != cause.TenantId || resource.EventId != cause.EventId
                || resource.ModerationQuarantineCount != resource.ModerationQuarantines.Count
                || resource.ModerationQuarantines.Any(row => row.TenantId != resource.TenantId
                    || row.EventId != resource.EventId || row.EventResourceId != resource.Id
                    || row.MaterialVersionId != resource.MaterialVersionId))
                throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalCauseAccounting);

            // A committed independent disposition is the fact. Discovery, escalation, pending
            // duty rows and the mere existence of a proposed inspection cannot stand in for it.
            var reviews = await context.Set<EventResourceModerationReviewCompletion>().AsNoTracking()
                .Where(row => row.QuarantineId == cause.Id).OrderBy(row => row.CompletedAt).ThenBy(row => row.Id)
                .ToListAsync(token);
            if (reviews.Count == 0)
                throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.IndependentObjectReview);
            foreach (var review in reviews)
            {
                await RelationalEntityRowFence.AcquireGlobalAsync<EventResourceModerationReviewCompletion>(context, review.Id, token);
                review.ValidatePersistedState();
                if (!review.Owns(cause) || review.CompletedAt < cause.AppliedAt || review.CompletedAt > observedAt)
                    throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.IndependentObjectReview);
                facts.Add(Digest(review));
            }
            if (cause.ReleaseId is { } releaseId)
            {
                await RelationalEntityRowFence.AcquireGlobalAsync<EventResourceModerationQuarantineRelease>(context, releaseId, token);
                var release = await context.Set<EventResourceModerationQuarantineRelease>().AsNoTracking()
                    .SingleOrDefaultAsync(row => row.Id == releaseId, token);
                if (release is null)
                    throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalCauseAccounting);
                release.ValidatePersistedState();
                if (!release.Owns(cause) || release.ReleasedAt > observedAt
                    || !reviews.Any(row => row.ApprovalId == release.ApprovalId
                        && row.Disposition != ResourceSafetyAttestation.IndependentlyInspectedUnsafeForCurrentAudience
                        && row.CompletedAt <= release.ReleasedAt)
                    || resource.ModerationQuarantines.Any(row => row.Id == cause.Id))
                    throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalCauseAccounting);
                // Retired original ordinals remain accounted, even after native material replacement.
                // This owner never applies a guard or invokes a release.
                facts.Add(Digest(release));
            }
            else if (resource.MaterialVersionId != cause.MaterialVersionId || !resource.HasModerationQuarantine
                || !resource.ModerationQuarantines.Any(row => row.Id == cause.Id))
                throw new CompletionPending(ModerationOriginalEffectCompletionPendingReason.OriginalCauseAccounting);
            facts.Add(Digest(cause));
            facts.Add(Digest(new { generation.Id, generation.Revision, generation.WriterGeneration, AdoptionId = adoption.Id,
                AdoptionRevision = adoption.Revision, adoption.ValidUntil }));
        }
        return until;
    }
}
