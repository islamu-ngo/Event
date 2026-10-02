using System.Globalization;
using System.Linq.Expressions;
using Explore.Application.Specifications.Events;
using Explore.Domain;
using Explore.Domain.Services.Discovery;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class EventDirectoryTemporalQuery
{
    private const string InstantCollation = "event_directory_instant";

    internal static IQueryable<EventSession> ApplyOccurrences(
        ExploreDbContext dbContext, IQueryable<EventSession> query, EventOccurrenceDiscoveryFilter filter)
    {
        Expression<Func<EventSession, bool>> temporal = filter.View switch
        {
            TemporalView.Upcoming => EventOccurrenceEligibility.Upcoming(filter.Now),
            TemporalView.Ongoing => EventOccurrenceEligibility.Ongoing(filter.Now),
            TemporalView.Past => EventOccurrenceEligibility.Past(filter.Now),
            TemporalView.UpcomingAndOngoing => EventOccurrenceEligibility.CurrentOrUpcoming(filter.Now),
            _ => session => true
        };
        var published = EventOccurrenceEligibility.Published();
        if (dbContext.Database.IsSqlite())
        {
            RegisterInstantCollation(dbContext);
            published = (Expression<Func<EventSession, bool>>)new SqliteInstantComparison().Visit(published)!;
            temporal = (Expression<Func<EventSession, bool>>)new SqliteInstantComparison().Visit(temporal)!;
        }

        return query.Where(published)
            .Where(EventOccurrenceEligibility.WithinDates(filter.DateFrom, filter.DateTo))
            .Where(temporal);
    }

    internal static IOrderedQueryable<EventSession> OrderOccurrences(
        ExploreDbContext dbContext, IQueryable<EventSession> query) =>
        dbContext.Database.IsSqlite()
            ? query.OrderBy(session => EF.Functions.Collate((string)(object)session.StartTime!, InstantCollation))
                .ThenBy(session => session.Id)
            : query.OrderBy(session => session.StartTime).ThenBy(session => session.Id);

    internal static IOrderedQueryable<Event> OrderEventsByOccurrence(
        ExploreDbContext dbContext, IQueryable<Event> query, IQueryable<EventSession> occurrences, bool descending)
    {
        var ordered = OrderOccurrences(dbContext, occurrences);
        if (dbContext.Database.IsSqlite())
        {
            Expression<Func<Event, string>> key = entity => EF.Functions.Collate(
                ordered.Where(session => session.EventId == entity.Id && session.TenantId == entity.TenantId)
                    .Select(session => (string)(object)session.StartTime!).First(), InstantCollation);
            return (descending ? query.OrderByDescending(key) : query.OrderBy(key)).ThenBy(entity => entity.Id);
        }

        Expression<Func<Event, DateTimeOffset?>> instant = entity =>
            ordered.Where(session => session.EventId == entity.Id && session.TenantId == entity.TenantId)
                .Select(session => session.StartTime).First();
        return (descending ? query.OrderByDescending(instant) : query.OrderBy(instant)).ThenBy(entity => entity.Id);
    }

    private static void RegisterInstantCollation(ExploreDbContext dbContext) =>
        ((SqliteConnection)dbContext.Database.GetDbConnection()).CreateCollation(InstantCollation,
            static (left, right) => DateTimeOffset.Parse(left, CultureInfo.InvariantCulture)
                .CompareTo(DateTimeOffset.Parse(right, CultureInfo.InvariantCulture)));

    /// <summary>
    /// Keeps Domain interval rules authoritative while translating instant comparisons
    /// to SQLite's connection-local, offset-aware collation. Only query parameters are
    /// evaluated here; entity rows and interval predicates remain entirely in SQL.
    /// </summary>
    private sealed class SqliteInstantComparison : ExpressionVisitor
    {
        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType is not (ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual
                    or ExpressionType.LessThan or ExpressionType.LessThanOrEqual)
                || Nullable.GetUnderlyingType(node.Left.Type) != typeof(DateTimeOffset)
                    && node.Left.Type != typeof(DateTimeOffset))
            {
                return base.VisitBinary(node);
            }

            var comparison = Expression.Call(InstantText(node.Left),
                typeof(string).GetMethod(nameof(string.CompareTo), [typeof(string)])!,
                InstantText(node.Right));
            Expression result = Expression.MakeBinary(node.NodeType, comparison, Expression.Constant(0));
            if (Nullable.GetUnderlyingType(node.Left.Type) is not null)
            {
                result = Expression.AndAlso(
                    Expression.NotEqual(node.Left, Expression.Constant(null, node.Left.Type)), result);
            }

            if (Nullable.GetUnderlyingType(node.Right.Type) is not null)
            {
                result = Expression.AndAlso(
                    Expression.NotEqual(node.Right, Expression.Constant(null, node.Right.Type)), result);
            }

            return result;
        }

        private static Expression InstantText(Expression expression)
        {
            if (expression is MemberExpression { Expression: ParameterExpression })
            {
                return Expression.Call(typeof(RelationalDbFunctionsExtensions),
                    nameof(RelationalDbFunctionsExtensions.Collate), [typeof(string)],
                    Expression.Constant(EF.Functions),
                    Expression.Convert(Expression.Convert(expression, typeof(object)), typeof(string)),
                    Expression.Constant(InstantCollation));
            }

            var value = Expression.Lambda<Func<DateTimeOffset?>>(
                Expression.Convert(expression, typeof(DateTimeOffset?))).Compile()();
            var text = value!.Value.ToString("O", CultureInfo.InvariantCulture);
            Expression<Func<string>> parameter = () => text;
            return parameter.Body;
        }
    }

    internal static IQueryable<Event> Apply(
        ExploreDbContext dbContext, IQueryable<Event> query, TemporalView view, DateTimeOffset now)
    {
        if (dbContext.Database.IsSqlite())
        {
            // SQLite cannot translate DateTimeOffset relational operators. Register on the
            // query's actual connection, not just the database initialization connection.
            // Parsing preserves offsets and 100ns boundaries that datetime/julianday lose.
            RegisterInstantCollation(dbContext);
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
