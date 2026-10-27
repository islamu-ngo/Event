using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

internal static class ModerationResourceGenerationReader
{
    /// <summary>
    /// Acquires native ancestry only; no destination, document or credential payload is projected.
    /// An exact persisted quarantine replay may have advanced the resource concurrency stamp,
    /// but never relaxes material or ancestry. This reader does not qualify effect authority.
    /// </summary>
    internal static async Task<ModerationResourceGeneration?> AcquireAsync(
        ExploreDbContext context, Guid id, Guid resourceId, Guid hostId, Guid trustDomainId, Guid tenantId,
        long revision, Guid observerId, long writerGeneration, DateTimeOffset observedAt,
        DateTimeOffset validUntil, CancellationToken token, ModerationResourceGeneration? expected = null,
        EventResourceModerationQuarantine? replay = null,
        EventAuthorityProviderOperations.ResourceAncestry? ancestryPlan = null)
    {
        if (replay is not null)
        {
            replay.ValidatePersistedState();
            if (expected is null || replay.TenantId != tenantId || replay.EventResourceId != resourceId
                || replay.EventId != expected.EventId || replay.MaterialVersionId != expected.MaterialVersionId)
                return null;
        }
        var discovered = await context.Set<EventResource>().AsNoTracking()
            .Where(row => row.Id == resourceId && row.TenantId == tenantId && !row.IsDeleted)
            .Select(row => new { row.EventId, row.StorageObjectId }).SingleOrDefaultAsync(token);
        if (discovered is null || expected is not null && discovered.EventId != expected.EventId
            || ancestryPlan is not null && (ancestryPlan.ResourceId != resourceId
                || ancestryPlan.EventId != discovered.EventId || ancestryPlan.StorageObjectId != discovered.StorageObjectId)) return null;
        var discoveredEvent = await context.Events.AsNoTracking()
            .Where(row => row.Id == discovered.EventId && row.TenantId == tenantId && !row.IsDeleted)
            .Select(row => new { row.ActorId, row.OrganizerActorId }).SingleOrDefaultAsync(token);
        if (discoveredEvent is null || expected is not null
            && (discoveredEvent.ActorId != expected.ActorId || discoveredEvent.OrganizerActorId != expected.OrganizerActorId))
            return null;
        if (ancestryPlan is not null && (ancestryPlan.ActorId != discoveredEvent.ActorId
            || ancestryPlan.OrganizerActorId != discoveredEvent.OrganizerActorId)) return null;
        var storageActorId = discovered.StorageObjectId is { } discoveredStorageId
            ? await context.Set<StorageObject>().AsNoTracking()
                .Where(row => row.Id == discoveredStorageId && row.TenantId == tenantId && !row.IsDeleted)
                 .Select(row => row.ActorId).SingleOrDefaultAsync(token) : null;
        if (ancestryPlan is not null && ancestryPlan.StorageActorId != storageActorId) return null;
        foreach (var actorId in new[] { (Guid?)discoveredEvent.ActorId, discoveredEvent.OrganizerActorId, storageActorId }
            .OfType<Guid>().Distinct().Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<Actor>(context, actorId, token);
        var actor = await context.Actors.AsNoTracking()
            .Where(row => row.Id == discoveredEvent.ActorId && !row.IsDeleted)
            .Select(row => new { row.ConcurrencyStamp }).SingleOrDefaultAsync(token);
        var organizerStamp = discoveredEvent.OrganizerActorId is { } organizerId
            ? await context.Actors.AsNoTracking().Where(row => row.Id == organizerId && !row.IsDeleted)
                .Select(row => (Guid?)row.ConcurrencyStamp).SingleOrDefaultAsync(token) : null;
        if (actor is null || actor.ConcurrencyStamp == Guid.Empty
            || discoveredEvent.OrganizerActorId is not null && (organizerStamp is null || organizerStamp == Guid.Empty))
            return null;
        if (storageActorId is { } storageActor && !await context.Actors.AsNoTracking()
            .AnyAsync(row => row.Id == storageActor && !row.IsDeleted, token))
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<Event>(context, discovered.EventId, token);
        var owningEvent = await context.Events.AsNoTracking()
            .Where(row => row.Id == discovered.EventId && row.TenantId == tenantId && !row.IsDeleted)
            .Select(row => new { row.ConcurrencyStamp, row.ActorId, row.OrganizerActorId }).SingleOrDefaultAsync(token);
        if (owningEvent is null || owningEvent.ConcurrencyStamp == Guid.Empty
            || owningEvent.ActorId != discoveredEvent.ActorId || owningEvent.OrganizerActorId != discoveredEvent.OrganizerActorId)
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<EventResource>(context, resourceId, token);
        var resource = await context.Set<EventResource>().AsNoTracking()
            .Where(row => row.Id == resourceId && row.EventId == discovered.EventId
                && row.TenantId == tenantId && !row.IsDeleted)
            .Select(row => new
            {
                row.ConcurrencyStamp,
                row.MaterialVersionId,
                row.EventResourceDeliveryTypeId,
                row.StorageObjectId,
                HasDestination = row.ExternalDestinationCiphertext != null && row.ExternalDestinationProtectionVersion != null
            }).SingleOrDefaultAsync(token);
        if (resource is null || resource.ConcurrencyStamp == Guid.Empty
            || resource.MaterialVersionId is not { } materialVersionId || materialVersionId == Guid.Empty
            || ancestryPlan is not null && resource.StorageObjectId != ancestryPlan.StorageObjectId
            || expected is not null && (replay is null && resource.ConcurrencyStamp != expected.ResourceStamp
                || materialVersionId != expected.MaterialVersionId
                || resource.StorageObjectId != expected.StorageObjectId
                || resource.EventResourceDeliveryTypeId != (int)expected.DeliveryType)) return null;
        Guid? storageStamp = null;
        Guid? bindingId = null;
        string? provider = null;
        if (resource.EventResourceDeliveryTypeId == (int)EventResourceDeliveryTypeEnum.StoredFile)
        {
            if (resource.StorageObjectId is not { } storageId) return null;
            await RelationalEntityRowFence.AcquireGlobalAsync<StorageObject>(context, storageId, token);
            var storage = await context.Set<StorageObject>().AsNoTracking()
                .Where(row => row.Id == storageId && row.TenantId == tenantId && !row.IsDeleted
                    && row.OwningResourceKind == StorageOwningResourceKinds.EventResource
                    && row.OwningResourceId == resourceId && row.Purpose == StorageObjectPurposes.EventResource)
                .Select(row => new { row.ConcurrencyStamp, row.StorageProviderBindingId, row.Provider, row.ActorId })
                .SingleOrDefaultAsync(token);
            if (storage is null || storage.ConcurrencyStamp == Guid.Empty
                || storage.ActorId != storageActorId
                || storage.Provider is not (StorageProviders.Local or StorageProviders.S3Compatible)
                || storage.StorageProviderBindingId is not { } nativeBindingId
                || expected is not null && (storage.ConcurrencyStamp != expected.StorageObjectStamp
                    || nativeBindingId != expected.StorageProviderBindingId || storage.Provider != expected.StorageProvider))
                return null;
            await RelationalEntityRowFence.AcquireGlobalAsync<StorageProviderBinding>(context, nativeBindingId, token);
            if (!await context.Set<StorageProviderBinding>().AsNoTracking()
                .AnyAsync(row => row.Id == nativeBindingId && row.Provider == storage.Provider, token)) return null;
            storageStamp = storage.ConcurrencyStamp;
            bindingId = nativeBindingId;
            provider = storage.Provider;
        }
        else if (resource.EventResourceDeliveryTypeId != (int)EventResourceDeliveryTypeEnum.ExternalLink
            || resource.StorageObjectId is not null || !resource.HasDestination)
            return null;
        var result = new ModerationResourceGeneration
        {
            Id = id,
            HostBindingId = hostId,
            TrustDomainId = trustDomainId,
            TenantId = tenantId,
            ResourceId = resourceId,
            Revision = revision,
            ObservedByHumanId = observerId,
            WriterGeneration = writerGeneration,
            ObservedAt = observedAt,
            ValidUntil = validUntil,
            ResourceStamp = resource.ConcurrencyStamp,
            MaterialVersionId = materialVersionId,
            EventId = discovered.EventId,
            EventStamp = owningEvent.ConcurrencyStamp,
            ActorId = owningEvent.ActorId,
            ActorStamp = actor.ConcurrencyStamp,
            OrganizerActorId = owningEvent.OrganizerActorId,
            OrganizerActorStamp = organizerStamp,
            DeliveryType = (EventResourceDeliveryTypeEnum)resource.EventResourceDeliveryTypeId,
            StorageObjectId = resource.StorageObjectId,
            StorageObjectStamp = storageStamp,
            StorageProviderBindingId = bindingId,
            StorageProvider = provider
        };
        result.ValidatePersistedState();
        return result;
    }
}
