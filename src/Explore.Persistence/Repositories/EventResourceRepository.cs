using Explore.Application.Contracts.Persistence;
using Explore.Application.Specifications.EventResources;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class EventResourceRepository : IEventResourceRepository
{
    private const int MaximumCandidateCount = 500;
    private const int MaximumFactCount = 500;
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

    public Task<EventResource?> GetByIdForUpdateAsync(
        Guid tenantId,
        Guid eventId,
        Guid resourceId,
        CancellationToken cancellationToken) =>
        ResourceGraph(_dbContext.EventResources)
            .SingleOrDefaultAsync(resource => resource.TenantId == tenantId
                && resource.EventId == eventId && resource.Id == resourceId, cancellationToken);

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
