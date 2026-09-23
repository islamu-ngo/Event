using System.Security.Cryptography;
using System.Text;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Services;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Services;

/// <summary>Builds immutable resource-authority snapshots exclusively from fresh, bounded repository reads.</summary>
public sealed partial class EventResourceAuthoritySnapshotReader(
    IEventResourceRepository resources,
    IEventAuthoritySnapshotService eventAuthority,
    IEventResourceGovernancePolicyReader governance) : IEventResourceAuthoritySnapshotReader
{
    private const int FactLimit = EventResourceAuthorityRequest.MaximumBatchChecks;

    public async Task<EventResourceAuthorizationFacts?> ReadAsync(
        EventResourceAuthorityRequest request,
        DateTimeOffset evaluationUtc,
        CancellationToken cancellationToken) =>
        (await ReadBatchAsync([request], evaluationUtc, cancellationToken))[0];

    public async Task<IReadOnlyList<EventResourceAuthorizationFacts?>> ReadBatchAsync(
        IReadOnlyList<EventResourceAuthorityRequest> requests,
        DateTimeOffset evaluationUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count > EventResourceAuthorityRequest.MaximumBatchChecks)
            throw new ArgumentOutOfRangeException(nameof(requests));
        if (requests.Count == 0) return [];
        if (requests.Select(request => request.ResourceId).Where(id => id != Guid.Empty)
            .Distinct().Count() > EventResourceAuthorityRequest.MaximumBatchResources)
            throw new ArgumentOutOfRangeException(nameof(requests));

        EventResourceAuthorizationFacts?[] result = new EventResourceAuthorizationFacts?[requests.Count];
        if (evaluationUtc.Offset != TimeSpan.Zero) return result;

        EventResourceAuthorityRequest first = requests[0];
        if (first.TenantId == Guid.Empty || first.SubjectUserId == Guid.Empty
            || requests.Any(request => request.TenantId != first.TenantId
                || request.SubjectUserId != first.SubjectUserId
                || request.IsMachineCaller != first.IsMachineCaller))
            return result;

        Guid tenantId = first.TenantId;
        EventResourceGovernancePolicy? governancePolicy = await governance.ReadAsync(tenantId, cancellationToken);
        Guid[] resourceIds = requests.Where(request => !request.TargetsParentEvent && request.ResourceId != Guid.Empty)
            .Select(request => request.ResourceId).Distinct().ToArray();
        IReadOnlyList<EventResource> resourceRows = await resources.GetAuthorityResourcesAsync(
            tenantId, resourceIds, cancellationToken);
        Dictionary<Guid, EventResource> resourcesById = resourceRows.ToDictionary(value => value.Id);

        Guid[] eventIds = requests.Select(request => request.TargetsParentEvent
                ? request.ResourceId
                : resourcesById.GetValueOrDefault(request.ResourceId)?.EventId ?? Guid.Empty)
            .Where(id => id != Guid.Empty).Distinct().ToArray();
        IReadOnlyList<Event> eventRows = await resources.GetAuthorityEventsAsync(tenantId, eventIds, cancellationToken);
        Dictionary<Guid, Event> eventsById = eventRows.ToDictionary(value => value.Id);
        HashSet<Guid> publishedEligibleEventIds = (await resources.GetPublishedEligibleEventsAsync(
            tenantId, eventIds, cancellationToken)).Select(value => value.Id).ToHashSet();

        Guid[] sessionIds = resourceRows.Select(value => value.EventSessionId)
            .Concat(resourceRows.SelectMany(value => value.AudienceRules.Select(rule => rule.EventSessionId)))
            .OfType<Guid>().Distinct().ToArray();
        IReadOnlyList<EventSession> sessions = await resources.GetSessionsAsync(
            tenantId, eventIds, sessionIds, FactLimit, cancellationToken);
        Dictionary<Guid, EventSession> sessionsById = sessions.ToDictionary(value => value.Id);

        Guid[] storageIds = resourceRows.Select(value => value.StorageObjectId).OfType<Guid>().Distinct().ToArray();
        Dictionary<Guid, StorageObject> storageById = (await resources.GetStorageObjectsAsync(
            tenantId, storageIds, cancellationToken)).ToDictionary(value => value.Id);

        Guid[] moderationEventIds = requests.Where(request => request.Action == "moderate")
            .Select(request => resourcesById.GetValueOrDefault(request.ResourceId)?.EventId)
            .OfType<Guid>().Where(eventsById.ContainsKey).Distinct().ToArray();
        SubjectBatchFacts subject = await ReadSubjectBatchFactsAsync(
            first, eventRows, resourceRows, sessionsById, evaluationUtc, moderationEventIds, cancellationToken);

        for (int index = 0; index < requests.Count; index++)
        {
            EventResourceAuthorityRequest request = requests[index];
            if (request.ResourceId == Guid.Empty || request.SubjectUserId == Guid.Empty) continue;
            if (request.IsEventCollection && request.Action is not ("view-management" or "export")) continue;

            EventResource? resource = request.TargetsParentEvent ? null : resourcesById.GetValueOrDefault(request.ResourceId);
            if (!request.TargetsParentEvent && resource is null || resource?.IsDeleted == true) continue;
            Guid eventId = resource?.EventId ?? request.ResourceId;
            if (!eventsById.TryGetValue(eventId, out Event? parentEvent) || parentEvent.IsDeleted) continue;

            EventSession? resourceSession = null;
            if (resource?.EventSessionId is { } resourceSessionId
                && (!sessionsById.TryGetValue(resourceSessionId, out resourceSession)
                    || resourceSession.EventId != eventId || resourceSession.IsDeleted))
                continue;

            IReadOnlyList<EventResourceAudienceFact> audience = resource is null
                ? subject.CommonAudience.GetValueOrDefault(eventId) ?? []
                : BuildResourceAudience(request, resource, subject, sessionsById);
            bool organizer = subject.ControlledActorIds.Contains(parentEvent.OrganizerActorId ?? Guid.Empty);
            bool eventEligible = publishedEligibleEventIds.Contains(eventId)
                && (parentEvent.VisibilityTypeId == (int)VisibilityTypeEnum.Public
                    || request.SubjectUserId.HasValue && !request.IsMachineCaller
                        && IsAuthenticatedParentVisible(parentEvent, subject.TenantMember, organizer, audience));
            EventResourceParentFacts parent = new(
                tenantId, eventId, resource?.EventSessionId, (EventStatusEnum)parentEvent.EventStatusId,
                parentEvent.IsDeleted, eventEligible,
                resourceSession is null ? null : (EventSessionStatusEnum)resourceSession.EventSessionStatusId,
                resourceSession?.IsDeleted ?? false,
                new(parentEvent.FirstSessionStartUtc, parentEvent.LastSessionEndUtc,
                    resourceSession?.StartTime, resourceSession?.EndTime));

            IReadOnlyList<EventResourcePermissionGrant> grants = subject.PermissionGrants.GetValueOrDefault(eventId) ?? [];
            EventResourceManagementFacts management = new(
                new EventResourceTimedAuthority(organizer), new EventResourceTimedAuthority(subject.TenantMember),
                new EventResourceTimedAuthority(subject.CanModerate),
                grants, managementCeiling: resource is not null || request.IsEventCollection || governancePolicy is
                {
                    MaxActiveResources: > 0, EnabledDeliveryTypes.Count: > 0, EnabledAudiences.Count: > 0
                }, publicationCeiling: governancePolicy is not null);
            if (resource is null)
            {
                result[index] = new(parent, parentEvent.ConcurrencyStamp, request.SubjectUserId,
                    request.IsMachineCaller, management, governancePolicy);
                continue;
            }

            (bool payloadSafe, string generation) = ReadAttachment(resource, storageById, governancePolicy);
            result[index] = new(resource,
                new(tenantId, request.SubjectUserId, request.IsMachineCaller, parent, audience, payloadSafe, governancePolicy),
                management, generation, request.Action == "moderate" && subject.ModerationPrincipal is { } principal
                    ? new(principal, CaptureNativeParent(parentEvent)) : null);
        }
        return result;
    }

    private async Task<SubjectBatchFacts> ReadSubjectBatchFactsAsync(
        EventResourceAuthorityRequest request,
        IReadOnlyList<Event> events,
        IReadOnlyList<EventResource> resourceRows,
        IReadOnlyDictionary<Guid, EventSession> sessionsById,
        DateTimeOffset evaluationUtc,
        IReadOnlyCollection<Guid> moderationEventIds,
        CancellationToken cancellationToken)
    {
        Guid[] eventIds = events.Select(value => value.Id).ToArray();
        if (request.IsMachineCaller || request.SubjectUserId is not { } userId || eventIds.Length == 0)
            return SubjectBatchFacts.Empty;

        TenantUser? tenantUser = await resources.GetTenantUserAsync(request.TenantId, userId, cancellationToken);
        bool tenantMember = tenantUser is { IsDeleted: false, StatusId: (int)TenantUserStatusEnum.Active };
        IReadOnlyList<RegistrationParticipant> participants = await resources.GetSubjectParticipantsAsync(
            request.TenantId, eventIds, userId, FactLimit, cancellationToken);
        IReadOnlyList<EventRegistration> registrations = await resources.GetSubjectRegistrationsAsync(
            request.TenantId, eventIds, userId, FactLimit, cancellationToken);
        IReadOnlyList<ParticipantAdmissionEligibility> eligibility = await resources.GetSubjectEligibilityAsync(
            request.TenantId, eventIds, userId, FactLimit, cancellationToken);
        IReadOnlyList<AdmissionTicket> tickets = await resources.GetSubjectTicketsAsync(
            request.TenantId, eventIds, userId, FactLimit, cancellationToken);
        IReadOnlyList<TicketTypeEntitlement> entitlements = await resources.GetTicketEntitlementsAsync(
            request.TenantId, eventIds, tickets.Select(value => value.EventTicketTypeId).Distinct().ToArray(),
            FactLimit, cancellationToken);
        Guid[] targetIds = resourceRows.SelectMany(value => value.AudienceRules)
            .Where(rule => rule.AudienceKindId == (int)EventResourceAudienceKindEnum.CheckedInParticipant)
            .Select(rule => rule.AdmissionTargetId).OfType<Guid>().Distinct().ToArray();
        IReadOnlyList<AdmissionTarget> targets = await resources.GetAdmissionTargetsAsync(
            request.TenantId, eventIds, targetIds, FactLimit, cancellationToken);
        IReadOnlyList<AdmissionCheckInState> checkIns = await resources.GetCheckInStatesAsync(
            request.TenantId, tickets.Select(value => value.Id).ToArray(), targetIds, FactLimit, cancellationToken);
        IReadOnlyList<EventSessionSpeaker> speakers = await resources.GetEventSpeakersAsync(
            request.TenantId, eventIds, FactLimit, cancellationToken);

        Actor[] actors = speakers.Select(value => value.Actor)
            .Concat(events.Select(value => value.OrganizerActor))
            .OfType<Actor>().DistinctBy(value => value.Id).ToArray();
        HashSet<Guid> controlledActors = await ResolveControlledActorsAsync(
            request.TenantId, userId, tenantUser, actors, cancellationToken);

        IReadOnlyList<EventRoleAssignment> assignments = await resources.GetEventRoleAssignmentsAsync(
            request.TenantId, eventIds, userId, FactLimit, cancellationToken);
        IReadOnlyList<RolePermission> rolePermissions = await resources.GetRolePermissionsAsync(
            assignments.Select(value => value.RoleId).Distinct().ToArray(), FactLimit, cancellationToken);
        EventAuthoritySnapshot authority = await eventAuthority.GetForUserAndEventsAsync(
            request.TenantId, userId, eventIds, evaluationUtc.UtcDateTime, cancellationToken);

        Dictionary<Guid, IReadOnlyList<EventResourceAudienceFact>> common = new();
        Dictionary<Guid, IReadOnlyList<EventResourcePermissionGrant>> grantsByEvent = new();
        foreach (Event parentEvent in events)
        {
            List<EventResourceAudienceFact> audience = [];
            if (tenantMember)
                audience.Add(AudienceFact(request, parentEvent.Id, EventResourceAudienceKindEnum.AuthenticatedTenantMember));

            foreach (RegistrationParticipant participant in participants.Where(value => !value.IsDeleted
                         && value.LinkedUserId == userId && value.RegistrationOrder?.EventId == parentEvent.Id
                         && IsAllowedParticipant(value) && IsOrderCurrent(value.RegistrationOrder)))
            {
                RegistrationOrder order = participant.RegistrationOrder!;
                foreach (EventRegistration registration in registrations.Where(value => !value.IsDeleted
                             && value.EventId == parentEvent.Id && value.RegistrationParticipantId == participant.Id
                             && value.RegistrationOrderId == order.Id && value.LinkedUserId == userId
                             && sessionsById.TryGetValue(value.EventSessionId, out EventSession? session)
                             && session.EventId == parentEvent.Id && IsEligibleSession(session)))
                {
                    ParticipantAdmissionEligibility[] states = eligibility.Where(value =>
                        value.EventId == parentEvent.Id && value.ParticipantId == participant.Id
                        && value.RegistrationOrderId == order.Id && value.SubjectUserId == userId
                        && registration.RegistrationOrderLineId is { } registrationLineId
                        && value.RegistrationOrderLineId == registrationLineId
                        && value.RevokedAt is null
                        && value.RegistrationTicketAssignment is
                        {
                            AssignmentStatusId: (int)AssignmentStatusEnum.Assigned,
                            ParticipantId: var assignedParticipant,
                            RegistrationOrderId: var assignedOrder,
                            RegistrationOrderLineId: var assignedLine
                        }
                        && assignedParticipant == participant.Id && assignedOrder == order.Id
                        && assignedLine == registrationLineId).ToArray();
                    if (states.Length == 0)
                    {
                        audience.Add(SessionRegistrantFact(request, parentEvent.Id, registration.EventSessionId,
                            order, null, userId));
                    }
                    else
                    {
                        audience.AddRange(states.Select(state => SessionRegistrantFact(
                            request, parentEvent.Id, registration.EventSessionId, order, state, userId)));
                    }
                }
            }

            foreach (EventSessionSpeaker speaker in speakers.Where(value => value.EventSession.EventId == parentEvent.Id
                         && !value.EventSession.IsDeleted && IsEligibleSession(value.EventSession)
                         && controlledActors.Contains(value.ActorId)
                         && value.Actor is { IsDeleted: false, IsSuspended: false }))
            {
                audience.Add(AudienceFact(request, parentEvent.Id, EventResourceAudienceKindEnum.AnyEventSessionSpeaker)
                    with { EventSessionId = speaker.EventSessionId });
                audience.Add(AudienceFact(request, parentEvent.Id, EventResourceAudienceKindEnum.SessionSpeaker)
                    with { EventSessionId = speaker.EventSessionId });
            }
            if (parentEvent.OrganizerActorId is { } organizerId && controlledActors.Contains(organizerId))
                audience.Add(AudienceFact(request, parentEvent.Id, EventResourceAudienceKindEnum.Organizer));

            authority.Events.TryGetValue(parentEvent.Id, out EventAuthorityForUser? eventFacts);
            List<EventResourcePermissionGrant> grants = [];
            foreach (EventRoleAssignment assignment in assignments.Where(value => value.EventId == parentEvent.Id
                         && value.RevokedAtUtc is null && value.IsEffectiveAt(evaluationUtc.UtcDateTime)))
            {
                foreach (string code in rolePermissions.Where(value => value.RoleId == assignment.RoleId
                             && value.Permission.IsActive
                             && eventFacts?.PermissionCodes.Contains(value.Permission.MasterCode) == true)
                         .Select(value => value.Permission.MasterCode))
                {
                    grants.Add(new(code, new(true,
                        new DateTimeOffset(DateTime.SpecifyKind(assignment.StartsAtUtc, DateTimeKind.Utc)),
                        assignment.ExpiresAtUtc is null ? null
                            : new DateTimeOffset(DateTime.SpecifyKind(assignment.ExpiresAtUtc.Value, DateTimeKind.Utc)))));
                }
            }
            foreach (EventResourcePermissionGrant grant in grants.Where(value =>
                         value.Code == PermissionCodes.EventUpdate && value.Authority.IsEffectiveAt(evaluationUtc)))
                audience.Add(AudienceFact(request, parentEvent.Id, EventResourceAudienceKindEnum.EventStaff) with
                { ValidFromUtc = grant.Authority.ValidFromUtc, ExpiresAtUtc = grant.Authority.ExpiresAtUtc });
            common[parentEvent.Id] = audience;
            grantsByEvent[parentEvent.Id] = grants;
        }

        var platformRoles = await resources.GetSubjectPlatformRolesAsync(userId, FactLimit, cancellationToken);
        var tenantGrants = await resources.GetSubjectTenantRoleGrantsAsync(userId, FactLimit, cancellationToken);
        bool canModerate = IsNativeInstanceAdmin(userId, platformRoles)
            || NativeAdminTenantIds(userId, tenantGrants).Contains(request.TenantId);
        EventModerationPrincipalFacts? moderation = moderationEventIds.Count > 0
            ? await ReadModerationPrincipalAsync(request.TenantId, userId, moderationEventIds, assignments,
                rolePermissions, platformRoles, tenantGrants, cancellationToken) : null;
        return new(tenantMember, controlledActors, common, grantsByEvent, participants, tickets,
            eligibility, entitlements, targets, checkIns, canModerate, moderation);
    }

    private static IReadOnlyList<EventResourceAudienceFact> BuildResourceAudience(
        EventResourceAuthorityRequest request,
        EventResource resource,
        SubjectBatchFacts subject,
        IReadOnlyDictionary<Guid, EventSession> sessionsById)
    {
        List<EventResourceAudienceFact> audience = [.. subject.CommonAudience.GetValueOrDefault(resource.EventId) ?? []];
        if (request.SubjectUserId is not { } userId) return audience;

        foreach (AdmissionTicket ticket in subject.Tickets.Where(ticket => ticket.EventId == resource.EventId
                     && ticket.IsActive && ticket.HolderSubjectUserId == userId))
        {
            RegistrationParticipant? participant = subject.Participants.SingleOrDefault(value =>
                value.Id == ticket.ParticipantId && !value.IsDeleted && value.LinkedUserId == userId
                && IsAllowedParticipant(value) && IsOrderCurrent(value.RegistrationOrder)
                && value.RegistrationOrder?.Id == ticket.RegistrationOrderId);
            ParticipantAdmissionEligibility? state = participant is null ? null : subject.Eligibility.SingleOrDefault(value =>
                IsCurrentEligibility(value, ticket, participant, userId));
            if (participant is null || state is null) continue;

            foreach (EventResourceAudienceRule rule in resource.AudienceRules.Where(rule =>
                         rule.AudienceKindId is (int)EventResourceAudienceKindEnum.TicketHolder
                             or (int)EventResourceAudienceKindEnum.CheckedInParticipant
                         && (!rule.EventTicketTypeId.HasValue || rule.EventTicketTypeId == ticket.EventTicketTypeId)
                         && (!rule.EventTicketCatalogVersionId.HasValue
                             || rule.EventTicketCatalogVersionId == ticket.TicketCatalogVersionId)))
            {
                Guid? qualifierSessionId = rule.EventSessionId ?? resource.EventSessionId;
                if (qualifierSessionId is { } sessionId
                    && (!sessionsById.TryGetValue(sessionId, out EventSession? session)
                        || session.EventId != resource.EventId || !IsEligibleSession(session)))
                    continue;
                if (!subject.Entitlements.Any(value => value.TargetEventId == resource.EventId
                        && value.TicketTypeId == ticket.EventTicketTypeId && Covers(value, qualifierSessionId)))
                    continue;

                bool confirmed = participant.RegistrationOrder?.RegistrationOrderStatusId
                    == (int)RegistrationOrderStatusEnum.Confirmed;
                Guid? approved = state.ApprovedAt.HasValue && state.SubjectUserId == userId ? userId : null;
                Guid? completed = state.RequirementsCompletedAt.HasValue && state.SubjectUserId == userId ? userId : null;
                audience.Add(AudienceFact(request, resource.EventId, EventResourceAudienceKindEnum.TicketHolder) with
                {
                    EventSessionId = qualifierSessionId, EventTicketTypeId = ticket.EventTicketTypeId,
                    OrderConfirmed = confirmed, ApprovedSubjectUserId = approved, CompletedSubjectUserId = completed
                });

                if (rule.AudienceKindId != (int)EventResourceAudienceKindEnum.CheckedInParticipant
                    || rule.AdmissionTargetId is not { } targetId) continue;
                AdmissionTarget? target = subject.Targets.SingleOrDefault(value => value.Id == targetId
                    && value.EventId == resource.EventId && value.IsOperational
                    && value.AdmissionTargetTypeId == rule.AdmissionTargetTypeId
                    && value.ScopeId == rule.AdmissionTargetScopeId);
                bool checkedIn = subject.CheckIns.Any(value => value.AdmissionTicketId == ticket.Id
                    && value.AdmissionTargetId == targetId && value.ActiveCheckInEventId.HasValue);
                if (target is null || !checkedIn) continue;
                audience.Add(AudienceFact(request, resource.EventId,
                    EventResourceAudienceKindEnum.CheckedInParticipant) with
                {
                    EventSessionId = qualifierSessionId, EventTicketTypeId = ticket.EventTicketTypeId,
                    AdmissionTargetType = (AdmissionTargetTypeEnum)target.AdmissionTargetTypeId,
                    AdmissionTargetId = target.Id, OrderConfirmed = confirmed,
                    ApprovedSubjectUserId = approved, CompletedSubjectUserId = completed
                });
            }
        }
        return audience;
    }

    private async Task<HashSet<Guid>> ResolveControlledActorsAsync(
        Guid tenantId, Guid userId, TenantUser? tenantUser, IReadOnlyCollection<Actor> actors,
        CancellationToken cancellationToken)
    {
        HashSet<Guid> controlled = actors.Where(actor => !actor.IsDeleted && !actor.IsSuspended
                && actor.UserId == userId && tenantUser is { IsDeleted: false,
                    StatusId: (int)TenantUserStatusEnum.Active } && tenantUser.ActorId == actor.Id)
            .Select(actor => actor.Id).ToHashSet();
        Guid[] organizationIds = actors.Where(actor => !actor.IsDeleted && !actor.IsSuspended)
            .Select(actor => actor.OrganizationId).OfType<Guid>().Distinct().ToArray();
        Guid[] groupIds = actors.Where(actor => !actor.IsDeleted && !actor.IsSuspended)
            .Select(actor => actor.GroupId).OfType<Guid>().Distinct().ToArray();
        IReadOnlyList<OrganizationMember> organizations = await resources.GetOrganizationControlMembershipsAsync(
            tenantId, userId, organizationIds, FactLimit, cancellationToken);
        IReadOnlyList<GroupMember> groups = await resources.GetGroupControlMembershipsAsync(
            tenantId, userId, groupIds, FactLimit, cancellationToken);
        int[] roleIds = organizations.Select(value => value.RoleId).Concat(groups.Select(value => value.RoleId))
            .Distinct().ToArray();
        IReadOnlyList<RolePermission> permissions = await resources.GetRolePermissionsAsync(
            roleIds, FactLimit, cancellationToken);
        HashSet<int> createRoles = permissions.Where(value => value.Permission.IsActive
                && value.Permission.MasterCode == PermissionCodes.EventCreate)
            .Select(value => value.RoleId).ToHashSet();

        foreach (OrganizationMember membership in organizations.Where(value => !value.IsDeleted
                     && createRoles.Contains(value.RoleId)
                     && value.OrganizationTenant is { IsDeleted: false, IsSuspended: false,
                         IsOrganizerEligible: true, ApprovalStatusId: (int)ApprovalStatusEnum.Approved }))
            foreach (Actor actor in actors.Where(value => !value.IsDeleted && !value.IsSuspended
                         && value.OrganizationId == membership.OrganizationTenant.OrganizationId))
                controlled.Add(actor.Id);
        foreach (GroupMember membership in groups.Where(value => !value.IsDeleted
                     && createRoles.Contains(value.RoleId)
                     && value.GroupTenant is { IsDeleted: false, IsSuspended: false,
                         IsOrganizerEligible: true, ApprovalStatusId: (int)ApprovalStatusEnum.Approved }))
            foreach (Actor actor in actors.Where(value => !value.IsDeleted && !value.IsSuspended
                         && value.GroupId == membership.GroupTenant.GroupId))
                controlled.Add(actor.Id);
        return controlled;
    }

    private static bool IsAuthenticatedParentVisible(Event parent, bool tenantMember, bool organizer,
        IReadOnlyCollection<EventResourceAudienceFact> audience)
    {
        if (parent.EventStatusId != (int)EventStatusEnum.Published) return false;
        return (VisibilityTypeEnum)parent.VisibilityTypeId switch
        {
            VisibilityTypeEnum.Unlisted => true,
            VisibilityTypeEnum.MembersOnly => tenantMember,
            VisibilityTypeEnum.Private => organizer || audience.Any(value => value.Kind is not
                EventResourceAudienceKindEnum.AuthenticatedTenantMember),
            _ => false
        };
    }

    private static EventResourceAudienceFact SessionRegistrantFact(
        EventResourceAuthorityRequest request, Guid eventId, Guid sessionId, RegistrationOrder order,
        ParticipantAdmissionEligibility? state, Guid userId) => AudienceFact(
        request, eventId, EventResourceAudienceKindEnum.SessionRegistrant) with
    {
        EventSessionId = sessionId,
        OrderConfirmed = order.RegistrationOrderStatusId == (int)RegistrationOrderStatusEnum.Confirmed,
        ApprovedSubjectUserId = state is { ApprovedAt: not null, RevokedAt: null, SubjectUserId: var approvedSubject }
            && approvedSubject == userId ? userId : null,
        CompletedSubjectUserId = state is { RequirementsCompletedAt: not null, RevokedAt: null, SubjectUserId: var completedSubject }
            && completedSubject == userId ? userId : null
    };

    private static (bool Safe, string Generation) ReadAttachment(
        EventResource resource, IReadOnlyDictionary<Guid, StorageObject> storageById, EventResourceGovernancePolicy? governancePolicy)
    {
        if (resource.StorageObjectId is not { } storageId)
        {
            string protectedPayload = string.Join('|', resource.ConcurrencyStamp,
                resource.ExternalDestinationProtectionVersion,
                resource.ExternalDestinationSafeOrigin ?? string.Empty,
                resource.ExternalDestinationCiphertext ?? string.Empty);
            return (resource.HasPublishablePayload()
                && governancePolicy?.AllowsExternalOrigin(resource.ExternalDestinationSafeOrigin ?? string.Empty) == true,
                Sha256(protectedPayload));
        }
        if (!storageById.TryGetValue(storageId, out StorageObject? storage)
            || storage.IsDeleted || storage.LifecycleState != StorageObjectLifecycleStates.Active
            || storage.OwningResourceId != resource.Id
            || !string.Equals(storage.OwningResourceKind, "event_resource", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(storage.Sha256Checksum) || string.IsNullOrWhiteSpace(storage.ObjectKey))
            return (false, "missing");
        string generation = Sha256(string.Join('|', storage.Id, storage.ConcurrencyStamp,
            storage.Provider, storage.ObjectKey, storage.Sha256Checksum, storage.Size));
        return (false, generation);
    }

    private static EventResourceAudienceFact AudienceFact(
        EventResourceAuthorityRequest request, Guid eventId, EventResourceAudienceKindEnum kind) => new()
    {
        TenantId = request.TenantId, EventId = eventId, SubjectUserId = request.SubjectUserId!.Value,
        Kind = kind, IsCurrent = true
    };

    private static bool Covers(TicketTypeEntitlement entitlement, Guid? sessionId) =>
        (EntitlementScopeTypeEnum)entitlement.EntitlementScopeTypeId switch
        {
            EntitlementScopeTypeEnum.Event => true,
            EntitlementScopeTypeEnum.EventSession => sessionId.HasValue && entitlement.EventSessionId == sessionId,
            _ => false
        };

    private static bool IsEligibleSession(EventSession session) => !session.IsDeleted
        && session.EventSessionStatusId is (int)EventSessionStatusEnum.Published
            or (int)EventSessionStatusEnum.Completed;

    private static bool IsCurrentEligibility(ParticipantAdmissionEligibility state, AdmissionTicket ticket,
        RegistrationParticipant participant, Guid userId) =>
        state.EventId == ticket.EventId && state.ParticipantId == participant.Id
        && state.RegistrationOrderId == ticket.RegistrationOrderId
        && state.RegistrationOrderLineId == ticket.RegistrationOrderLineId
        && state.RegistrationTicketAssignmentId == ticket.RegistrationTicketAssignmentId
        && state.SubjectUserId == userId && state.RevokedAt is null
        && state.RegistrationTicketAssignment is
        {
            AssignmentStatusId: (int)AssignmentStatusEnum.Assigned,
            ParticipantId: var assignedParticipant,
            RegistrationOrderId: var assignedOrder,
            RegistrationOrderLineId: var assignedLine
        }
        && assignedParticipant == participant.Id && assignedOrder == ticket.RegistrationOrderId
        && assignedLine == ticket.RegistrationOrderLineId;

    private static bool IsAllowedParticipant(RegistrationParticipant participant) =>
        participant.ParticipantTypeId is not (int)ParticipantTypeEnum.Dependent
            and not (int)ParticipantTypeEnum.Guest and not (int)ParticipantTypeEnum.Unnamed;

    private static bool IsOrderCurrent(RegistrationOrder? order) => order is { IsDeleted: false }
        && Enum.IsDefined((RegistrationOrderStatusEnum)order.RegistrationOrderStatusId)
        && order.RegistrationOrderStatusId is not (int)RegistrationOrderStatusEnum.Cancelled
            and not (int)RegistrationOrderStatusEnum.Rejected
            and not (int)RegistrationOrderStatusEnum.Expired;

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record SubjectBatchFacts(
        bool TenantMember,
        HashSet<Guid> ControlledActorIds,
        IReadOnlyDictionary<Guid, IReadOnlyList<EventResourceAudienceFact>> CommonAudience,
        IReadOnlyDictionary<Guid, IReadOnlyList<EventResourcePermissionGrant>> PermissionGrants,
        IReadOnlyList<RegistrationParticipant> Participants,
        IReadOnlyList<AdmissionTicket> Tickets,
        IReadOnlyList<ParticipantAdmissionEligibility> Eligibility,
        IReadOnlyList<TicketTypeEntitlement> Entitlements,
        IReadOnlyList<AdmissionTarget> Targets,
        IReadOnlyList<AdmissionCheckInState> CheckIns,
        bool CanModerate,
        EventModerationPrincipalFacts? ModerationPrincipal = null)
    {
        internal static SubjectBatchFacts Empty { get; } = new(false, [],
            new Dictionary<Guid, IReadOnlyList<EventResourceAudienceFact>>(),
            new Dictionary<Guid, IReadOnlyList<EventResourcePermissionGrant>>(), [], [], [], [], [], [], false);
    }
}
