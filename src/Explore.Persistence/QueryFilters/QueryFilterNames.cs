// These names are used to selectively disable filters via IgnoreQueryFilter().

namespace Explore.Persistence.QueryFilters;

/// <summary>
/// Constants for named query filter names.
/// Use these with IgnoreQueryFilter() to selectively disable specific filters.
/// </summary>
public static class QueryFilterNames
{
    /// <summary>
    /// Filter name for soft delete filtering (!IsDeleted).
    /// Use IgnoreQueryFilter(SoftDelete) to include deleted entities in queries.
    /// </summary>
    public const string SoftDelete = "SoftDelete";

    /// <summary>
    /// Filter name for tenant isolation (TenantId == current tenant).
    /// Use IgnoreQueryFilter(Tenant) to query across all tenants (admin operations).
    /// WARNING: Disabling this filter can expose data from other tenants.
    /// </summary>
    public const string Tenant = "Tenant";
}
