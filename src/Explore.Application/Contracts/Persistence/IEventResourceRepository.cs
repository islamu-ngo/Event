using Explore.Application.Specifications.EventResources;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventResourceRepository
{
    Task AddAsync(EventResource resource, CancellationToken cancellationToken);
    void Update(EventResource resource);
    Task AddAuditEntryAsync(EventResourceAuditEntry entry, CancellationToken cancellationToken);
    Task<EventResource?> GetByIdAsync(Guid tenantId, Guid eventId, Guid resourceId, CancellationToken cancellationToken);
    Task<EventResource?> GetByIdForUpdateAsync(Guid tenantId, Guid eventId, Guid resourceId, CancellationToken cancellationToken);
    Task<IReadOnlyList<EventResource>> ListCandidatesAsync(Guid tenantId, int limit, EventResourceQuerySpecification specification, CancellationToken cancellationToken);
    Task<IReadOnlyList<EventSession>> GetSessionsAsync(Guid tenantId, Guid eventId, IReadOnlyCollection<Guid> sessionIds, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<RegistrationParticipant>> GetSubjectParticipantsAsync(Guid tenantId, Guid eventId, Guid subjectUserId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdmissionTicket>> GetSubjectTicketsAsync(Guid tenantId, Guid eventId, Guid subjectUserId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdmissionTarget>> GetAdmissionTargetsAsync(Guid tenantId, Guid eventId, IReadOnlyCollection<Guid> targetIds, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<EventResourceAuditEntry>> GetAuditEntriesAsync(Guid tenantId, Guid resourceId, int limit, CancellationToken cancellationToken);
}
