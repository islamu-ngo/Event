using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Events.Moderation;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class EventHeavyRedactionRepository(ExploreDbContext dbContext) : IEventHeavyRedactionRepository
{
    public async Task<EventHeavyRedactionGraph?> GetForUpdateAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var @event = await dbContext.Events
            .AsSplitQuery()
            .Include(e => e.FeaturedImage)
            .Include(e => e.BackgroundImage)
            .Include(e => e.TechAspect)
            .Include(e => e.Sessions)
                .ThenInclude(session => session.FeaturedImage)
            .Include(e => e.Sessions)
                .ThenInclude(session => session.IslamicAspect)
            .Include(e => e.Days)
                .ThenInclude(day => day.BannerImage)
            .Include(e => e.AgendaItems)
            .Include(e => e.SessionGroups)
            .Include(e => e.AtprotoRecord)
            .FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);

        if (@event is null)
        {
            return null;
        }

        var sessionIds = @event.Sessions.Select(session => session.Id).ToArray();
        var sessionAgendaItems = sessionIds.Length == 0
            ? []
            : await dbContext.EventSessionAgendaItems
                .Where(item => sessionIds.Contains(item.EventSessionId))
                .ToListAsync(cancellationToken);

        var eventCustomPropertyDefinitions = await dbContext.EventCustomPropertyDefinitions
            .AsSplitQuery()
            .Where(definition => definition.EventId == eventId)
            .Include(definition => definition.Options.OrderBy(option => option.SortOrder))
            .Include(definition => definition.Values.OrderBy(value => value.Ordinal))
            .OrderBy(definition => definition.SortOrder)
            .ThenBy(definition => definition.Id)
            .ToListAsync(cancellationToken);

        var eventCustomPropertyProjections = await dbContext.EventCustomPropertyProjections
            .Where(projection => projection.EventId == eventId)
            .ToListAsync(cancellationToken);

        var sessionCustomPropertyDefinitions = sessionIds.Length == 0
            ? []
            : await dbContext.EventSessionCustomPropertyDefinitions
                .AsSplitQuery()
                .Where(definition => sessionIds.Contains(definition.EventSessionId))
                .Include(definition => definition.Options.OrderBy(option => option.SortOrder))
                .Include(definition => definition.Values.OrderBy(value => value.Ordinal))
                .OrderBy(definition => definition.SortOrder)
                .ThenBy(definition => definition.Id)
                .ToListAsync(cancellationToken);

        var sessionCustomPropertyProjections = sessionIds.Length == 0
            ? []
            : await dbContext.EventSessionCustomPropertyProjections
                .Where(projection => sessionIds.Contains(projection.EventSessionId))
                .ToListAsync(cancellationToken);

        var imageObjectIds = CollectImageObjectIds(@event);
        var imageStorageObjects = imageObjectIds.Count == 0
            ? []
            : await dbContext.StorageObjects
                .Where(storageObject => imageObjectIds.Contains(storageObject.Id))
                .ToListAsync(cancellationToken);

        return new EventHeavyRedactionGraph(
            @event,
            @event.Sessions.ToArray(),
            @event.Days.ToArray(),
            @event.AgendaItems.ToArray(),
            sessionAgendaItems,
            @event.SessionGroups.ToArray(),
            eventCustomPropertyDefinitions,
            eventCustomPropertyProjections,
            sessionCustomPropertyDefinitions,
            sessionCustomPropertyProjections,
            imageStorageObjects);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    private static HashSet<Guid> CollectImageObjectIds(Event @event)
    {
        var ids = new HashSet<Guid>();
        AddIfPresent(ids, @event.FeaturedImageId);
        AddIfPresent(ids, @event.BackgroundImageId);

        foreach (var session in @event.Sessions)
        {
            AddIfPresent(ids, session.FeaturedImageId);
        }

        foreach (var day in @event.Days)
        {
            AddIfPresent(ids, day.BannerImageId);
        }

        return ids;
    }

    private static void AddIfPresent(HashSet<Guid> ids, Guid? id)
    {
        if (id is { } value)
        {
            ids.Add(value);
        }
    }
}
