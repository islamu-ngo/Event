using System;
using System.Collections.Generic;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class EventTagsRepository : GenericRepository<EventTags, Guid>, IEventTagsRepository
{
    private readonly ExploreDbContext _dbContext;

    public EventTagsRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<Event>> GetEventsByTag(Guid tagId)
    {
        var eventIds = await _dbContext.EventTags
            .AsNoTracking()
            .Where(et => et.TagId == tagId)
            .Select(et => et.EventId)
            .ToListAsync();

        return await _dbContext.Events
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Include(e => e.EventType)
            .Include(e => e.AudienceGender)
            .Include(e => e.AudienceAge)
            .Include(e => e.Actor)
                .ThenInclude(a => a.ActorType)
            .Include(e => e.Actor)
                .ThenInclude(a => a!.Pii)
            .Include(e => e.FeaturedImage)
            .Include(e => e.EventStatus)
            .Include(e => e.VisibilityType)
            .Include(e => e.EventFormat)
            .Include(e => e.Madhab)
            .Include(e => e.ParticipationConfiguration)
            .Include(e => e.TicketCatalogVersions.Where(catalog =>
                !catalog.IsDeleted && catalog.TicketCatalogStatusId == (int)TicketCatalogStatusEnum.Published))
                .ThenInclude(catalog => catalog.TicketTypes.Where(ticketType => !ticketType.IsDeleted))
            .Where(e => eventIds.Contains(e.Id))
            .ToListAsync();
    }

    public async Task<List<Tag>> GetTagsByEvent(Guid eventId)
    {
        return await _dbContext.EventTags
            .AsNoTracking()
            .Include(et => et.Tag)
            .Where(et => et.EventId == eventId)
            .Select(et => et.Tag)
            .ToListAsync();
    }

    public async Task<bool> Exists(Guid eventId, Guid tagId)
    {
        return await _dbContext.EventTags
            .AsNoTracking()
            .AnyAsync(et => et.EventId == eventId && et.TagId == tagId);
    }

    public async Task<EventTags?> GetByEventAndTag(Guid eventId, Guid tagId, Guid? excludeId = null)
    {
        var query = _dbContext.EventTags
            .AsNoTracking()
            .Where(et => et.EventId == eventId && et.TagId == tagId);

        if (excludeId.HasValue)
        {
            query = query.Where(et => et.Id != excludeId.Value);
        }

        return await query.FirstOrDefaultAsync();
    }
}
