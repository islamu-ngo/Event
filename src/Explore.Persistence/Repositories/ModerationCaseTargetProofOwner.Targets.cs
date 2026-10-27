using Explore.Domain;
using Explore.Domain.Enums;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationCaseTargetProofOwner
{
    private async Task<EventActorPlan?> ReadEventActorPlanAsync(Guid eventId, CancellationToken token) =>
        await context.Events.AsNoTracking().Where(row => row.Id == eventId)
            .Select(row => new EventActorPlan(row.Id, row.ActorId, row.OrganizerActorId)).SingleOrDefaultAsync(token);

    private async Task<bool> AcquireSubmissionEventActorsAsync(Guid eventId, Guid tenantId, CancellationToken token)
    {
        var plan = await ReadEventActorPlanAsync(eventId, token);
        if (plan is null) return false;
        if (_submissionEventPlan is { } held) return held == plan;
        if (_fences.Contains((typeof(Tenant), tenantId))) return false;
        foreach (var actorId in new[] { (Guid?)plan.ActorId, plan.OrganizerActorId }.OfType<Guid>().Distinct().Order())
            if (!await FenceAsync<Actor>(actorId, true, token)) return false;
        _submissionEventPlan = plan;
        return true;
    }

    private async Task<NativeTarget?> NativeTargetAsync(
        ModerationCaseTargetKind kind, Guid id, Guid? eventId, bool acquire, CancellationToken token)
    {
        if (id == Guid.Empty || eventId == Guid.Empty) return null;
        if (eventId is { } parentId)
        {
            var target = await EventTargetAsync(kind, id, parentId, acquire, token);
            return target is null ? null : target with { EventId = parentId };
        }
        switch (kind)
        {
            case ModerationCaseTargetKind.Actor:
                {
                    if (!await FenceAsync<Actor>(id, acquire, token)) return null;
                    var actor = await context.Actors.AsNoTracking().Where(row => row.Id == id && !row.IsDeleted)
                        .Select(row => new
                        {
                            row.ConcurrencyStamp,
                            row.ActorTypeId,
                            row.UserId,
                            row.OrganizationId,
                            row.GroupId,
                            row.ExternalActorSubjectId,
                            row.ServicePrincipalId,
                            row.IsSuspended
                        }).SingleOrDefaultAsync(token);
                    return actor is not null && actor.ConcurrencyStamp != Guid.Empty
                        ? new(actor.ConcurrencyStamp, null, null, ActorId: id, ActorVersion: actor.ConcurrencyStamp,
                            IdentityFacts: $"{actor.ActorTypeId}|{actor.UserId:D}|{actor.OrganizationId:D}|{actor.GroupId:D}|" +
                                $"{actor.ExternalActorSubjectId:D}|{actor.ServicePrincipalId:D}|{actor.IsSuspended}")
                        : null;
                }
            case ModerationCaseTargetKind.Service:
                {
                    if (!await FenceAsync<ServicePrincipal>(id, acquire, token)) return null;
                    var service = await context.ServicePrincipals.AsNoTracking().Where(row => row.Id == id && !row.IsDeleted)
                        .Select(row => new { row.ConcurrencyStamp, row.Code }).SingleOrDefaultAsync(token);
                    // A service owns its native identity even without a publisher Actor or Event.
                    // Credential ownership/operator/issuer fields are not attribution evidence.
                    return service is not null && service.ConcurrencyStamp != Guid.Empty
                        ? new(service.ConcurrencyStamp, null, null,
                            IdentityFacts: Digest(System.Text.Encoding.UTF8.GetBytes(service.Code))) : null;
                }
            default:
                return null;
        }
    }

    private async Task<NativeTarget?> EventTargetAsync(
        ModerationCaseTargetKind kind, Guid id, Guid eventId, bool acquire, CancellationToken token)
    {
        if (id == Guid.Empty || eventId == Guid.Empty
            || kind is < ModerationCaseTargetKind.EventVersion or > ModerationCaseTargetKind.Destination)
            return null;
        var discovered = await ReadEventActorPlanAsync(eventId, token);
        if (discovered is null) return null;
        var issuancePlan = kind switch
        {
            ModerationCaseTargetKind.EventVersion or ModerationCaseTargetKind.Actor or ModerationCaseTargetKind.Service =>
                _submissionEventPlan ?? _readIssuancePlan?.Event ?? _protectedReadPlan?.Sources.Event,
            _ => null
        };
        if (issuancePlan is not null && issuancePlan != discovered) return null;
        // Publisher fences precede Event, matching the native source mutation order.
        if (kind is ModerationCaseTargetKind.EventVersion or ModerationCaseTargetKind.Actor or ModerationCaseTargetKind.Service)
        {
            foreach (var actorId in new[] { (Guid?)discovered.ActorId, discovered.OrganizerActorId }
                .OfType<Guid>().Distinct().Order())
                if (!await FenceAsync<Actor>(actorId, acquire && issuancePlan is null, token)) return null;
        }
        if (!await FenceAsync<Event>(eventId, acquire, token)) return null;
        var parent = await context.Events.AsNoTracking().Where(row => row.Id == eventId && !row.IsDeleted)
            .Select(row => new { row.ConcurrencyStamp, row.SubmittedByUserId, row.ActorId, row.OrganizerActorId })
            .SingleOrDefaultAsync(token);
        if (parent is null || parent.ConcurrencyStamp == Guid.Empty || parent.ActorId != discovered.ActorId
            || parent.OrganizerActorId != discovered.OrganizerActorId)
            return null;
        switch (kind)
        {
            case ModerationCaseTargetKind.EventVersion:
                return id == eventId ? new(parent.ConcurrencyStamp, parent.ConcurrencyStamp, parent.SubmittedByUserId) : null;
            case ModerationCaseTargetKind.ParticipantIncident:
                {
                    var participant = await NativeParticipantAsync(id, eventId, null, acquire, token);
                    return participant is not null
                        ? new(participant.Version, parent.ConcurrencyStamp, parent.SubmittedByUserId,
                            ParentId: participant.OrderId, ParentVersion: participant.OrderVersion) : null;
                }
            case ModerationCaseTargetKind.Actor:
                {
                    if (id != parent.ActorId && id != parent.OrganizerActorId) return null;
                    var actor = await context.Actors.AsNoTracking().Where(row => row.Id == id && !row.IsDeleted)
                        .Select(row => new { row.ConcurrencyStamp }).SingleOrDefaultAsync(token);
                    return actor is not null && actor.ConcurrencyStamp != Guid.Empty
                        ? new(actor.ConcurrencyStamp, parent.ConcurrencyStamp, parent.SubmittedByUserId,
                            ActorId: id, ActorVersion: actor.ConcurrencyStamp) : null;
                }
            case ModerationCaseTargetKind.Service:
                {
                    // Only the actual publisher Actor's service ownership joins these identities.
                    // No API-key issuer, key owner, operator or similarly valued GUID is substituted.
                    var actor = await context.Actors.AsNoTracking()
                        .Where(row => (row.Id == parent.ActorId || row.Id == parent.OrganizerActorId)
                            && row.ServicePrincipalId == id && !row.IsDeleted)
                        .OrderBy(row => row.Id).Select(row => new { row.Id, row.ConcurrencyStamp })
                        .FirstOrDefaultAsync(token);
                    if (actor is null || actor.ConcurrencyStamp == Guid.Empty
                        || !await FenceAsync<ServicePrincipal>(id, acquire, token)) return null;
                    var service = await context.ServicePrincipals.AsNoTracking().Where(row => row.Id == id && !row.IsDeleted)
                        .Select(row => new { row.ConcurrencyStamp }).SingleOrDefaultAsync(token);
                    return service is not null && service.ConcurrencyStamp != Guid.Empty
                        ? new(service.ConcurrencyStamp, parent.ConcurrencyStamp, parent.SubmittedByUserId,
                            ActorId: actor.Id, ActorVersion: actor.ConcurrencyStamp) : null;
                }
            case ModerationCaseTargetKind.Resource:
            case ModerationCaseTargetKind.Destination:
                {
                    if (!await FenceAsync<EventResource>(id, acquire, token)) return null;
                    var resource = await context.Set<EventResource>().AsNoTracking()
                        .Where(row => row.Id == id && row.EventId == eventId && !row.IsDeleted)
                        .Select(row => new
                        {
                            row.ConcurrencyStamp,
                            row.EventResourceDeliveryTypeId,
                            row.StorageObjectId,
                            HasDestination = row.ExternalDestinationCiphertext != null
                                && row.ExternalDestinationProtectionVersion != null
                        }).SingleOrDefaultAsync(token);
                    if (resource is null || resource.ConcurrencyStamp == Guid.Empty
                        || kind == ModerationCaseTargetKind.Destination
                            && (!resource.HasDestination
                                || resource.EventResourceDeliveryTypeId != (int)EventResourceDeliveryTypeEnum.ExternalLink))
                        return null;
                    var result = new NativeTarget(resource.ConcurrencyStamp, parent.ConcurrencyStamp, parent.SubmittedByUserId);
                    if (resource.StorageObjectId is not { } storageId) return result;
                    if (resource.EventResourceDeliveryTypeId != (int)EventResourceDeliveryTypeEnum.StoredFile
                        || !await FenceAsync<StorageObject>(storageId, acquire, token)) return null;
                    var storage = await context.Set<StorageObject>().AsNoTracking()
                        .Where(row => row.Id == storageId && !row.IsDeleted
                            && row.OwningResourceKind == StorageOwningResourceKinds.EventResource
                            && row.OwningResourceId == id && row.Purpose == StorageObjectPurposes.EventResource)
                        .Select(row => new { row.ConcurrencyStamp, row.StorageProviderBindingId, row.Provider })
                        .SingleOrDefaultAsync(token);
                    if (storage is null || storage.ConcurrencyStamp == Guid.Empty
                        || storage.StorageProviderBindingId is not { } bindingId
                        || !await FenceAsync<StorageProviderBinding>(bindingId, acquire, token)
                        || !await context.Set<StorageProviderBinding>().AsNoTracking()
                            .AnyAsync(row => row.Id == bindingId && row.Provider == storage.Provider, token))
                        return null;
                    // The binding is immutable and has its own native identity, not an invented revision.
                    // Do not retrieve filesystem paths, endpoints, credential references or object bytes.
                    return result with
                    {
                        ParentId = storageId,
                        ParentVersion = storage.ConcurrencyStamp,
                        ProviderId = bindingId,
                        StorageProvider = storage.Provider
                    };
                }
            case ModerationCaseTargetKind.Form:
                {
                    if (!await FenceAsync<RegistrationFormVersion>(id, acquire, token)) return null;
                    var version = await context.Set<RegistrationFormVersion>().AsNoTracking()
                        .Where(row => row.Id == id && row.EventId == eventId && !row.IsDeleted)
                        .Select(row => new
                        {
                            row.ConcurrencyStamp,
                            row.RegistrationFormId,
                            row.SourceKindId,
                            row.ExternalRegistrationProviderConnectionId,
                            row.ExternalRegistrationProviderSchemaRevisionId
                        }).SingleOrDefaultAsync(token);
                    if (version is null || version.ConcurrencyStamp == Guid.Empty
                        || !await FenceAsync<RegistrationForm>(version.RegistrationFormId, acquire, token)) return null;
                    var form = await context.Set<RegistrationForm>().AsNoTracking()
                        .Where(row => row.Id == version.RegistrationFormId && row.EventId == eventId && !row.IsDeleted)
                        .Select(row => new { row.Id, row.ConcurrencyStamp }).SingleOrDefaultAsync(token);
                    if (form is null || form.ConcurrencyStamp == Guid.Empty) return null;
                    var result = new NativeTarget(version.ConcurrencyStamp, parent.ConcurrencyStamp,
                        parent.SubmittedByUserId, ParentId: form.Id, ParentVersion: form.ConcurrencyStamp);
                    if (version.SourceKindId != (int)RegistrationFormVersionSourceKindEnum.ExternalImported)
                        return version.ExternalRegistrationProviderConnectionId is null
                            && version.ExternalRegistrationProviderSchemaRevisionId is null ? result : null;
                    if (version.ExternalRegistrationProviderConnectionId is not { } connectionId
                        || version.ExternalRegistrationProviderSchemaRevisionId is not { } schemaId
                        || !await FenceAsync<RegistrationProviderConnection>(connectionId, acquire, token)
                        || !await FenceAsync<RegistrationProviderSchemaRevision>(schemaId, acquire, token))
                        return null;
                    var connection = await context.Set<RegistrationProviderConnection>().AsNoTracking()
                        .Where(row => row.Id == connectionId && !row.IsDeleted)
                        .Select(row => new { row.ConcurrencyStamp }).SingleOrDefaultAsync(token);
                    var schema = await context.Set<RegistrationProviderSchemaRevision>().AsNoTracking()
                        .Where(row => row.Id == schemaId && row.RegistrationProviderConnectionId == connectionId && !row.IsDeleted)
                        .Select(row => new { row.ProviderSnapshotSha256Hash, row.RevisionHash }).SingleOrDefaultAsync(token);
                    return connection is not null && connection.ConcurrencyStamp != Guid.Empty && schema is not null
                        ? result with
                        {
                            ProviderId = connectionId,
                            ProviderVersion = connection.ConcurrencyStamp,
                            SchemaId = schemaId,
                            SchemaHash = $"{schema.ProviderSnapshotSha256Hash}:{schema.RevisionHash.Value}"
                        } : null;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private async Task<NativeParticipant?> NativeParticipantAsync(
        Guid id, Guid eventId, Guid? callerId, bool acquire, CancellationToken token)
    {
        // Discovery selects the parent fence only. Authority comes from both
        // rows reloaded under the order-before-participant mutation hierarchy.
        var orderId = await context.RegistrationParticipants.AsNoTracking()
            .Where(row => row.Id == id && !row.IsDeleted)
            .Select(row => (Guid?)row.RegistrationOrderId).SingleOrDefaultAsync(token);
        if (orderId is not { } parentId || parentId == Guid.Empty
            || !await FenceAsync<RegistrationOrder>(parentId, acquire, token))
            return null;
        var order = await context.RegistrationOrders.AsNoTracking()
            .Where(row => row.Id == parentId && row.EventId == eventId && !row.IsDeleted)
            .Select(row => new { row.ConcurrencyStamp }).SingleOrDefaultAsync(token);
        if (order is null || order.ConcurrencyStamp == Guid.Empty
            || !await FenceAsync<RegistrationParticipant>(id, acquire, token))
            return null;
        var participant = await context.RegistrationParticipants.AsNoTracking()
            .Where(row => row.Id == id && row.RegistrationOrderId == parentId && !row.IsDeleted
                && (callerId == null || row.LinkedUserId == callerId))
            .Select(row => new { row.ConcurrencyStamp }).SingleOrDefaultAsync(token);
        return participant is not null && participant.ConcurrencyStamp != Guid.Empty
            ? new(participant.ConcurrencyStamp, parentId, order.ConcurrencyStamp) : null;
    }

    private sealed record EventActorPlan(Guid EventId, Guid ActorId, Guid? OrganizerActorId);
    private sealed record NativeParticipant(Guid Version, Guid OrderId, Guid OrderVersion);
}
