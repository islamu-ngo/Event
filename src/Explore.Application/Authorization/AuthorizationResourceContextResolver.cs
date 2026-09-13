using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Exceptions;
using Explore.Application.Features.Notifications.Requests.Commands;
using Explore.Application.Features.Notifications.Requests.Queries;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Features.StorageObjects.Requests.Queries;
using Explore.Domain;

namespace Explore.Application.Authorization;

/// <summary>
/// Turns a request plus its declared context into the trusted <see cref="IAuthorizationFacts"/> the
/// provider evaluates.
/// <para>
/// For every resource kind that can be loaded server-side, the loaded entity is the authority: request
/// input may select <em>which</em> resource is evaluated, never <em>what</em> is true about it. When the
/// resource cannot be found, or belongs to another tenant, the resolver returns no facts so the provider
/// fails closed.
/// </para>
/// </summary>
public sealed class AuthorizationResourceContextResolver(
    IEventRepository? eventRepository = null,
    IOrganizationMemberRepository? organizationMemberRepository = null,
    IStorageObjectRepository? storageObjectRepository = null,
    IEventSessionRepository? eventSessionRepository = null,
    IRegistrationInventoryRepository? registrationInventoryRepository = null,
    IWebhookOwnershipScopeResolver? webhookOwnershipScopeResolver = null,
    ITenantContext? tenantContext = null,
    IGroupTenantRepository? groupTenantRepository = null,
    IOrganizationTenantRepository? organizationTenantRepository = null,
    IStorageUploadSessionRepository? storageUploadSessionRepository = null)
{
    public async Task<AuthorizationContext> ResolveAsync<TRequest>(
        TRequest request,
        string resourceKind,
        string action,
        string resourceId,
        IAuthorizationFacts? declaredFacts,
        CancellationToken cancellationToken)
        where TRequest : notnull
    {
        if (resourceKind == ResourceKinds.Webhook && request is IWebhookPersistedOwnerRequest persistedOwnerRequest)
        {
            var ownership = await ResolvePersistedWebhookOwnershipAsync(persistedOwnerRequest, cancellationToken);
            return new AuthorizationContext(
                persistedOwnerRequest.OwnedResourceId.ToString("D"),
                WebhookOwnershipAuthorizationFacts.From(ownership));
        }

        if (resourceKind == ResourceKinds.Webhook && request is IWebhookOwnerScopedRequest ownerScopedRequest)
        {
            var ownership = await ResolveWebhookOwnershipAsync(ownerScopedRequest, cancellationToken);
            return new AuthorizationContext(
                ownership.OwnerId.ToString("D"),
                WebhookOwnershipAuthorizationFacts.From(ownership));
        }

        Guid? preferenceGroupId = request switch
        {
            GetGroupNotificationPreferenceMatrixQuery query => query.GroupId,
            UpdateGroupNotificationPreferenceMatrixCommand command => command.GroupId,
            SetGroupNotificationPreferenceMuteCommand command => command.GroupId,
            _ => null
        };
        if (resourceKind == ResourceKinds.Group && preferenceGroupId is { } groupId)
        {
            var groupFacts = await ResolveGroupPreferenceFactsAsync(groupId, action, cancellationToken);
            return new AuthorizationContext(groupId.ToString("D"), groupFacts);
        }

        if (request is FinalizeStorageUploadSessionCommand finalization
            && resourceKind == ResourceKinds.StorageObject
            && action == AuthorizationActions.StorageObjects.Create)
        {
            return new AuthorizationContext(finalization.UploadSessionId.ToString("D"),
                await ResolveStorageUploadFinalizationFactsAsync(finalization.UploadSessionId, cancellationToken));
        }

        if (request is GetStorageObjectContentRequest download
            && resourceKind == ResourceKinds.StorageObject
            && action == AuthorizationActions.StorageObjects.Download)
        {
            var objectId = download.StorageObjectId.ToString("D");
            return new AuthorizationContext(objectId, tenantContext is null ? null :
                await ResolveStorageObjectFactsAsync(objectId, declaredFacts: null, cancellationToken));
        }

        var facts = await ResolveTrustedFactsAsync(resourceKind, resourceId, declaredFacts, cancellationToken);

        if (resourceKind is ResourceKinds.RegistrationForm or ResourceKinds.RegistrationOrder && facts is null)
        {
            throw new AuthorizationException(resourceKind, action);
        }

        return new AuthorizationContext(resourceId, facts);
    }

    private async Task<GroupAuthorizationFacts> ResolveGroupPreferenceFactsAsync(
        Guid groupId,
        string action,
        CancellationToken cancellationToken)
    {
        if (tenantContext is null || groupTenantRepository is null || organizationTenantRepository is null)
            throw new AuthorizationException(ResourceKinds.Group, action);

        var tenantId = tenantContext.TenantId;
        var group = await groupTenantRepository.GetByGroupAndTenant(groupId, tenantId, cancellationToken);
        if (group is null || group.TenantId != tenantId || group.IsDeleted || group.Group.IsDeleted)
            throw new AuthorizationException(ResourceKinds.Group, action);

        OrganizationTenant? parent = null;
        if (group.ParentOrganizationTenantId is { } parentId)
        {
            parent = await organizationTenantRepository.GetById(parentId);
            cancellationToken.ThrowIfCancellationRequested();
            if (parent is null || parent.TenantId != tenantId || parent.IsDeleted)
                throw new AuthorizationException(ResourceKinds.Group, action);
        }

        return new GroupAuthorizationFacts(tenantId, groupId, parent?.OrganizationId);
    }

    private async Task<IAuthorizationFacts?> ResolveTrustedFactsAsync(
        string resourceKind,
        string resourceId,
        IAuthorizationFacts? declaredFacts,
        CancellationToken cancellationToken)
    {
        // A pre-create check has no persisted row to load, so entity resolution cannot apply and must
        // not erase the lifecycle facts the create rules match on.
        if (declaredFacts is PreCreateAuthorizationFacts)
            return declaredFacts;

        return resourceKind switch
        {
            ResourceKinds.Event => await ResolveEventFactsAsync(resourceId, declaredFacts, cancellationToken),
            ResourceKinds.EventOrganizerClaim => await ResolveOrganizerClaimFactsAsync(declaredFacts, cancellationToken),
            ResourceKinds.RegistrationForm => await ResolveRegistrationFormFactsAsync(declaredFacts, cancellationToken),
            ResourceKinds.RegistrationOrder => await ResolveRegistrationOrderFactsAsync(
                resourceId, declaredFacts, cancellationToken),
            ResourceKinds.EventSession => await ResolveEventSessionFactsAsync(resourceId, declaredFacts, cancellationToken),
            ResourceKinds.OrganizationMember => await ResolveOrganizationMemberFactsAsync(resourceId, declaredFacts, cancellationToken),
            ResourceKinds.StorageObject => await ResolveStorageObjectFactsAsync(resourceId, declaredFacts, cancellationToken),
            ResourceKinds.CustomPropertyProjection => await ResolveProjectionFactsAsync(declaredFacts, cancellationToken),
            _ => declaredFacts
        };
    }

    private async Task<IAuthorizationFacts?> ResolveRegistrationOrderFactsAsync(
        string resourceId,
        IAuthorizationFacts? declaredFacts,
        CancellationToken cancellationToken)
    {
        if (registrationInventoryRepository is null ||
            tenantContext is null ||
            !Guid.TryParse(resourceId, out Guid orderId))
        {
            return null;
        }

        RegistrationOrder? order = await registrationInventoryRepository.GetOrderWithLinesAsync(
            orderId,
            tenantContext.TenantId,
            cancellationToken);
        if (order is null ||
            declaredFacts is RegistrationOrderAuthorizationFacts requested &&
            requested.EventId != Guid.Empty &&
            requested.EventId != order.EventId)
        {
            return null;
        }

        return new RegistrationOrderAuthorizationFacts(
            order.TenantId,
            order.EventId,
            order.AccountUserId);
    }

    private async Task<WebhookOwnershipScope> ResolveWebhookOwnershipAsync(
        IWebhookOwnerScopedRequest request,
        CancellationToken cancellationToken)
    {
        if (webhookOwnershipScopeResolver is null)
        {
            throw new AuthorizationException(ResourceKinds.Webhook, "resolve-owner");
        }

        var resolution = await webhookOwnershipScopeResolver.ResolveAsync(
            request.OwnerKindId,
            request.OwnerId,
            cancellationToken);
        return resolution.Scope ?? throw new AuthorizationException(ResourceKinds.Webhook, "resolve-owner");
    }

    private async Task<WebhookOwnershipScope> ResolvePersistedWebhookOwnershipAsync(
        IWebhookPersistedOwnerRequest request,
        CancellationToken cancellationToken)
    {
        if (webhookOwnershipScopeResolver is null)
        {
            throw new AuthorizationException(ResourceKinds.Webhook, "resolve-persisted-owner");
        }

        var resolution = await webhookOwnershipScopeResolver.ResolvePersistedAsync(
            request.OwnedResourceKind,
            request.OwnedResourceId,
            cancellationToken);
        return resolution.Scope ??
            throw new AuthorizationException(ResourceKinds.Webhook, "resolve-persisted-owner");
    }

    /// <summary>
    /// Loads the event and rebuilds its authority facts. Pre-create checks carry no persisted event, so
    /// the request-declared phase facts survive untouched.
    /// </summary>
    private async Task<IAuthorizationFacts?> ResolveEventFactsAsync(
        string resourceId,
        IAuthorizationFacts? declaredFacts,
        CancellationToken cancellationToken)
    {
        if (eventRepository is null || !Guid.TryParse(resourceId, out var eventId))
            return declaredFacts;

        var eventEntity = await eventRepository.GetEventWithDetails(eventId);
        if (!IsInCurrentTenant(eventEntity?.TenantId))
            return null;

        return CreateEventFacts(eventEntity!);
    }

    private async Task<IAuthorizationFacts?> ResolveOrganizerClaimFactsAsync(
        IAuthorizationFacts? declaredFacts,
        CancellationToken cancellationToken)
    {
        if (declaredFacts is EventOrganizerClaimAuthorizationFacts claimFacts)
        {
            return await IsEventInCurrentTenantAsync(claimFacts.EventId) ? claimFacts : null;
        }

        if (declaredFacts is EventScopedAuthorizationFacts scopedFacts)
        {
            return await ResolveEventFactsAsync(
                scopedFacts.EventId.ToString("D"),
                declaredFacts,
                cancellationToken);
        }

        return declaredFacts is EventAuthorizationFacts eventFacts
            ? await ResolveEventFactsAsync(eventFacts.EventId.ToString("D"), declaredFacts, cancellationToken)
            : null;
    }

    /// <summary>
    /// Registration forms authorize as their parent event. A form whose event cannot be loaded inside the
    /// current tenant produces no facts, and the caller converts that into a denial.
    /// </summary>
    private async Task<IAuthorizationFacts?> ResolveRegistrationFormFactsAsync(
        IAuthorizationFacts? declaredFacts,
        CancellationToken cancellationToken)
    {
        var eventId = EventIdOf(declaredFacts);
        if (eventId is null || eventRepository is null)
            return null;

        var eventEntity = await eventRepository.GetEventWithDetails(eventId.Value);
        return IsInCurrentTenant(eventEntity?.TenantId)
            ? CreateEventFacts(eventEntity!)
            : null;
    }

    private async Task<IAuthorizationFacts?> ResolveEventSessionFactsAsync(
        string resourceId,
        IAuthorizationFacts? declaredFacts,
        CancellationToken cancellationToken)
    {
        if (eventSessionRepository is null || !Guid.TryParse(resourceId, out var eventSessionId))
            return declaredFacts;

        var session = await eventSessionRepository.GetSessionWithDetails(eventSessionId);
        if (!IsInCurrentTenant(session?.TenantId))
            return null;

        return new EventScopedAuthorizationFacts(
            session!.TenantId,
            session.EventId,
            session.Id);
    }

    private async Task<IAuthorizationFacts?> ResolveOrganizationMemberFactsAsync(
        string resourceId,
        IAuthorizationFacts? declaredFacts,
        CancellationToken cancellationToken)
    {
        if (organizationMemberRepository is null || !Guid.TryParse(resourceId, out var memberId))
            return declaredFacts;

        var member = await organizationMemberRepository.GetOrganizationMemberWithDetails(memberId);
        if (!IsInCurrentTenant(member?.TenantId))
            return null;

        return new OrganizationMemberAuthorizationFacts(
            member!.TenantId,
            member.OrganizationTenant.OrganizationId,
            member.Id,
            member.UserId);
    }

    private async Task<IAuthorizationFacts?> ResolveStorageUploadFinalizationFactsAsync(
        Guid sessionId, CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty || storageUploadSessionRepository is null || tenantContext is null)
            return null;

        // Read every lifecycle state without tracking: the handler must re-read under its transaction,
        // including finalized retries and expired reservations whose quota still needs releasing.
        var session = await storageUploadSessionRepository.GetForAuthorizationAsync(sessionId, cancellationToken);
        if (session is null || session.TenantId != tenantContext.TenantId)
            return null;

        // Only OrganizationTenant reservations use the evidence-specific authorization contract.
        // Non-OrganizationTenant reservations retain their existing Cerbos create policy and handler checks.
        if (session.OwningResourceKind != StorageOwningResourceKinds.OrganizationTenant)
            return new StorageObjectCollectionAuthorizationFacts(session.TenantId);

        if (session.UserId is not { } ownerUserId)
            return null;

        return new StorageUploadFinalizationFacts(session.Id, session.TenantId, ownerUserId,
            session.Purpose, session.Visibility, session.OwningResourceKind, session.OwningResourceId,
            session.ContentType, session.Extension, session.ExpectedSizeBytes);
    }

    /// <summary>
    /// Collection scopes have no single object to load, so their declared tenant facts stand. A persisted
    /// object is always re-read: visibility and lifecycle drive content access and must not come from input.
    /// </summary>
    private async Task<IAuthorizationFacts?> ResolveStorageObjectFactsAsync(
        string resourceId,
        IAuthorizationFacts? declaredFacts,
        CancellationToken cancellationToken)
    {
        if (declaredFacts is StorageObjectCollectionAuthorizationFacts or StorageUploadIntentFacts)
            return declaredFacts;

        if (storageObjectRepository is null || !Guid.TryParse(resourceId, out var storageObjectId))
            return declaredFacts;

        var storageObject = await storageObjectRepository.GetById(storageObjectId);
        if (!IsInCurrentTenant(storageObject?.TenantId))
            return null;

        return new PersistedStorageObjectAuthorizationFacts(
            storageObject!.TenantId,
            storageObject.Id,
            storageObject.Visibility,
            storageObject.LifecycleState,
            storageObject.CreatedBy,
            storageObject.OwningResourceKind,
            storageObject.OwningResourceId);
    }

    /// <summary>
    /// Custom-property projections are tenant-administered but addressed by event or session, so the
    /// owning aggregate supplies the tenant the policy checks against.
    /// </summary>
    private async Task<IAuthorizationFacts?> ResolveProjectionFactsAsync(
        IAuthorizationFacts? declaredFacts,
        CancellationToken cancellationToken)
    {
        if (declaredFacts is not CustomPropertyProjectionAuthorizationFacts projectionFacts)
            return declaredFacts;

        if (projectionFacts.TenantId != Guid.Empty)
            return projectionFacts;

        if (projectionFacts.EventId is { } eventId && eventRepository is not null)
        {
            var eventEntity = await eventRepository.GetEventWithDetails(eventId);
            return eventEntity is null
                ? projectionFacts
                : projectionFacts with { TenantId = eventEntity.TenantId, EventId = eventEntity.Id };
        }

        if (projectionFacts.EventSessionId is { } eventSessionId && eventSessionRepository is not null)
        {
            var session = await eventSessionRepository.GetSessionWithDetails(eventSessionId);
            return session is null
                ? projectionFacts
                : projectionFacts with
                {
                    TenantId = session.TenantId,
                    EventId = session.EventId,
                    EventSessionId = session.Id
                };
        }

        return projectionFacts;
    }

    private async Task<bool> IsEventInCurrentTenantAsync(Guid eventId)
    {
        if (eventRepository is null)
            return true;

        var eventEntity = await eventRepository.GetEventWithDetails(eventId);
        return IsInCurrentTenant(eventEntity?.TenantId);
    }

    private bool IsInCurrentTenant(Guid? resourceTenantId) =>
        resourceTenantId is not null &&
        (tenantContext is null || resourceTenantId == tenantContext.TenantId);

    private static EventAuthorizationFacts CreateEventFacts(Event eventEntity) => new(
        eventEntity.TenantId,
        eventEntity.Id,
        eventEntity.ActorId,
        eventEntity.Actor?.UserId,
        eventEntity.Actor?.OrganizationId,
        eventEntity.Actor?.GroupId,
        eventEntity.OrganizerActorId,
        eventEntity.OrganizerActor?.UserId,
        eventEntity.OrganizerActor?.OrganizationId,
        eventEntity.OrganizerActor?.GroupId,
        eventEntity.EventProvenanceType?.MasterCode ?? eventEntity.EventProvenanceTypeId.ToString(),
        eventEntity.SubmittedByUserId);

    private static Guid? EventIdOf(IAuthorizationFacts? facts) => facts switch
    {
        EventAuthorizationFacts value => value.EventId,
        EventScopedAuthorizationFacts value => value.EventId,
        EventOrganizerClaimAuthorizationFacts value => value.EventId,
        RegistrationOrderAuthorizationFacts value => value.EventId,
        _ => null
    };
}
