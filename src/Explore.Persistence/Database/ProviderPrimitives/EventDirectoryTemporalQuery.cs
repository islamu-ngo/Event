using System.Globalization;
using Explore.Application.Specifications.Events;
using Explore.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class EventDirectoryTemporalQuery
{
    private const string InstantCollation = "event_directory_instant";

    internal static IQueryable<Event> Apply(
        ExploreDbContext dbContext, IQueryable<Event> query, TemporalView view, DateTimeOffset now)
    {
        if (dbContext.Database.IsSqlite())
        {
            // SQLite cannot translate DateTimeOffset relational operators. Register on the
            // query's actual connection, not just the database initialization connection.
            // Parsing preserves offsets and 100ns boundaries that datetime/julianday lose.
            ((SqliteConnection)dbContext.Database.GetDbConnection()).CreateCollation(InstantCollation,
                static (left, right) => DateTimeOffset.Parse(left, CultureInfo.InvariantCulture)
                    .CompareTo(DateTimeOffset.Parse(right, CultureInfo.InvariantCulture)));
            var instant = now.ToString("O", CultureInfo.InvariantCulture);
            // These casts translate to SQL CAST(... AS TEXT); no rows are evaluated in CLR.
            return view switch
            {
                TemporalView.Upcoming => query.Where(e => e.FirstSessionStartUtc != null &&
                    EF.Functions.Collate((string)(object)e.FirstSessionStartUtc, InstantCollation).CompareTo(instant) > 0),
                TemporalView.Ongoing => query.Where(e => e.FirstSessionStartUtc != null &&
                    EF.Functions.Collate((string)(object)e.FirstSessionStartUtc, InstantCollation).CompareTo(instant) <= 0 &&
                    e.LastSessionEndUtc != null &&
                    EF.Functions.Collate((string)(object)e.LastSessionEndUtc, InstantCollation).CompareTo(instant) > 0),
                TemporalView.Past => query.Where(e => e.LastSessionEndUtc != null &&
                    EF.Functions.Collate((string)(object)e.LastSessionEndUtc, InstantCollation).CompareTo(instant) <= 0),
                TemporalView.UpcomingAndOngoing => query.Where(e => e.LastSessionEndUtc != null &&
                    EF.Functions.Collate((string)(object)e.LastSessionEndUtc, InstantCollation).CompareTo(instant) > 0),
                _ => query
            };
        }

        return view switch
        {
            TemporalView.Upcoming => query.Where(e => e.FirstSessionStartUtc != null && e.FirstSessionStartUtc > now),
            TemporalView.Ongoing => query.Where(e => e.FirstSessionStartUtc != null && e.FirstSessionStartUtc <= now && e.LastSessionEndUtc != null && e.LastSessionEndUtc > now),
            TemporalView.Past => query.Where(e => e.LastSessionEndUtc != null && e.LastSessionEndUtc <= now),
            TemporalView.UpcomingAndOngoing => query.Where(e => e.LastSessionEndUtc != null && e.LastSessionEndUtc > now),
            _ => query
        };
    }
}
