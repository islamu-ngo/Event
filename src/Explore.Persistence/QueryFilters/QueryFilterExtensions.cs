namespace Explore.Persistence.QueryFilters;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// Extension methods for selectively ignoring named query filters.
/// Uses EF Core 10+ IgnoreQueryFilters(string[]) syntax for selective filter disabling.
/// </summary>
public static class QueryFilterExtensions
{
    /// <summary>
    /// Ignores the soft delete filter, including soft-deleted entities in the query.
    /// Tenant isolation is still enforced.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="query">The queryable to modify.</param>
    /// <returns>The queryable with the soft delete filter disabled.</returns>
    public static IQueryable<TEntity> IncludeDeleted<TEntity>(this IQueryable<TEntity> query)
        where TEntity : class
    {
        return query.IgnoreQueryFilters([QueryFilterNames.SoftDelete]);
    }

    /// <summary>
    /// Ignores the tenant filter, querying across all tenants.
    /// WARNING: Use only for admin/system operations with an explicit tenant predicate or privileged scope.
    /// Soft delete filter is still enforced.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="query">The queryable to modify.</param>
    /// <param name="reason">Documented reason for the tenant-filter bypass.</param>
    /// <returns>The queryable with the tenant filter disabled.</returns>
    public static IQueryable<TEntity> IgnoreTenantFilter<TEntity>(this IQueryable<TEntity> query, string reason)
        where TEntity : class
    {
        EnsureBypassReason(reason);
        return query.IgnoreQueryFilters([QueryFilterNames.Tenant]);
    }

    /// <summary>
    /// Ignores all query filters (tenant and soft delete).
    /// WARNING: Use only for maintenance operations requiring full data access.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="query">The queryable to modify.</param>
    /// <param name="reason">Documented reason for the full-filter bypass.</param>
    /// <returns>The queryable with all filters disabled.</returns>
    public static IQueryable<TEntity> IgnoreAllFilters<TEntity>(this IQueryable<TEntity> query, string reason)
        where TEntity : class
    {
        EnsureBypassReason(reason);
        return query.IgnoreQueryFilters();
    }

    private static void EnsureBypassReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Query filter bypasses require a non-empty reason.", nameof(reason));
        }
    }
}
