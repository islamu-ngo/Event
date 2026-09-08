using System;
using System.Collections.Generic;
using System.Text;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventTagsRepository : IGenericRepository<EventTags, Guid>
{
    Task<List<Event>> GetEventsByTag(Guid tagId);
    Task<List<Tag>> GetTagsByEvent(Guid eventId);
    Task<bool> Exists(Guid eventId, Guid tagId);
    Task<EventTags?> GetByEventAndTag(Guid eventId, Guid tagId, Guid? excludeId = null);
}
