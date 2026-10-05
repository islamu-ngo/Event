using System.Globalization;
using System.Linq.Expressions;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Federation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class AtprotoEventProjectionTemporalQuery
{
    internal static IOrderedQueryable<AtprotoEventProjection> Order(
        ExploreDbContext dbContext, IQueryable<AtprotoEventProjection> query,
        AtprotoEventDiscoverySort sort, bool descending)
    {
        if (dbContext.Database.IsSqlite())
        {
            if (sort == AtprotoEventDiscoverySort.CreatedAt)
                return descending
                    ? query.OrderByDescending(value => EF.Functions.Collate((string)(object)value.CreatedAt, "event_directory_instant"))
                        .ThenBy(value => value.DiscoverySourceSortKey)
                    : query.OrderBy(value => EF.Functions.Collate((string)(object)value.CreatedAt, "event_directory_instant"))
                        .ThenBy(value => value.DiscoverySourceSortKey);
            if (sort == AtprotoEventDiscoverySort.Date)
                return descending
                    ? query.OrderBy(value => value.StartsAt == null)
                        .ThenByDescending(value => EF.Functions.Collate((string)(object)value.StartsAt!, "event_directory_instant"))
                        .ThenBy(value => value.DiscoverySourceSortKey)
                    : query.OrderByDescending(value => value.StartsAt == null)
                        .ThenBy(value => EF.Functions.Collate((string)(object)value.StartsAt!, "event_directory_instant"))
                        .ThenBy(value => value.DiscoverySourceSortKey);
        }
        return (sort, descending) switch
        {
            (AtprotoEventDiscoverySort.Title, false) => query
                .OrderBy(value => value.DiscoveryTitleSortKey)
                .ThenBy(value => value.DiscoverySourceSortKey),
            (AtprotoEventDiscoverySort.Title, _) => query
                .OrderByDescending(value => value.DiscoveryTitleSortKey)
                .ThenBy(value => value.DiscoverySourceSortKey),
            (AtprotoEventDiscoverySort.CreatedAt, false) => query
                .OrderBy(value => value.CreatedAt)
                .ThenBy(value => value.DiscoverySourceSortKey),
            (AtprotoEventDiscoverySort.CreatedAt, _) => query
                .OrderByDescending(value => value.CreatedAt)
                .ThenBy(value => value.DiscoverySourceSortKey),
            (AtprotoEventDiscoverySort.Views, _) => query.OrderBy(value => value.DiscoverySourceSortKey),
            (_, false) => query.OrderByDescending(value => value.StartsAt == null)
                .ThenBy(value => value.StartsAt).ThenBy(value => value.DiscoverySourceSortKey),
            _ => query.OrderBy(value => value.StartsAt == null)
                .ThenByDescending(value => value.StartsAt).ThenBy(value => value.DiscoverySourceSortKey)
        };
    }

    internal static IQueryable<AtprotoEventProjection> PortableInstants(
        ExploreDbContext dbContext, IQueryable<AtprotoEventProjection> query)
    {
        if (!dbContext.Database.IsSqlite())
            return query;
        ((SqliteConnection)dbContext.Database.GetDbConnection()).CreateCollation("event_directory_instant",
            static (left, right) => DateTimeOffset.Parse(left, CultureInfo.InvariantCulture)
                .CompareTo(DateTimeOffset.Parse(right, CultureInfo.InvariantCulture)));
        return query.Provider.CreateQuery<AtprotoEventProjection>(new SqliteInstantComparison().Visit(query.Expression)!);
    }

    private sealed class SqliteInstantComparison : ExpressionVisitor
    {
        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType is not (ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual
                    or ExpressionType.LessThan or ExpressionType.LessThanOrEqual or ExpressionType.Equal)
                || Nullable.GetUnderlyingType(node.Left.Type) != typeof(DateTimeOffset)
                    && node.Left.Type != typeof(DateTimeOffset)
                || node.Right is ConstantExpression { Value: null })
                return base.VisitBinary(node);
            var left = Expression.Call(typeof(RelationalDbFunctionsExtensions),
                nameof(RelationalDbFunctionsExtensions.Collate), [typeof(string)],
                Expression.Constant(EF.Functions),
                Expression.Convert(Expression.Convert(node.Left, typeof(object)), typeof(string)),
                Expression.Constant("event_directory_instant"));
            var value = Expression.Lambda<Func<DateTimeOffset?>>(
                Expression.Convert(node.Right, typeof(DateTimeOffset?))).Compile()();
            var text = value?.ToString("O", CultureInfo.InvariantCulture);
            Expression<Func<string?>> parameter = () => text;
            var comparison = Expression.Call(left, typeof(string).GetMethod(nameof(string.CompareTo), [typeof(string)])!,
                parameter.Body);
            return Expression.MakeBinary(node.NodeType, comparison, Expression.Constant(0));
        }
    }
}
