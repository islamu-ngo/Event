using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventTemplates.Requests.Commands;
using Explore.Application.Features.EventSessionTemplates.Requests.Commands;
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
    IStorageUploadSessionRepository? storageUploadSessionRepository = null,
    IEventOrganizerClaimRepository? eventOrganizerClaimRepository = null,
    IEventTemplateRepository? eventTemplateRepository = null,
    IEventSessionTemplateRepository? eventSessionTemplateRepository = null)
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
        cancellationToken.ThrowIfCancellationRequested();
        if (resourceKind == ResourceKinds.Tenant && request is CreateEventTemplateCommand)
        {
            if (tenantContext is null || tenantContext.TenantId == Guid.Empty)
                throw new AuthorizationException(resourceKind, action);
            return new AuthorizationContext(tenantContext.TenantId.ToString("D"),
                new TenantScopedAuthorizationFacts(tenantContext.TenantId));
        }
        Guid? templateTargetId = request switch
        {
            CreateEventSessionTemplateCommand sessionCreation => sessionCreation.SessionTemplateDto.EventTemplateId,
            DeleteEventTemplateCommand deletion => deletion.Id,
            UpdateEventTemplateCommand update => update.TemplateId,
            _ => null
        };
        if (resourceKind == ResourceKinds.Tenant && templateTargetId is { } templateId)
        {
            var template = eventTemplateRepository is null ? null
                : await eventTemplateRepository.GetTemplateWithDetails(templateId, cancellationToken);
            if (template is null || template.IsDeleted || !IsInCurrentTenant(template.TenantId))
                throw new AuthorizationException(resourceKind, action);
            return new AuthorizationContext(template.TenantId.ToString("D"), new TenantScopedAuthorizationFacts(template.TenantId));
        }
        Guid? sessionTemplateTargetId = request switch
        {
            DeleteEventSessionTemplateCommand deletion => deletion.Id,
            UpdateEventSessionTemplateCommand update => update.SessionTemplateId,
            _ => null
        };
        if (resourceKind == ResourceKinds.Tenant && sessionTemplateTargetId is { } sessionTemplateId)
        {
            var template = eventSessionTemplateRepository is null ? null
                : await eventSessionTemplateRepository.GetSessionTemplateWithDetails(sessionTemplateId, cancellationToken);
            if (template is null || template.IsDeleted || !IsInCurrentTenant(template.TenantId)
                || template.EventTemplate is not { IsDeleted: false } parent || parent.TenantId != template.TenantId)
                throw new AuthorizationException(resourceKind, action);
            return new AuthorizationContext(template.TenantId.ToString("D"), new TenantScopedAuthorizationFacts(template.TenantId));
        }
        if (declaredFacts is PreCreateAuthorizationFacts creation)
        {
            if (action != AuthorizationActions.Create
                || resourceKind != ResourceKinds.Organization
                    && (tenantContext is null || tenantContext.TenantId == Guid.Empty
                        || creation.TenantId != tenantContext.TenantId)
                || resourceKind == ResourceKinds.Organization && creation.TenantId != Guid.Empty)
                throw new AuthorizationException(resourceKind, action);

            if (creation.ParentEventId is { } parentEventId
                && !await IsEventInCurrentTenantAsync(parentEventId, cancellationToken))
                throw new AuthorizationException(resourceKind, action);
        }

        if (resourceKind == ResourceKinds.EventResource)
        {
            if (tenantContext is null || tenantContext.TenantId == Guid.Empty
                || !Guid.TryParse(resourceId, out Guid targetId) || targetId == Guid.Empty)
                throw new AuthorizationException(resourceKind, action);
            bool validTarget = declaredFacts switch
            {
                null => true,
                EventResourceTargetAuthorizationFacts target => target.TenantId == tenantContext.TenantId && target.ResourceId == targetId,
                EventResourceCollectionAuthorizationFacts collection => collection.TenantId == tenantContext.TenantId
                    && collection.EventId == targetId && action is "view-management" or "export",
                _ => false
            };
            if (!validTarget) throw new AuthorizationException(resourceKind, action);
            // Keep database resolution inside the resource authority A/B protocol, not the decorator.
            return new AuthorizationContext(targetId.ToString("D"),
                declaredFacts ?? new EventResourceTargetAuthorizationFacts(tenantContext.TenantId, targetId));
        }

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

        if (request is GetStorageObjectListRequest && resourceKind == ResourceKinds.StorageObject)
        {
            if (declaredFacts is not StorageObjectCollectionAuthorizationFacts collection
                || !IsInCurrentTenant(collection.TenantId))
                throw new AuthorizationException(resourceKind, action);
            return new AuthorizationContext(resourceId, new StorageObjectCollectionAuthorizationFacts(tenantContext!.TenantId));
        }

        if (request is FinalizeStorageUploadSessionCommand finalization
            && resourceKind == ResourceKinds.StorageObject
            && action == AuthorizationActions.StorageObjects.Create)
        {
            return new AuthorizationContext(finalization.UploadSessionId.ToString("D"),
                await ResolveStorageUploadFinalizationFactsAsync(finalization.UploadSessionId, cancellationToken));
        }

        if (request is CancelStorageUploadSessionCommand cancellation
            && resourceKind == ResourceKinds.StorageObject && action == AuthorizationActions.Delete)
        {
            var uploadFacts = await ResolveStorageUploadFinalizationFactsAsync(cancellation.UploadSessionId, cancellationToken);
            if (uploadFacts is null)
                throw new AuthorizationException(resourceKind, action);
            return new AuthorizationContext(cancellation.UploadSessionId.ToString("D"),
                uploadFacts is EventResourceUploadAuthorizationFacts ? uploadFacts
                    : new StorageObjectCollectionAuthorizationFacts(tenantContext!.TenantId));
        }

        if (request is GetStorageObjectContentRequest download
            && resourceKind == ResourceKinds.StorageObject
            && action == AuthorizationActions.StorageObjects.Download)
        {
            var objectId = download.StorageObjectId.ToString("D");
            // Do not populate EF's identity map before the awaited policy decision: the content
            // reader must load current metadata after authorization, not reuse this snapshot.
            var snapshot = tenantContext is null || storageObjectRepository is null ? null :
                await storageObjectRepository.GetForAuthorizationAsync(download.StorageObjectId, tenantContext.TenantId, cancellationToken);
            return new AuthorizationContext(objectId, snapshot is null ? null : CreateStorageObjectFacts(snapshot));
        }

        var facts = await ResolveTrustedFactsAsync(resourceKind, resourceId, declaredFacts, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        if (facts is null && (resourceKind is ResourceKinds.Event or ResourceKinds.EventSession
            or ResourceKinds.OrganizationMember or ResourceKinds.CustomPropertyProjection
            or ResourceKinds.EventOrganizerClaim or ResourceKinds.RegistrationForm or ResourceKinds.RegistrationOrder
            || resourceKind == ResourceKinds.StorageObject && action != AuthorizationActions.Create))
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
            ResourceKinds.EventOrganizerClaim => await ResolveOrganizerClaimFactsAsync(resourceId, declaredFacts, cancellationToken),
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
        if (order is null || order.IsDeleted || !IsInCurrentTenant(order.TenantId) ||
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
        if (eventRepository is null || !Guid.TryParse(resourceId, out var eventId) || eventId == Guid.Empty)
            return null;

        var eventEntity = await eventRepository.GetAuthorizationTargetByIdAsync(eventId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsInCurrentTenant(eventEntity?.TenantId) || eventEntity!.IsDeleted)
            return null;

        return CreateEventFacts(eventEntity!);
    }

    private async Task<IAuthorizationFacts?> ResolveOrganizerClaimFactsAsync(
        string resourceId,
        IAuthorizationFacts? declaredFacts,
        CancellationToken cancellationToken)
    {
        if (declaredFacts is EventOrganizerClaimAuthorizationFacts claimFacts)
        {
            if (eventOrganizerClaimRepository is null || !Guid.TryParse(resourceId, out Guid claimId)
                || claimId != claimFacts.ClaimId)
                return null;
            var claim = await eventOrganizerClaimRepository.GetDetailsAsync(claimId, false, cancellationToken);
            if (claim is null || claim.IsDeleted || !IsInCurrentTenant(claim.TenantId)
                || claim.ClaimantActor is null || claim.ClaimantActor.IsDeleted
                || !await IsEventInCurrentTenantAsync(claim.EventId, cancellationToken))
                return null;
            return new EventOrganizerClaimAuthorizationFacts(claim.TenantId, claim.EventId, claim.Id,
                claim.ClaimantActorId, claim.ClaimantActor.UserId, claim.ClaimantActor.OrganizationId,
                claim.ClaimantActor.GroupId, claim.Status?.MasterCode ?? claim.StatusId.ToString());
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

        var eventEntity = await eventRepository.GetEventWithDetails(eventId.Value, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return IsInCurrentTenant(eventEntity?.TenantId) && !eventEntity!.IsDeleted
            ? CreateEventFacts(eventEntity!)
            : null;
    }

    private async Task<IAuthorizationFacts?> ResolveEventSessionFactsAsync(
        string resourceId,
        IAuthorizationFacts? declaredFacts,
        CancellationToken cancellationToken)
    {
        if (eventSessionRepository is null || !Guid.TryParse(resourceId, out var eventSessionId) || eventSessionId == Guid.Empty)
            return null;

        var session = await eventSessionRepository.GetSessionWithDetails(eventSessionId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsInCurrentTenant(session?.TenantId) || session!.IsDeleted
            || !await IsEventInCurrentTenantAsync(session.EventId, cancellationToken))
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
        if (organizationMemberRepository is null || !Guid.TryParse(resourceId, out var memberId) || memberId == Guid.Empty)
            return null;

        var member = await organizationMemberRepository.GetOrganizationMemberWithDetails(memberId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsInCurrentTenant(member?.TenantId) || member!.IsDeleted
            || member.OrganizationTenant.IsDeleted || member.OrganizationTenant.Organization.IsDeleted
            || member.OrganizationTenant.TenantId != member.TenantId)
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

        if (session.Purpose == StorageObjectPurposes.EventResource || session.OwningResourceKind == StorageOwningResourceKinds.EventResource
            || session.ExpectedResourceVersion is not null)
        {
            if (session.Purpose != StorageObjectPurposes.EventResource || session.Visibility != StorageObjectVisibilities.PrivateOwner
                || session.OwningResourceKind != StorageOwningResourceKinds.EventResource
                || session.OwningResourceId is not { } resourceId || resourceId == Guid.Empty
                || session.ExpectedResourceVersion is null || session.UserId is null)
                throw new AuthorizationException(ResourceKinds.EventResource, "update");
            return new EventResourceUploadAuthorizationFacts(session.TenantId, session.Id, resourceId);
        }

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
        if (declaredFacts is StorageUploadIntentFacts upload)
            return IsInCurrentTenant(upload.TenantId) ? upload : null;

        if (storageObjectRepository is null || !Guid.TryParse(resourceId, out var storageObjectId) || storageObjectId == Guid.Empty)
            return null;

        var storageObject = await storageObjectRepository.GetForGenericAccessAsync(storageObjectId, cancellationToken);
        if (!IsInCurrentTenant(storageObject?.TenantId))
            return null;

        return CreateStorageObjectFacts(storageObject!);
    }

    private static PersistedStorageObjectAuthorizationFacts CreateStorageObjectFacts(StorageObject storageObject) =>
        new(storageObject.TenantId, storageObject.Id, storageObject.Visibility, storageObject.LifecycleState,
            storageObject.CreatedBy, storageObject.OwningResourceKind, storageObject.OwningResourceId);

    /// <summary>
    /// Custom-property projections are tenant-administered but addressed by event or session, so the
    /// owning aggregate supplies the tenant the policy checks against.
    /// </summary>
    private async Task<IAuthorizationFacts?> ResolveProjectionFactsAsync(
        IAuthorizationFacts? declaredFacts,
        CancellationToken cancellationToken)
    {
        if (declaredFacts is not CustomPropertyProjectionAuthorizationFacts projectionFacts)
            return null;

        if (projectionFacts.EventSessionId is { } eventSessionId && eventSessionRepository is not null)
        {
            var session = await eventSessionRepository.GetSessionWithDetails(eventSessionId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return session is null || session.IsDeleted || !IsInCurrentTenant(session.TenantId)
                || projectionFacts.EventId is { } requestedParent && requestedParent != session.EventId
                || !await IsEventInCurrentTenantAsync(session.EventId, cancellationToken)
                ? null
                : new CustomPropertyProjectionAuthorizationFacts(session.TenantId, session.EventId, session.Id);
        }

        if (projectionFacts.EventSessionId is not null)
            return null;

        // Tenant-wide rebuild/status requests have no single parent. Their scope
        // must match the server lease; the selected provider still checks the role.
        if (projectionFacts.EventId is null)
            return IsInCurrentTenant(projectionFacts.TenantId)
                ? new CustomPropertyProjectionAuthorizationFacts(tenantContext!.TenantId) : null;

        if (projectionFacts.EventId is { } eventId && eventRepository is not null)
        {
            var eventEntity = await eventRepository.GetEventWithDetails(eventId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return eventEntity is null || eventEntity.IsDeleted || !IsInCurrentTenant(eventEntity.TenantId)
                ? null
                : new CustomPropertyProjectionAuthorizationFacts(eventEntity.TenantId, eventEntity.Id);
        }

        return null;
    }

    private async Task<bool> IsEventInCurrentTenantAsync(Guid eventId, CancellationToken cancellationToken)
    {
        if (eventRepository is null)
            return false;

        var eventEntity = await eventRepository.GetAuthorizationTargetByIdAsync(eventId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return IsInCurrentTenant(eventEntity?.TenantId) && !eventEntity!.IsDeleted;
    }

    private bool IsInCurrentTenant(Guid? resourceTenantId) =>
        tenantContext is not null && tenantContext.TenantId != Guid.Empty &&
        resourceTenantId == tenantContext.TenantId;

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
