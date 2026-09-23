using Explore.Application.Specifications.EventResources;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventResourceRepository
{
    Task AddAsync(EventResource resource, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task<EventResource?> GetReplayIdentityAsync(Guid tenantId, Guid resourceId, CancellationToken cancellationToken);
    Task<int> CountActiveAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken);
    Task<IReadOnlyList<EventResource>> ListManagementAsync(Guid tenantId, Guid eventId, int skip, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<EventTicketType>> GetAudienceTicketTypesAsync(Guid tenantId, Guid eventId, IReadOnlyCollection<Guid> ticketTypeIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<EventResourceAuditEntry>> GetUnexpiredAuditEntriesAsync(Guid tenantId, Guid resourceId, DateTime cutoffUtc, int limit, CancellationToken cancellationToken);
    void Update(EventResource resource);
    Task AddAuditEntryAsync(EventResourceAuditEntry entry, CancellationToken cancellationToken);
    Task<EventResource?> GetByIdAsync(Guid tenantId, Guid eventId, Guid resourceId, CancellationToken cancellationToken);
    Task<EventResource?> GetAuthorityResourceAsync(Guid tenantId, Guid resourceId, CancellationToken cancellationToken);
    Task<IReadOnlyList<EventResource>> GetAuthorityResourcesAsync(Guid tenantId, IReadOnlyCollection<Guid> resourceIds, CancellationToken cancellationToken);
    Task<EventResource?> GetByIdForUpdateAsync(Guid tenantId, Guid eventId, Guid resourceId, CancellationToken cancellationToken);
    Task<Event?> GetAuthorityEventAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Event>> GetAuthorityEventsAsync(Guid tenantId, IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken);
    Task<Event?> GetPubliclyEligibleEventAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Event>> GetPublishedEligibleEventsAsync(Guid tenantId, IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken);
    Task<StorageObject?> GetStorageObjectAsync(Guid tenantId, Guid storageObjectId, CancellationToken cancellationToken);
    Task<IReadOnlyList<StorageObject>> GetStorageObjectsAsync(Guid tenantId, IReadOnlyCollection<Guid> storageObjectIds, CancellationToken cancellationToken);
    Task<TenantUser?> GetTenantUserAsync(Guid tenantId, Guid subjectUserId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlatformUserRole>> GetSubjectPlatformRolesAsync(Guid subjectUserId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<TenantUserRoleGrant>> GetSubjectTenantRoleGrantsAsync(Guid subjectUserId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrganizationMember>> GetSubjectOrganizationMembershipsAsync(Guid subjectUserId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<GroupMember>> GetSubjectGroupMembershipsAsync(Guid subjectUserId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrganizationMember>> GetSubjectOrganizationAdminMembershipsAsync(Guid subjectUserId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<GroupMember>> GetSubjectGroupAdminMembershipsAsync(Guid subjectUserId, int limit, CancellationToken cancellationToken);
    Task<RolePermission?> GetFirstRolePermissionAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<EventResource>> ListCandidatesAsync(Guid tenantId, int limit, EventResourceQuerySpecification specification, CancellationToken cancellationToken);
    Task<IReadOnlyList<EventSession>> GetSessionsAsync(Guid tenantId, Guid eventId, IReadOnlyCollection<Guid> sessionIds, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<RegistrationParticipant>> GetSubjectParticipantsAsync(Guid tenantId, Guid eventId, Guid subjectUserId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdmissionTicket>> GetSubjectTicketsAsync(Guid tenantId, Guid eventId, Guid subjectUserId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<EventRegistration>> GetSubjectRegistrationsAsync(Guid tenantId, Guid eventId, Guid subjectUserId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<ParticipantAdmissionEligibility>> GetSubjectEligibilityAsync(Guid tenantId, Guid eventId, Guid subjectUserId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<TicketTypeEntitlement>> GetTicketEntitlementsAsync(Guid tenantId, Guid eventId, IReadOnlyCollection<Guid> ticketTypeIds, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdmissionTarget>> GetAdmissionTargetsAsync(Guid tenantId, Guid eventId, IReadOnlyCollection<Guid> targetIds, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdmissionCheckInState>> GetCheckInStatesAsync(Guid tenantId, IReadOnlyCollection<Guid> ticketIds, IReadOnlyCollection<Guid> targetIds, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<EventSessionSpeaker>> GetEventSpeakersAsync(Guid tenantId, Guid eventId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrganizationMember>> GetOrganizationControlMembershipsAsync(Guid tenantId, Guid subjectUserId, IReadOnlyCollection<Guid> organizationIds, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<GroupMember>> GetGroupControlMembershipsAsync(Guid tenantId, Guid subjectUserId, IReadOnlyCollection<Guid> groupIds, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<EventRoleAssignment>> GetEventRoleAssignmentsAsync(Guid tenantId, Guid eventId, Guid subjectUserId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<RolePermission>> GetRolePermissionsAsync(IReadOnlyCollection<int> roleIds, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<EventResourceAuditEntry>> GetAuditEntriesAsync(Guid tenantId, Guid resourceId, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<EventSession>> GetSessionsAsync(Guid tenantId, IReadOnlyCollection<Guid> eventIds, IReadOnlyCollection<Guid> sessionIds, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<RegistrationParticipant>> GetSubjectParticipantsAsync(Guid tenantId, IReadOnlyCollection<Guid> eventIds, Guid subjectUserId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdmissionTicket>> GetSubjectTicketsAsync(Guid tenantId, IReadOnlyCollection<Guid> eventIds, Guid subjectUserId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<EventRegistration>> GetSubjectRegistrationsAsync(Guid tenantId, IReadOnlyCollection<Guid> eventIds, Guid subjectUserId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<ParticipantAdmissionEligibility>> GetSubjectEligibilityAsync(Guid tenantId, IReadOnlyCollection<Guid> eventIds, Guid subjectUserId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<TicketTypeEntitlement>> GetTicketEntitlementsAsync(Guid tenantId, IReadOnlyCollection<Guid> eventIds, IReadOnlyCollection<Guid> ticketTypeIds, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdmissionTarget>> GetAdmissionTargetsAsync(Guid tenantId, IReadOnlyCollection<Guid> eventIds, IReadOnlyCollection<Guid> targetIds, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<EventSessionSpeaker>> GetEventSpeakersAsync(Guid tenantId, IReadOnlyCollection<Guid> eventIds, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<EventRoleAssignment>> GetEventRoleAssignmentsAsync(Guid tenantId, IReadOnlyCollection<Guid> eventIds, Guid subjectUserId, int limit, CancellationToken cancellationToken);
}
