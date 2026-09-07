using System;
using System.Collections.Generic;
using System.Text;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventCategoriesRepository : IGenericRepository<EventCategories, Guid>
{
    Task<List<Event>> GetEventsByCategory(Guid categoryId);
    Task<List<Category>> GetCategoriesByEvent(Guid eventId);
    Task<bool> Exists(Guid eventId, Guid categoryId);
    Task<EventCategories?> GetByEventAndCategory(Guid eventId, Guid categoryId, Guid? excludeId = null);
}
