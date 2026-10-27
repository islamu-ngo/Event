using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

/// <summary>Loads the complete retained publication graph without replacing saved meanings or plans.</summary>
internal static class ModerationPolicyPublicationGraph
{
    internal static IQueryable<ModerationPolicyPublication> Include(IQueryable<ModerationPolicyPublication> query) =>
        query.AsSplitQuery()
            .Include(row => row.Classes).Include(row => row.Reasons).Include(row => row.Modes).Include(row => row.Evidence)
            .Include(row => row.Profiles).ThenInclude(row => row.Scopes)
            .Include(row => row.Profiles).ThenInclude(row => row.Subjects)
            .Include(row => row.Profiles).ThenInclude(row => row.Effects)
            .Include(row => row.Profiles).ThenInclude(row => row.Plans)
            .Include(row => row.Profiles).ThenInclude(row => row.Consumers)
            .Include(row => row.Profiles).ThenInclude(row => row.Prerequisites);
}
