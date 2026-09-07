using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class EventSessionGroupSessionRepository : GenericRepository<EventSessionGroupSession, Guid>, IEventSessionGroupSessionRepository
{
    private readonly ExploreDbContext _dbContext;

    public EventSessionGroupSessionRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<EventSessionGroupSession>> GetByGroupAsync(Guid eventSessionGroupId, CancellationToken cancellationToken)
    {
        return await _dbContext.EventSessionGroupSessions
            .AsNoTracking()
            .Include(assignment => assignment.EventSession)
                .ThenInclude(session => session.Event)
            .Include(assignment => assignment.EventSession)
                .ThenInclude(session => session.Location)
            .Include(assignment => assignment.EventSession)
                .ThenInclude(session => session.Room)
            .Include(assignment => assignment.EventSession)
                .ThenInclude(session => session.RegistrationMode)
            .Include(assignment => assignment.EventSession)
                .ThenInclude(session => session.FeaturedImage)
            .Include(assignment => assignment.EventSession)
                .ThenInclude(session => session.IslamicAspect)
            .Include(assignment => assignment.EventSessionGroup)
            .Where(assignment => assignment.EventSessionGroupId == eventSessionGroupId)
            .OrderBy(assignment => assignment.SortOrder)
            .ThenBy(assignment => assignment.EventSessionId)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<EventSessionGroupSession>> GetBySessionAsync(Guid eventSessionId, CancellationToken cancellationToken)
    {
        return await _dbContext.EventSessionGroupSessions
            .AsNoTracking()
            .Include(assignment => assignment.EventSessionGroup)
            .Where(assignment => assignment.EventSessionId == eventSessionId)
            .OrderByDescending(assignment => assignment.IsPrimary)
            .ThenBy(assignment => assignment.SortOrder)
            .ThenBy(assignment => assignment.EventSessionGroupId)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<EventSessionGroupSession>> GetPublicByGroupAsync(
        Guid eventSessionGroupId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.EventSessionGroupSessions
            .AsNoTracking()
            .Include(assignment => assignment.EventSession)
                .ThenInclude(session => session.Event)
            .Include(assignment => assignment.EventSession)
                .ThenInclude(session => session.Location)
            .Include(assignment => assignment.EventSession)
                .ThenInclude(session => session.Room)
            .Include(assignment => assignment.EventSession)
                .ThenInclude(session => session.RegistrationMode)
            .Include(assignment => assignment.EventSession)
                .ThenInclude(session => session.FeaturedImage)
            .Include(assignment => assignment.EventSession)
                .ThenInclude(session => session.IslamicAspect)
            .Include(assignment => assignment.EventSessionGroup)
                .ThenInclude(group => group.Event)
            .WherePubliclyEligible(_dbContext)
            .Where(assignment => assignment.EventSessionGroupId == eventSessionGroupId)
            .OrderBy(assignment => assignment.SortOrder)
            .ThenBy(assignment => assignment.EventSessionId)
            .ToListAsync(cancellationToken);
    }

    public async Task<EventSessionGroupSession?> GetExistingAssignmentAsync(
        Guid eventSessionGroupId,
        Guid eventSessionId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.EventSessionGroupSessions
            .Include(assignment => assignment.EventSessionGroup)
            .Include(assignment => assignment.EventSession)
            .FirstOrDefaultAsync(
                assignment => assignment.EventSessionGroupId == eventSessionGroupId
                    && assignment.EventSessionId == eventSessionId,
                cancellationToken);
    }

    public async Task<List<EventSessionGroupSession>> GetPrimaryAssignmentsForSessionAsync(
        Guid eventSessionId,
        Guid? excludeAssignmentId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.EventSessionGroupSessions
            .Where(assignment => assignment.EventSessionId == eventSessionId
                && assignment.IsPrimary
                && (!excludeAssignmentId.HasValue || assignment.Id != excludeAssignmentId.Value))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<EventSessionGroupSession>> GetAssignmentsForGroupUpdateAsync(
        Guid eventSessionGroupId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.EventSessionGroupSessions
            .Where(assignment => assignment.EventSessionGroupId == eventSessionGroupId)
            .ToListAsync(cancellationToken);
    }
}
