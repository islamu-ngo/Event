using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class EventRoleAssignmentRepository : GenericRepository<EventRoleAssignment, Guid>, IEventRoleAssignmentRepository
{
    private readonly ExploreDbContext _dbContext;

    public EventRoleAssignmentRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EventRoleAssignment?> GetByEventUserRoleAsync(
        Guid tenantId,
        Guid eventId,
        Guid userId,
        int roleId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.EventRoleAssignments
            .AsNoTracking()
            .Include(a => a.Role)
            .FirstOrDefaultAsync(a =>
                a.TenantId == tenantId &&
                a.EventId == eventId &&
                a.UserId == userId &&
                a.RoleId == roleId,
                cancellationToken);
    }

    public async Task<EventRoleAssignment?> GetOpenByEventUserRoleAsync(
        Guid tenantId,
        Guid eventId,
        Guid userId,
        int roleId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.EventRoleAssignments
            .Include(a => a.Role)
            .FirstOrDefaultAsync(a =>
                a.TenantId == tenantId &&
                a.EventId == eventId &&
                a.UserId == userId &&
                a.RoleId == roleId &&
                (a.Status == EventRoleAssignmentStatus.Pending || a.Status == EventRoleAssignmentStatus.Active),
                cancellationToken);
    }

    public async Task<IReadOnlyList<EventRoleAssignment>> GetEffectiveForUserAndEventsAsync(
        Guid tenantId,
        Guid userId,
        IReadOnlyCollection<Guid> eventIds,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (eventIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.EventRoleAssignments
            .AsNoTracking()
            .Include(a => a.Role)
            .Where(a =>
                a.TenantId == tenantId &&
                a.UserId == userId &&
                eventIds.Contains(a.EventId) &&
                a.Status == EventRoleAssignmentStatus.Active &&
                a.StartsAtUtc <= utcNow &&
                (a.ExpiresAtUtc == null || a.ExpiresAtUtc > utcNow))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasAnotherEffectiveOwnerAsync(
        Guid tenantId,
        Guid eventId,
        Guid excludedAssignmentId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        return await _dbContext.EventRoleAssignments
            .AsNoTracking()
            .AnyAsync(a =>
                a.Id != excludedAssignmentId &&
                a.TenantId == tenantId &&
                a.EventId == eventId &&
                a.RoleId == (int)RoleEnum.EventOwner &&
                a.Status == EventRoleAssignmentStatus.Active &&
                a.StartsAtUtc <= utcNow &&
                (a.ExpiresAtUtc == null || a.ExpiresAtUtc > utcNow),
                cancellationToken);
    }

    public async Task<IReadOnlyList<EventRoleAssignment>> GetTeamMembersForEventAsync(
        Guid tenantId,
        Guid eventId,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;

        var query = _dbContext.EventRoleAssignments
            .AsNoTracking()
            .Include(a => a.User)
                .ThenInclude(u => u.Pii)
            .Include(a => a.Role)
            .Where(a => a.TenantId == tenantId && a.EventId == eventId);

        if (!includeInactive)
        {
            query = query.Where(a =>
                a.Status == EventRoleAssignmentStatus.Active &&
                a.StartsAtUtc <= utcNow &&
                (a.ExpiresAtUtc == null || a.ExpiresAtUtc > utcNow));
        }

        return await query
            .OrderBy(a => a.RoleId)
            .ThenBy(a => a.User.Pii.FirstName)
            .ThenBy(a => a.User.Pii.LastName)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EventRoleAssignment>> GetEffectiveOwnersForEventAsync(
        Guid tenantId,
        Guid eventId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Owner resolution timestamp must be UTC.", nameof(utcNow));
        }

        return await _dbContext.EventRoleAssignments
            .AsNoTracking()
            .Include(assignment => assignment.User)
                .ThenInclude(user => user.Pii)
            .Where(assignment =>
                assignment.TenantId == tenantId
                && assignment.EventId == eventId
                && assignment.RoleId == (int)RoleEnum.EventOwner
                && assignment.Status == EventRoleAssignmentStatus.Active
                && assignment.StartsAtUtc <= utcNow
                && (assignment.ExpiresAtUtc == null || assignment.ExpiresAtUtc > utcNow))
            .OrderBy(assignment => assignment.UserId)
            .ToListAsync(cancellationToken);
    }
}
