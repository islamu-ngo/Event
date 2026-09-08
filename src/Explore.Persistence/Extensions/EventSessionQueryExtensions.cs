using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Extensions;

internal static class EventSessionQueryExtensions
{
    internal static IQueryable<EventSession> IncludeStandardDetails(this IQueryable<EventSession> query)
    {
        return query
            .Include(s => s.Event)
            .Include(s => s.Location)
                .ThenInclude(l => l!.Pii)
            .Include(s => s.EventSessionKind)
            .Include(s => s.RegistrationMode)
            .Include(s => s.Room)
            .Include(s => s.FeaturedImage)
            .Include(s => s.IslamicAspect)
            .Include(s => s.SessionGroups)
                .ThenInclude(assignment => assignment.EventSessionGroup);
    }
}
