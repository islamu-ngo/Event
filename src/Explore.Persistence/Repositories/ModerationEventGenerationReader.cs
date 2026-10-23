using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

internal static class ModerationEventGenerationReader
{
    internal static async Task<ModerationEventGeneration?> AcquireAsync(
        ExploreDbContext context, Guid id, Guid eventId, Guid hostId, Guid trustDomainId, Guid tenantId,
        long revision, Guid observerId, long writerGeneration, DateTimeOffset observedAt,
        DateTimeOffset validUntil, CancellationToken token, ModerationEventGeneration? expected = null,
        EventAuthorityProviderOperations.Ownership? ownershipPlan = null)
    {
        var discovered = await context.Events.AsNoTracking()
            .Where(row => row.Id == eventId && row.TenantId == tenantId && !row.IsDeleted)
            .Select(row => new { row.ActorId, row.OrganizerActorId }).SingleOrDefaultAsync(token);
        if (discovered is null || expected is not null
            && (expected.EventId != eventId || discovered.ActorId != expected.ActorId
                || discovered.OrganizerActorId != expected.OrganizerActorId))
            return null;
        if (ownershipPlan is not null && (ownershipPlan.Id != eventId
            || ownershipPlan.ActorId != discovered.ActorId || ownershipPlan.OrganizerActorId != discovered.OrganizerActorId))
            return null;
        foreach (var actorId in new[] { (Guid?)discovered.ActorId, discovered.OrganizerActorId }.OfType<Guid>().Distinct().Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<Actor>(context, actorId, token);
        var actorStamp = await context.Actors.AsNoTracking()
            .Where(row => row.Id == discovered.ActorId && !row.IsDeleted)
            .Select(row => (Guid?)row.ConcurrencyStamp).SingleOrDefaultAsync(token);
        var organizerStamp = discovered.OrganizerActorId is { } organizerId
            ? await context.Actors.AsNoTracking().Where(row => row.Id == organizerId && !row.IsDeleted)
                .Select(row => (Guid?)row.ConcurrencyStamp).SingleOrDefaultAsync(token) : null;
        if (actorStamp is null || actorStamp == Guid.Empty
            || discovered.OrganizerActorId is not null && (organizerStamp is null || organizerStamp == Guid.Empty))
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<Event>(context, eventId, token);
        var owningEvent = await context.Events.AsNoTracking()
            .Where(row => row.Id == eventId && row.TenantId == tenantId && !row.IsDeleted)
            .Select(row => new { row.ActorId, row.OrganizerActorId, row.ConcurrencyStamp, row.SubmittedByUserId })
            .SingleOrDefaultAsync(token);
        if (owningEvent is null || owningEvent.ConcurrencyStamp == Guid.Empty
            || owningEvent.ActorId != discovered.ActorId || owningEvent.OrganizerActorId != discovered.OrganizerActorId)
            return null;
        var result = new ModerationEventGeneration
        {
            Id = id,
            HostBindingId = hostId,
            TrustDomainId = trustDomainId,
            TenantId = tenantId,
            EventId = eventId,
            Revision = revision,
            ObservedByHumanId = observerId,
            WriterGeneration = writerGeneration,
            ObservedAt = observedAt,
            ValidUntil = validUntil,
            EventStamp = owningEvent.ConcurrencyStamp,
            ActorId = owningEvent.ActorId,
            ActorStamp = actorStamp.Value,
            OrganizerActorId = owningEvent.OrganizerActorId,
            OrganizerActorStamp = organizerStamp,
            SubmittedByHumanId = owningEvent.SubmittedByUserId
        };
        result.ValidatePersistedState();
        return result;
    }
}
