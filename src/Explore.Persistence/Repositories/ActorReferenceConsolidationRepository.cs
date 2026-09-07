using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class ActorReferenceConsolidationRepository(ExploreDbContext dbContext) : IActorReferenceConsolidationRepository
{
    public async Task<bool> MoveMutableReferencesAsync(Guid sourceActorId, Guid canonicalActorId, int canonicalActorTypeId, CancellationToken cancellationToken = default)
    {
        if (sourceActorId == Guid.Empty || canonicalActorId == Guid.Empty || sourceActorId == canonicalActorId
            || await HasCollisionAsync(sourceActorId, canonicalActorId, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await dbContext.AtprotoIdentities.Where(x => x.ActorId == sourceActorId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ActorId, canonicalActorId), cancellationToken).ConfigureAwait(false);
        await dbContext.Events.IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoActorConsolidation)
            .Where(x => x.ActorId == sourceActorId || x.OrganizerActorId == sourceActorId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.ActorId, x => x.ActorId == sourceActorId ? canonicalActorId : x.ActorId)
                .SetProperty(x => x.OrganizerActorId, x => x.OrganizerActorId == sourceActorId ? canonicalActorId : x.OrganizerActorId), cancellationToken).ConfigureAwait(false);
        await dbContext.EventSeries.IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoActorConsolidation).Where(x => x.ActorId == sourceActorId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ActorId, canonicalActorId), cancellationToken).ConfigureAwait(false);
        await dbContext.EventSessionSpeakers.IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoActorConsolidation).Where(x => x.ActorId == sourceActorId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ActorId, canonicalActorId), cancellationToken).ConfigureAwait(false);
        await dbContext.ActorSubscriptions.IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoActorConsolidation).Where(x => x.TargetActorId == sourceActorId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TargetActorId, canonicalActorId).SetProperty(x => x.TargetActorTypeId, canonicalActorTypeId), cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> HasCompletedConsolidationAsync(
        Guid atprotoIdentityId,
        Guid canonicalActorId,
        string evidenceReference,
        CancellationToken cancellationToken = default)
    {
        if (!evidenceReference.StartsWith($"atproto-identity:{atprotoIdentityId:D};", StringComparison.Ordinal))
        {
            return false;
        }

        return await dbContext.Set<ActorMerge>().AnyAsync(
            merge => merge.CanonicalActorId == canonicalActorId
                && merge.ProofKind == ActorMergeProofKind.VerifiedDid
                && merge.EvidenceReference == evidenceReference,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> HasCollisionAsync(Guid sourceActorId, Guid canonicalActorId, CancellationToken cancellationToken) =>
        await dbContext.EventSessionSpeakers.IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoActorConsolidation).Where(s => s.ActorId == sourceActorId)
            .AnyAsync(s => dbContext.EventSessionSpeakers.IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoActorConsolidation).Any(t => t.ActorId == canonicalActorId && t.TenantId == s.TenantId && t.EventSessionId == s.EventSessionId), cancellationToken).ConfigureAwait(false)
        || await dbContext.ActorSubscriptions.IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoActorConsolidation).Where(s => s.TargetActorId == sourceActorId)
            .AnyAsync(s => dbContext.ActorSubscriptions.IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoActorConsolidation).Any(t => t.TargetActorId == canonicalActorId && t.TenantId == s.TenantId && t.SubscriberTenantUserId == s.SubscriberTenantUserId), cancellationToken).ConfigureAwait(false);
}
