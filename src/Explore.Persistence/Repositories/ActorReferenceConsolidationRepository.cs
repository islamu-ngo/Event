using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class ActorReferenceConsolidationRepository(ExploreDbContext dbContext) : IActorReferenceConsolidationRepository
{
    public async Task<bool> MoveMutableReferencesAsync(Guid sourceActorId, Guid targetActorId, int targetActorTypeId, CancellationToken cancellationToken = default)
        => await dbContext.ExecuteDisclosureMutationAsync(
            token => MoveReferencesAsync(sourceActorId, targetActorId, targetActorTypeId, token), cancellationToken);

    private async Task<bool> MoveReferencesAsync(
        Guid sourceActorId, Guid targetActorId, int targetActorTypeId, CancellationToken cancellationToken)
    {
        if (sourceActorId == Guid.Empty || targetActorId == Guid.Empty || sourceActorId == targetActorId)
        {
            return false;
        }

        // The sole caller retires source Actor after these bulk writes. Acquire both
        // Actors first, matching review's Actor -> Event order rather than reversing it.
        await dbContext.DisclosureMutations.EnlistActorsAsync([sourceActorId, targetActorId], cancellationToken);
        if (await HasCollisionAsync(sourceActorId, targetActorId, cancellationToken).ConfigureAwait(false))
            return false;

        await dbContext.AtprotoIdentities.Where(x => x.ActorId == sourceActorId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ActorId, targetActorId), cancellationToken).ConfigureAwait(false);
        await dbContext.Events.IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoActorConsolidation)
            .Where(x => x.ActorId == sourceActorId || x.OrganizerActorId == sourceActorId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.ActorId, x => x.ActorId == sourceActorId ? targetActorId : x.ActorId)
                .SetProperty(x => x.OrganizerActorId, x => x.OrganizerActorId == sourceActorId ? targetActorId : x.OrganizerActorId), cancellationToken).ConfigureAwait(false);
        await dbContext.EventSeries.IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoActorConsolidation).Where(x => x.ActorId == sourceActorId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ActorId, targetActorId), cancellationToken).ConfigureAwait(false);
        await dbContext.EventSessionSpeakers.IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoActorConsolidation).Where(x => x.ActorId == sourceActorId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ActorId, targetActorId), cancellationToken).ConfigureAwait(false);
        await dbContext.ActorSubscriptions.IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoActorConsolidation).Where(x => x.TargetActorId == sourceActorId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TargetActorId, targetActorId).SetProperty(x => x.TargetActorTypeId, targetActorTypeId), cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> HasCompletedConsolidationAsync(
        Guid atprotoIdentityId,
        Guid targetActorId,
        string evidenceReference,
        CancellationToken cancellationToken = default)
    {
        if (!evidenceReference.StartsWith($"atproto-identity:{atprotoIdentityId:D};", StringComparison.Ordinal))
        {
            return false;
        }

        return await dbContext.Set<ActorMerge>().AnyAsync(
            merge => merge.TargetActorId == targetActorId
                && merge.ProofKind == ActorMergeProofKind.VerifiedDid
                && merge.EvidenceReference == evidenceReference,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> HasCollisionAsync(Guid sourceActorId, Guid targetActorId, CancellationToken cancellationToken) =>
        await dbContext.EventSessionSpeakers.IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoActorConsolidation).Where(s => s.ActorId == sourceActorId)
            .AnyAsync(s => dbContext.EventSessionSpeakers.IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoActorConsolidation).Any(t => t.ActorId == targetActorId && t.TenantId == s.TenantId && t.EventSessionId == s.EventSessionId), cancellationToken).ConfigureAwait(false)
        || await dbContext.ActorSubscriptions.IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoActorConsolidation).Where(s => s.TargetActorId == sourceActorId)
            .AnyAsync(s => dbContext.ActorSubscriptions.IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoActorConsolidation).Any(t => t.TargetActorId == targetActorId && t.TenantId == s.TenantId && t.SubscriberTenantUserId == s.SubscriberTenantUserId), cancellationToken).ConfigureAwait(false);
}
