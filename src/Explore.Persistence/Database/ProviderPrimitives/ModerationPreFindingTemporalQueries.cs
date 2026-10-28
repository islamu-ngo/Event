using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

/// <summary>Supplies the SQLite comparison that EF cannot translate for DateTimeOffset retention values.</summary>
internal static class ModerationPreFindingTemporalQueries
{
    internal static IQueryable<ModerationPreFindingNotice> UnpurgedRetainedThrough(
        ExploreDbContext context, DateTimeOffset now) =>
        context.Database.ProviderName == RelationalNamedLock.SqliteProvider
            ? context.Set<ModerationPreFindingNotice>().FromSqlInterpolated($"""
                SELECT n.* FROM moderation_pre_finding_notices AS n
                WHERE n.purged_at IS NULL AND julianday(n.retain_until) <= julianday({now})
                """)
            : context.Set<ModerationPreFindingNotice>().Where(row => row.PurgedAt == null && row.RetainUntil <= now);
}
