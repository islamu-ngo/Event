using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Specifications.EventResources;
using Explore.Domain;
using Explore.Persistence.Extensions;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class EventResourceRepository : IEventResourceRepository
{
    private const int MaximumCandidateCount = 500;
    private const int MaximumFactCount = EventResourceAuthorityRequest.MaximumBatchChecks;
    private const int MaximumAuditCount = 200;
    private readonly ExploreDbContext _dbContext;

    public EventResourceRepository(ExploreDbContext dbContext) => _dbContext = dbContext;

    public void Update(EventResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        _dbContext.Entry(resource).State = EntityState.Modified;
    }

    public Task AddAsync(EventResource resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return _dbContext.EventResources.AddAsync(resource, cancellationToken).AsTask();
    }

    public Task AddAuditEntryAsync(EventResourceAuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return _dbContext.EventResourceAuditEntries.AddAsync(entry, cancellationToken).AsTask();
    }

    public Task<EventResource?> GetByIdAsync(
        Guid tenantId,
        Guid eventId,
        Guid resourceId,
        CancellationToken cancellationToken) =>
        ResourceGraph(_dbContext.EventResources.AsNoTrackingWithIdentityResolution())
            .SingleOrDefaultAsync(resource => resource.TenantId == tenantId
                && resource.EventId == eventId && resource.Id == resourceId, cancellationToken);

    public Task<EventResource?> GetAuthorityResourceAsync(
        Guid tenantId,
        Guid resourceId,
        CancellationToken cancellationToken) =>
        ResourceGraph(_dbContext.EventResources.AsNoTrackingWithIdentityResolution())
            .SingleOrDefaultAsync(resource => resource.TenantId == tenantId
                && resource.Id == resourceId, cancellationToken);

    public async Task<IReadOnlyList<EventResource>> GetAuthorityResourcesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> resourceIds,
        CancellationToken cancellationToken)
    {
        Guid[] ids = NormalizeIds(resourceIds, EventResourceAuthorityRequest.MaximumBatchResources);
        return ids.Length == 0 ? [] : await ResourceGraph(
                _dbContext.EventResources.AsNoTrackingWithIdentityResolution()
                    .Where(resource => resource.TenantId == tenantId && ids.Contains(resource.Id)))
            .ToListAsync(cancellationToken);
    }

    public Task<EventResource?> GetByIdForUpdateAsync(
        Guid tenantId,
        Guid eventId,
        Guid resourceId,
        CancellationToken cancellationToken) =>
        ResourceGraph(_dbContext.EventResources)
            .SingleOrDefaultAsync(resource => resource.TenantId == tenantId
                && resource.EventId == eventId && resource.Id == resourceId, cancellationToken);

    public Task<Event?> GetAuthorityEventAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken) =>
        _dbContext.Events.AsNoTracking()
            .Include(@event => @event.Actor)
            .Include(@event => @event.EventProvenanceType)
            .Include(@event => @event.OrganizerActor)
            .SingleOrDefaultAsync(@event => @event.TenantId == tenantId && @event.Id == eventId,
                cancellationToken);

    public async Task<IReadOnlyList<Event>> GetAuthorityEventsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> eventIds,
        CancellationToken cancellationToken)
    {
        Guid[] ids = NormalizeIds(eventIds, EventResourceAuthorityRequest.MaximumBatchResources);
        return ids.Length == 0 ? [] : await _dbContext.Events.AsNoTracking()
            .Include(@event => @event.Actor)
            .Include(@event => @event.EventProvenanceType)
            .Include(@event => @event.OrganizerActor)
            .Where(@event => @event.TenantId == tenantId && ids.Contains(@event.Id))
            .ToListAsync(cancellationToken);
    }

    public Task<Event?> GetPubliclyEligibleEventAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken) =>
        _dbContext.Events.AsNoTracking().WherePubliclyEligible(_dbContext)
            .SingleOrDefaultAsync(@event => @event.TenantId == tenantId && @event.Id == eventId,
                cancellationToken);

    public async Task<IReadOnlyList<Event>> GetPublishedEligibleEventsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> eventIds,
        CancellationToken cancellationToken)
    {
        Guid[] ids = NormalizeIds(eventIds, EventResourceAuthorityRequest.MaximumBatchResources);
        return ids.Length == 0 ? [] : await _dbContext.Events.AsNoTracking()
            .WherePublishedWithEligibleSource(_dbContext)
            .Where(@event => @event.TenantId == tenantId && ids.Contains(@event.Id))
            .ToListAsync(cancellationToken);
    }

    public Task<StorageObject?> GetStorageObjectAsync(
        Guid tenantId,
        Guid storageObjectId,
        CancellationToken cancellationToken) =>
        _dbContext.StorageObjects.AsNoTracking()
            .SingleOrDefaultAsync(storage => storage.TenantId == tenantId
                && storage.Id == storageObjectId, cancellationToken);

    public async Task<IReadOnlyList<StorageObject>> GetStorageObjectsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> storageObjectIds,
        CancellationToken cancellationToken)
    {
        Guid[] ids = NormalizeIds(storageObjectIds, EventResourceAuthorityRequest.MaximumBatchResources);
        return ids.Length == 0 ? [] : await _dbContext.StorageObjects.AsNoTracking()
            .Where(storage => storage.TenantId == tenantId && ids.Contains(storage.Id))
            .ToListAsync(cancellationToken);
    }

    public Task<TenantUser?> GetTenantUserAsync(
        Guid tenantId,
        Guid subjectUserId,
        CancellationToken cancellationToken) =>
        _dbContext.TenantUsers.AsNoTracking()
            .SingleOrDefaultAsync(user => user.TenantId == tenantId
                && user.UserId == subjectUserId, cancellationToken);

    public async Task<IReadOnlyList<EventResource>> ListCandidatesAsync(
        Guid tenantId,
        int limit,
        EventResourceQuerySpecification specification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(specification);
        int boundedLimit = RequireLimit(limit, MaximumCandidateCount);
        // Candidate cursors encode only ascending SortOrder and Id.
        if (specification.HasSort
            && (specification.SortDescending || !ReferenceEquals(specification.Sort, EventResourceSort.SortOrder)))
        {
            throw new ArgumentException("Resource candidates require ascending SortOrder ordering.", nameof(specification));
        }

        IQueryable<EventResource> query = specification.Apply(
            _dbContext.EventResources.AsNoTracking().Where(resource => resource.TenantId == tenantId));
        query = query.OrderBy(resource => resource.SortOrder).ThenBy(resource => resource.Id);
        return await ResourceGraph(query.AsNoTrackingWithIdentityResolution())
            .Take(boundedLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EventSession>> GetSessionsAsync(
        Guid tenantId,
        Guid eventId,
        IReadOnlyCollection<Guid> sessionIds,
        int limit,
        CancellationToken cancellationToken)
    {
        Guid[] ids = NormalizeIds(sessionIds, limit);
        return ids.Length == 0 ? [] : await _dbContext.EventSessions.AsNoTracking()
            .Where(session => session.TenantId == tenantId && session.EventId == eventId && ids.Contains(session.Id))
            .Take(ids.Length)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RegistrationParticipant>> GetSubjectParticipantsAsync(
        Guid tenantId,
        Guid eventId,
        Guid subjectUserId,
        int limit,
        CancellationToken cancellationToken)
    {
        int boundedLimit = RequireLimit(limit, MaximumFactCount);
        List<RegistrationParticipant> participants = await _dbContext.RegistrationParticipants.AsNoTracking()
            .Include(participant => participant.RegistrationOrder)
            .Where(participant => participant.TenantId == tenantId
                && participant.LinkedUserId == subjectUserId
                && participant.RegistrationOrder != null
                && participant.RegistrationOrder.EventId == eventId)
            .OrderBy(participant => participant.Id)
            .Take(boundedLimit + 1)
            .ToListAsync(cancellationToken);
        return RequireCompleteFacts(participants, boundedLimit, nameof(RegistrationParticipant));
    }

    public async Task<IReadOnlyList<AdmissionTicket>> GetSubjectTicketsAsync(
        Guid tenantId,
        Guid eventId,
        Guid subjectUserId,
        int limit,
        CancellationToken cancellationToken)
    {
        int boundedLimit = RequireLimit(limit, MaximumFactCount);
        List<AdmissionTicket> tickets = await _dbContext.AdmissionTickets.AsNoTracking()
            .Where(ticket => ticket.TenantId == tenantId && ticket.EventId == eventId
                && ticket.HolderSubjectUserId == subjectUserId)
            .OrderBy(ticket => ticket.Id)
            .Take(boundedLimit + 1)
            .ToListAsync(cancellationToken);
        return RequireCompleteFacts(tickets, boundedLimit, nameof(AdmissionTicket));
    }

    public async Task<IReadOnlyList<EventRegistration>> GetSubjectRegistrationsAsync(
        Guid tenantId,
        Guid eventId,
        Guid subjectUserId,
        int limit,
        CancellationToken cancellationToken)
    {
        int boundedLimit = RequireLimit(limit, MaximumFactCount);
        List<EventRegistration> registrations = await _dbContext.EventRegistrations.AsNoTracking()
            .Where(registration => registration.TenantId == tenantId
                && registration.EventId == eventId
                && registration.LinkedUserId == subjectUserId)
            .OrderBy(registration => registration.Id)
            .Take(boundedLimit + 1)
            .ToListAsync(cancellationToken);
        return RequireCompleteFacts(registrations, boundedLimit, nameof(EventRegistration));
    }

    public async Task<IReadOnlyList<ParticipantAdmissionEligibility>> GetSubjectEligibilityAsync(
        Guid tenantId,
        Guid eventId,
        Guid subjectUserId,
        int limit,
        CancellationToken cancellationToken)
    {
        int boundedLimit = RequireLimit(limit, MaximumFactCount);
        List<ParticipantAdmissionEligibility> eligibility = await _dbContext.ParticipantAdmissionEligibilities.AsNoTracking()
            .Include(value => value.RegistrationTicketAssignment)
            .Where(value => value.TenantId == tenantId && value.EventId == eventId
                && value.Participant != null && !value.Participant.IsDeleted
                && value.Participant.LinkedUserId == subjectUserId)
            .OrderBy(value => value.Id)
            .Take(boundedLimit + 1)
            .ToListAsync(cancellationToken);
        return RequireCompleteFacts(eligibility, boundedLimit, nameof(ParticipantAdmissionEligibility));
    }

    public async Task<IReadOnlyList<TicketTypeEntitlement>> GetTicketEntitlementsAsync(
        Guid tenantId,
        Guid eventId,
        IReadOnlyCollection<Guid> ticketTypeIds,
        int limit,
        CancellationToken cancellationToken)
    {
        Guid[] ids = NormalizeIds(ticketTypeIds, limit);
        if (ids.Length == 0) return [];
        List<TicketTypeEntitlement> entitlements = await _dbContext.TicketTypeEntitlements.AsNoTracking()
            .Where(value => value.TenantId == tenantId && value.TargetEventId == eventId
                && ids.Contains(value.TicketTypeId))
            .OrderBy(value => value.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        return RequireCompleteFacts(entitlements, limit, nameof(TicketTypeEntitlement));
    }

    public async Task<IReadOnlyList<AdmissionTarget>> GetAdmissionTargetsAsync(
        Guid tenantId,
        Guid eventId,
        IReadOnlyCollection<Guid> targetIds,
        int limit,
        CancellationToken cancellationToken)
    {
        Guid[] ids = NormalizeIds(targetIds, limit);
        return ids.Length == 0 ? [] : await _dbContext.AdmissionTargets.AsNoTracking()
            .Where(target => target.TenantId == tenantId && target.EventId == eventId && ids.Contains(target.Id))
            .Take(ids.Length)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdmissionCheckInState>> GetCheckInStatesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> ticketIds,
        IReadOnlyCollection<Guid> targetIds,
        int limit,
        CancellationToken cancellationToken)
    {
        Guid[] tickets = NormalizeIds(ticketIds, limit);
        Guid[] targets = NormalizeIds(targetIds, limit);
        if (tickets.Length == 0 || targets.Length == 0) return [];
        List<AdmissionCheckInState> states = await _dbContext.AdmissionCheckInStates.AsNoTracking()
            .Where(state => state.TenantId == tenantId && tickets.Contains(state.AdmissionTicketId)
                && targets.Contains(state.AdmissionTargetId))
            .OrderBy(state => state.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        return RequireCompleteFacts(states, limit, nameof(AdmissionCheckInState));
    }

    public async Task<IReadOnlyList<EventSessionSpeaker>> GetEventSpeakersAsync(
        Guid tenantId,
        Guid eventId,
        int limit,
        CancellationToken cancellationToken)
    {
        int boundedLimit = RequireLimit(limit, MaximumFactCount);
        List<EventSessionSpeaker> speakers = await _dbContext.EventSessionSpeakers.AsNoTracking()
            .Include(speaker => speaker.Actor)
            .Include(speaker => speaker.EventSession)
            .Where(speaker => speaker.TenantId == tenantId
                && speaker.EventSession.EventId == eventId && !speaker.EventSession.IsDeleted)
            .OrderBy(speaker => speaker.Id)
            .Take(boundedLimit + 1)
            .ToListAsync(cancellationToken);
        return RequireCompleteFacts(speakers, boundedLimit, nameof(EventSessionSpeaker));
    }

    public async Task<IReadOnlyList<OrganizationMember>> GetOrganizationControlMembershipsAsync(
        Guid tenantId,
        Guid subjectUserId,
        IReadOnlyCollection<Guid> organizationIds,
        int limit,
        CancellationToken cancellationToken)
    {
        Guid[] ids = NormalizeIds(organizationIds, limit);
        if (ids.Length == 0) return [];
        List<OrganizationMember> memberships = await _dbContext.OrganizationMembers.AsNoTracking()
            .Include(member => member.OrganizationTenant)
            .Where(member => member.TenantId == tenantId && member.UserId == subjectUserId
                && ids.Contains(member.OrganizationTenant.OrganizationId))
            .OrderBy(member => member.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        return RequireCompleteFacts(memberships, limit, nameof(OrganizationMember));
    }

    public async Task<IReadOnlyList<GroupMember>> GetGroupControlMembershipsAsync(
        Guid tenantId,
        Guid subjectUserId,
        IReadOnlyCollection<Guid> groupIds,
        int limit,
        CancellationToken cancellationToken)
    {
        Guid[] ids = NormalizeIds(groupIds, limit);
        if (ids.Length == 0) return [];
        List<GroupMember> memberships = await _dbContext.GroupMembers.AsNoTracking()
            .Include(member => member.GroupTenant)
            .Where(member => member.TenantId == tenantId && member.UserId == subjectUserId
                && ids.Contains(member.GroupTenant.GroupId))
            .OrderBy(member => member.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        return RequireCompleteFacts(memberships, limit, nameof(GroupMember));
    }

    public async Task<IReadOnlyList<EventRoleAssignment>> GetEventRoleAssignmentsAsync(
        Guid tenantId,
        Guid eventId,
        Guid subjectUserId,
        int limit,
        CancellationToken cancellationToken)
    {
        int boundedLimit = RequireLimit(limit, MaximumFactCount);
        List<EventRoleAssignment> assignments = await _dbContext.EventRoleAssignments.AsNoTracking()
            .Where(assignment => assignment.TenantId == tenantId && assignment.EventId == eventId
                && assignment.UserId == subjectUserId)
            .OrderBy(assignment => assignment.Id)
            .Take(boundedLimit + 1)
            .ToListAsync(cancellationToken);
        return RequireCompleteFacts(assignments, boundedLimit, nameof(EventRoleAssignment));
    }

    public async Task<IReadOnlyList<RolePermission>> GetRolePermissionsAsync(
        IReadOnlyCollection<int> roleIds,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roleIds);
        int boundedLimit = RequireLimit(limit, MaximumFactCount);
        int[] ids = roleIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length > boundedLimit)
            throw new ArgumentOutOfRangeException(nameof(roleIds), ids.Length,
                $"The authority request exceeds its {boundedLimit}-identity budget.");
        if (ids.Length == 0) return [];
        List<RolePermission> permissions = await _dbContext.RolePermissions.AsNoTracking()
            .Include(value => value.Permission)
            .Where(value => ids.Contains(value.RoleId))
            .OrderBy(value => value.RoleId).ThenBy(value => value.PermissionId)
            .Take(boundedLimit + 1)
            .ToListAsync(cancellationToken);
        return RequireCompleteFacts(permissions, boundedLimit, nameof(RolePermission));
    }

    public async Task<IReadOnlyList<EventResourceAuditEntry>> GetAuditEntriesAsync(
        Guid tenantId,
        Guid resourceId,
        int limit,
        CancellationToken cancellationToken)
    {
        int boundedLimit = RequireLimit(limit, MaximumAuditCount);
        return await _dbContext.EventResourceAuditEntries.AsNoTracking()
            .Where(entry => entry.TenantId == tenantId && entry.EventResourceId == resourceId)
            .OrderByDescending(entry => entry.Timestamp)
            .ThenByDescending(entry => entry.Id)
            .Take(boundedLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EventSession>> GetSessionsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> eventIds, IReadOnlyCollection<Guid> sessionIds,
        int limit, CancellationToken cancellationToken)
    {
        Guid[] events = NormalizeIds(eventIds, EventResourceAuthorityRequest.MaximumBatchResources);
        Guid[] sessions = NormalizeIds(sessionIds, limit);
        return events.Length == 0 || sessions.Length == 0 ? [] : await _dbContext.EventSessions.AsNoTracking()
            .Where(value => value.TenantId == tenantId && events.Contains(value.EventId) && sessions.Contains(value.Id))
            .Take(sessions.Length).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RegistrationParticipant>> GetSubjectParticipantsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> eventIds, Guid subjectUserId, int limit,
        CancellationToken cancellationToken)
    {
        Guid[] events = NormalizeIds(eventIds, EventResourceAuthorityRequest.MaximumBatchResources);
        int boundedLimit = RequireLimit(limit, MaximumFactCount);
        if (events.Length == 0) return [];
        List<RegistrationParticipant> values = await _dbContext.RegistrationParticipants.AsNoTracking()
            .Include(value => value.RegistrationOrder)
            .Where(value => value.TenantId == tenantId && value.LinkedUserId == subjectUserId
                && value.RegistrationOrder != null && events.Contains(value.RegistrationOrder.EventId))
            .OrderBy(value => value.Id).Take(boundedLimit + 1).ToListAsync(cancellationToken);
        return RequireCompleteFacts(values, boundedLimit, nameof(RegistrationParticipant));
    }

    public async Task<IReadOnlyList<AdmissionTicket>> GetSubjectTicketsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> eventIds, Guid subjectUserId, int limit,
        CancellationToken cancellationToken)
    {
        Guid[] events = NormalizeIds(eventIds, EventResourceAuthorityRequest.MaximumBatchResources);
        int boundedLimit = RequireLimit(limit, MaximumFactCount);
        if (events.Length == 0) return [];
        List<AdmissionTicket> values = await _dbContext.AdmissionTickets.AsNoTracking()
            .Where(value => value.TenantId == tenantId && events.Contains(value.EventId)
                && value.HolderSubjectUserId == subjectUserId)
            .OrderBy(value => value.Id).Take(boundedLimit + 1).ToListAsync(cancellationToken);
        return RequireCompleteFacts(values, boundedLimit, nameof(AdmissionTicket));
    }

    public async Task<IReadOnlyList<EventRegistration>> GetSubjectRegistrationsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> eventIds, Guid subjectUserId, int limit,
        CancellationToken cancellationToken)
    {
        Guid[] events = NormalizeIds(eventIds, EventResourceAuthorityRequest.MaximumBatchResources);
        int boundedLimit = RequireLimit(limit, MaximumFactCount);
        if (events.Length == 0) return [];
        List<EventRegistration> values = await _dbContext.EventRegistrations.AsNoTracking()
            .Where(value => value.TenantId == tenantId && events.Contains(value.EventId)
                && value.LinkedUserId == subjectUserId)
            .OrderBy(value => value.Id).Take(boundedLimit + 1).ToListAsync(cancellationToken);
        return RequireCompleteFacts(values, boundedLimit, nameof(EventRegistration));
    }

    public async Task<IReadOnlyList<ParticipantAdmissionEligibility>> GetSubjectEligibilityAsync(
        Guid tenantId, IReadOnlyCollection<Guid> eventIds, Guid subjectUserId, int limit,
        CancellationToken cancellationToken)
    {
        Guid[] events = NormalizeIds(eventIds, EventResourceAuthorityRequest.MaximumBatchResources);
        int boundedLimit = RequireLimit(limit, MaximumFactCount);
        if (events.Length == 0) return [];
        List<ParticipantAdmissionEligibility> values = await _dbContext.ParticipantAdmissionEligibilities.AsNoTracking()
            .Include(value => value.RegistrationTicketAssignment)
            .Where(value => value.TenantId == tenantId && events.Contains(value.EventId)
                && value.SubjectUserId == subjectUserId && value.Participant != null
                && !value.Participant.IsDeleted && value.Participant.LinkedUserId == subjectUserId)
            .OrderBy(value => value.Id).Take(boundedLimit + 1).ToListAsync(cancellationToken);
        return RequireCompleteFacts(values, boundedLimit, nameof(ParticipantAdmissionEligibility));
    }

    public async Task<IReadOnlyList<TicketTypeEntitlement>> GetTicketEntitlementsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> eventIds, IReadOnlyCollection<Guid> ticketTypeIds,
        int limit, CancellationToken cancellationToken)
    {
        Guid[] events = NormalizeIds(eventIds, EventResourceAuthorityRequest.MaximumBatchResources);
        Guid[] ticketTypes = NormalizeIds(ticketTypeIds, limit);
        int boundedLimit = RequireLimit(limit, MaximumFactCount);
        if (events.Length == 0 || ticketTypes.Length == 0) return [];
        List<TicketTypeEntitlement> values = await _dbContext.TicketTypeEntitlements.AsNoTracking()
            .Where(value => value.TenantId == tenantId && events.Contains(value.TargetEventId)
                && ticketTypes.Contains(value.TicketTypeId))
            .OrderBy(value => value.Id).Take(boundedLimit + 1).ToListAsync(cancellationToken);
        return RequireCompleteFacts(values, boundedLimit, nameof(TicketTypeEntitlement));
    }

    public async Task<IReadOnlyList<AdmissionTarget>> GetAdmissionTargetsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> eventIds, IReadOnlyCollection<Guid> targetIds,
        int limit, CancellationToken cancellationToken)
    {
        Guid[] events = NormalizeIds(eventIds, EventResourceAuthorityRequest.MaximumBatchResources);
        Guid[] targets = NormalizeIds(targetIds, limit);
        return events.Length == 0 || targets.Length == 0 ? [] : await _dbContext.AdmissionTargets.AsNoTracking()
            .Where(value => value.TenantId == tenantId && events.Contains(value.EventId) && targets.Contains(value.Id))
            .Take(targets.Length).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EventSessionSpeaker>> GetEventSpeakersAsync(
        Guid tenantId, IReadOnlyCollection<Guid> eventIds, int limit, CancellationToken cancellationToken)
    {
        Guid[] events = NormalizeIds(eventIds, EventResourceAuthorityRequest.MaximumBatchResources);
        int boundedLimit = RequireLimit(limit, MaximumFactCount);
        if (events.Length == 0) return [];
        List<EventSessionSpeaker> values = await _dbContext.EventSessionSpeakers.AsNoTracking()
            .Include(value => value.Actor).Include(value => value.EventSession)
            .Where(value => value.TenantId == tenantId && events.Contains(value.EventSession.EventId)
                && !value.EventSession.IsDeleted)
            .OrderBy(value => value.Id).Take(boundedLimit + 1).ToListAsync(cancellationToken);
        return RequireCompleteFacts(values, boundedLimit, nameof(EventSessionSpeaker));
    }

    public async Task<IReadOnlyList<EventRoleAssignment>> GetEventRoleAssignmentsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> eventIds, Guid subjectUserId, int limit,
        CancellationToken cancellationToken)
    {
        Guid[] events = NormalizeIds(eventIds, EventResourceAuthorityRequest.MaximumBatchResources);
        int boundedLimit = RequireLimit(limit, MaximumFactCount);
        if (events.Length == 0) return [];
        List<EventRoleAssignment> values = await _dbContext.EventRoleAssignments.AsNoTracking()
            .Include(value => value.Role)
            .Where(value => value.TenantId == tenantId && events.Contains(value.EventId)
                && value.UserId == subjectUserId)
            .OrderBy(value => value.Id).Take(boundedLimit + 1).ToListAsync(cancellationToken);
        return RequireCompleteFacts(values, boundedLimit, nameof(EventRoleAssignment));
    }

    public async Task<IReadOnlyList<PlatformUserRole>> GetSubjectPlatformRolesAsync(
        Guid subjectUserId, int limit, CancellationToken cancellationToken)
    {
        int bound = RequireLimit(limit, MaximumFactCount);
        var values = await _dbContext.PlatformUserRoles.AsNoTracking().Include(value => value.Role)
            .Where(value => value.UserId == subjectUserId)
            .OrderBy(value => value.Id).Take(bound + 1).ToListAsync(cancellationToken);
        return RequireCompleteFacts(values, bound, nameof(PlatformUserRole));
    }

    public async Task<IReadOnlyList<TenantUserRoleGrant>> GetSubjectTenantRoleGrantsAsync(
        Guid subjectUserId, int limit, CancellationToken cancellationToken)
    {
        int bound = RequireLimit(limit, MaximumFactCount);
        // Match native principal enumeration across tenants, retaining the soft-delete filter.
        var values = await _dbContext.TenantUserRoleGrants
            .IgnoreTenantFilter(TenantFilterBypassReasons.UserTenantMembershipEnumeration)
            .AsNoTracking().Include(value => value.TenantUser).Include(value => value.Role)
            .Where(value => value.TenantUser.UserId == subjectUserId)
            .OrderBy(value => value.Id).Take(bound + 1).ToListAsync(cancellationToken);
        return RequireCompleteFacts(values, bound, nameof(TenantUserRoleGrant));
    }

    public async Task<IReadOnlyList<OrganizationMember>> GetSubjectOrganizationMembershipsAsync(
        Guid subjectUserId, int limit, CancellationToken cancellationToken)
    {
        int bound = RequireLimit(limit, MaximumFactCount);
        // Native organization/group membership helpers use the ambient tenant and soft-delete filters.
        var values = await _dbContext.OrganizationMembers.AsNoTracking().Include(value => value.OrganizationTenant)
            .Where(value => value.UserId == subjectUserId)
            .OrderBy(value => value.Id).Take(bound + 1).ToListAsync(cancellationToken);
        return RequireCompleteFacts(values, bound, nameof(OrganizationMember));
    }

    public async Task<IReadOnlyList<GroupMember>> GetSubjectGroupMembershipsAsync(
        Guid subjectUserId, int limit, CancellationToken cancellationToken)
    {
        int bound = RequireLimit(limit, MaximumFactCount);
        var values = await _dbContext.GroupMembers.AsNoTracking().Include(value => value.GroupTenant)
            .Where(value => value.UserId == subjectUserId)
            .OrderBy(value => value.Id).Take(bound + 1).ToListAsync(cancellationToken);
        return RequireCompleteFacts(values, bound, nameof(GroupMember));
    }

    public async Task<IReadOnlyList<OrganizationMember>> GetSubjectOrganizationAdminMembershipsAsync(
        Guid subjectUserId, int limit, CancellationToken cancellationToken)
    {
        int bound = RequireLimit(limit, MaximumFactCount);
        var values = await _dbContext.OrganizationMembers.AsNoTracking()
            .Include(value => value.OrganizationTenant).ThenInclude(value => value.ApprovalStatus)
            .Include(value => value.OrganizationTenant).ThenInclude(value => value.Organization).ThenInclude(value => value.Actor)
            .Include(value => value.Role)
            .Where(value => value.UserId == subjectUserId)
            .OrderBy(value => value.Id).Take(bound + 1).ToListAsync(cancellationToken);
        return RequireCompleteFacts(values, bound, nameof(OrganizationMember));
    }

    public async Task<IReadOnlyList<GroupMember>> GetSubjectGroupAdminMembershipsAsync(
        Guid subjectUserId, int limit, CancellationToken cancellationToken)
    {
        int bound = RequireLimit(limit, MaximumFactCount);
        var values = await _dbContext.GroupMembers.AsNoTracking()
            .Include(value => value.GroupTenant).ThenInclude(value => value.ApprovalStatus)
            .Include(value => value.GroupTenant).ThenInclude(value => value.Group).ThenInclude(value => value.Actor)
            .Include(value => value.Role)
            .Where(value => value.UserId == subjectUserId)
            .OrderBy(value => value.Id).Take(bound + 1).ToListAsync(cancellationToken);
        return RequireCompleteFacts(values, bound, nameof(GroupMember));
    }

    public Task<RolePermission?> GetFirstRolePermissionAsync(CancellationToken cancellationToken) =>
        _dbContext.RolePermissions.AsNoTracking().OrderBy(value => value.RoleId).ThenBy(value => value.PermissionId)
            .FirstOrDefaultAsync(cancellationToken);

    private static IQueryable<EventResource> ResourceGraph(IQueryable<EventResource> query) =>
        query.Include(resource => resource.AudienceRules).AsSplitQuery();

    private static int RequireLimit(int requested, int maximum)
    {
        if (requested <= 0 || requested > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(requested), requested,
                $"The requested authority fact limit must be between 1 and {maximum}.");
        }
        return requested;
    }

    private static Guid[] NormalizeIds(IReadOnlyCollection<Guid> ids, int limit)
    {
        ArgumentNullException.ThrowIfNull(ids);
        int boundedLimit = RequireLimit(limit, MaximumFactCount);
        Guid[] normalized = ids.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (normalized.Length > boundedLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(ids), normalized.Length,
                $"The authority request exceeds its {boundedLimit}-identity budget.");
        }
        return normalized;
    }

    private static IReadOnlyList<T> RequireCompleteFacts<T>(List<T> facts, int limit, string factName)
    {
        if (facts.Count > limit)
        {
            throw new InvalidOperationException(
                $"The {factName} authority snapshot exceeds its {limit}-row budget.");
        }
        return facts;
    }
}
