using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class EventRegistrationRepository : GenericRepository<EventRegistration, Guid>, IEventRegistrationRepository
{
    private readonly ExploreDbContext _dbContext;

    public EventRegistrationRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<EventRegistration>> GetLocationAccessCoverageAsync(
        Guid tenantId,
        Guid eventId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || eventId == Guid.Empty || userId == Guid.Empty)
        {
            return [];
        }

        return await _dbContext.EventRegistrations
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .AsNoTrackingWithIdentityResolution()
            .AsSingleQuery()
            .Include(registration => registration.RegistrationOrder)
            .Include(registration => registration.EventSession)
                .ThenInclude(session => session.EventLocation)
            .Include(registration => registration.Event)
                .ThenInclude(@event => @event.Sessions)
                    .ThenInclude(session => session.EventLocation)
            .Where(registration => registration.TenantId == tenantId
                && registration.EventId == eventId
                && registration.LinkedUserId == userId
                && registration.RegistrationOrderId != null
                && registration.RegistrationOrder != null
                && registration.RegistrationOrder.TenantId == tenantId
                && registration.RegistrationOrder.EventId == eventId
                && registration.RegistrationOrder.AccountUserId == userId
                && registration.EventSession.TenantId == tenantId
                && registration.EventSession.EventId == eventId
                && registration.EventSession.EventLocationId != null
                && registration.Event.TenantId == tenantId
                && registration.Event.Id == eventId)
            .OrderBy(registration => registration.RegistrationOrderId)
            .ThenBy(registration => registration.EventSessionId)
            .ThenBy(registration => registration.Id)
            .ToListAsync(cancellationToken);
    }

}
