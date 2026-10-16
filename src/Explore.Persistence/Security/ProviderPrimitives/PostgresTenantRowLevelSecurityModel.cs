using System.Collections;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Explore.Persistence.Security;

/// <summary>
/// Derives the complete set of tenant-scoped relational tables requiring PostgreSQL Row-Level Security.
/// Derives table metadata directly from EF Core model named query filters (QueryFilterNames.Tenant)
/// to prevent drift between domain configuration and database security policies.
/// </summary>
public static class PostgresTenantRowLevelSecurityModel
{
    public const string PolicyName = "tenant_isolation";
    public const string DefaultTenantIdColumn = "tenant_id";
    public const string PostgresProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>
    /// Enumerates every mapped table name belonging to an entity type with a configured QueryFilterNames.Tenant filter.
    /// Derived dynamically from the EF Core model metadata.
    /// </summary>
    public static IReadOnlyList<TenantTableMetadata> GetTenantTables(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var tables = new Dictionary<string, TenantTableMetadata>(StringComparer.Ordinal);

        foreach (var entity in model.GetEntityTypes())
        {
            if (!TryGetTenantQueryFilter(entity, out var allowsNullTenant, out var filter))
            {
                continue;
            }

            var tableName = entity.GetTableName();
            if (string.IsNullOrEmpty(tableName))
            {
                continue;
            }

            var schema = entity.GetSchema();
            var tenantProperty = entity.FindProperty("TenantId");

            if (tenantProperty != null)
            {
                var columnName = tenantProperty.GetColumnName(StoreObjectIdentifier.Table(tableName, schema))
                                 ?? DefaultTenantIdColumn;

                if (!tables.TryGetValue(tableName, out var existing))
                {
                    tables.Add(tableName, new TenantTableMetadata(tableName, schema, columnName, allowsNullTenant, null));
                }
                else if (allowsNullTenant && !existing.AllowsNullTenant)
                {
                    tables[tableName] = existing with { AllowsNullTenant = true };
                }
            }
            else
            {
                var paths = new TenantOwnershipPaths(filter.Parameters.Single());
                paths.Visit(filter.Body);
                if (paths.Paths.Count != 1)
                    throw new InvalidOperationException(
                        $"Tenant ownership for {entity.Name} must have one mapped navigation path.");
                var parentJoin = BuildParentJoin(
                    entity, paths.Paths.Values.Single(), 0, allowsNullTenant,
                    new HashSet<IEntityType> { entity });
                if (!tables.TryGetValue(tableName, out var existing))
                {
                    tables.Add(tableName, new TenantTableMetadata(tableName, schema, null, allowsNullTenant, parentJoin));
                }
                else if (allowsNullTenant && !existing.AllowsNullTenant)
                {
                    tables[tableName] = existing with { AllowsNullTenant = true };
                }
            }
        }

        return tables.Values.OrderBy(t => t.TableName, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Generates the SQL statement to enable and force RLS and attach the tenant isolation policy for a table.
    /// </summary>
    public static string BuildEnableRlsSql(TenantTableMetadata table, string? defaultSchema = null)
    {
        var tableIdentifier = FormatTableIdentifier(table.TableName, table.Schema, defaultSchema);
        string condition = BuildCondition(table, defaultSchema);
        string tableLiteral = tableIdentifier.Replace("'", "''", StringComparison.Ordinal);
        string fingerprint = BuildPolicyFingerprintPrefix(table, defaultSchema);
        return $"""
            ALTER TABLE {tableIdentifier} ENABLE ROW LEVEL SECURITY;
            ALTER TABLE {tableIdentifier} FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS "{PolicyName}" ON {tableIdentifier};
            CREATE POLICY "{PolicyName}" ON {tableIdentifier}
                FOR ALL
                USING ({condition})
                WITH CHECK ({condition});
            DO $tenant_policy_stamp$
            BEGIN
                EXECUTE (
                    SELECT format('COMMENT ON POLICY %I ON %s IS %L',
                        p.polname, '{tableLiteral}',
                        '{fingerprint}' || md5(p.polqual::text)
                            || ':' || md5(p.polwithcheck::text))
                    FROM pg_policy p
                    WHERE p.polrelid = '{tableLiteral}'::regclass AND p.polname = '{PolicyName}');
            END $tenant_policy_stamp$;
            """;
    }

    internal static string BuildPolicyFingerprintPrefix(TenantTableMetadata table, string? defaultSchema = null) =>
        "event-tenant-policy:v2:" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(BuildCondition(table, defaultSchema)))) + ":";

    private static string BuildCondition(TenantTableMetadata table, string? defaultSchema)
    {
        var tableIdentifier = FormatTableIdentifier(table.TableName, table.Schema, defaultSchema);
        string condition;
        if (table.ParentJoin is null)
        {
            var tenantColumn = table.TenantIdColumn ?? DefaultTenantIdColumn;
            condition = table.AllowsNullTenant
                ? $"(\"{tenantColumn}\" IS NULL OR \"{tenantColumn}\" = nullif(current_setting('{PostgresTenantSessionInterceptor.CurrentTenantSettingName}', true), '')::uuid)"
                : $"\"{tenantColumn}\" = nullif(current_setting('{PostgresTenantSessionInterceptor.CurrentTenantSettingName}', true), '')::uuid";
        }
        else
        {
            condition = BuildParentCondition(table.ParentJoin, tableIdentifier, defaultSchema, 0);
        }

        return condition;
    }

    /// <summary>
    /// Generates the SQL statement to drop the tenant isolation policy and disable RLS for a table.
    /// </summary>
    public static string BuildDisableRlsSql(TenantTableMetadata table, string? defaultSchema = null)
    {
        var tableIdentifier = FormatTableIdentifier(table.TableName, table.Schema, defaultSchema);

        return $"""
            DROP POLICY IF EXISTS "{PolicyName}" ON {tableIdentifier};
            ALTER TABLE {tableIdentifier} NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE {tableIdentifier} DISABLE ROW LEVEL SECURITY;
            """;
    }

    /// <summary>
    /// Applies RLS policies to all tenant-scoped tables on PostgreSQL.
    /// No-op on other providers.
    /// </summary>
    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Database.ProviderName != PostgresProviderName)
        {
            return;
        }

        var defaultSchema = context.GetService<IDbContextOptions>()
            .FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema;

        var tables = GetTenantTables(context.Model);
        if (tables.Count == 0)
        {
            return;
        }

        foreach (var table in tables)
        {
            var sql = BuildEnableRlsSql(table, defaultSchema);
            try
            {
                await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to apply PostgreSQL RLS to table '{table.Schema ?? defaultSchema}.{table.TableName}' (TenantIdColumn='{table.TenantIdColumn}'): {ex.Message}. SQL was: {sql}",
                    ex);
            }
        }
    }

    /// <summary>
    /// Removes RLS policies from all tenant-scoped tables on PostgreSQL.
    /// No-op on other providers.
    /// </summary>
    public static async Task RevertAsync(ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Database.ProviderName != PostgresProviderName)
        {
            return;
        }

        var defaultSchema = context.GetService<IDbContextOptions>()
            .FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema;

        var tables = GetTenantTables(context.Model);
        if (tables.Count == 0)
        {
            return;
        }

        var sb = new StringBuilder();
        foreach (var table in tables)
        {
            sb.AppendLine(BuildDisableRlsSql(table, defaultSchema));
        }

        await context.Database.ExecuteSqlRawAsync(sb.ToString(), cancellationToken);
    }

    private static string FormatTableIdentifier(string tableName, string? schema, string? targetSchema)
    {
        var effectiveSchema = targetSchema ?? schema;
        return string.IsNullOrEmpty(effectiveSchema)
            ? $"\"{tableName}\""
            : $"\"{effectiveSchema}\".\"{tableName}\"";
    }

    private static TenantParentJoinMetadata BuildParentJoin(
        IEntityType entity, string[] path, int index, bool allowsNullTenant, HashSet<IEntityType> visited)
    {
        var navigation = entity.FindNavigation(path[index]);
        if (navigation is null || navigation.IsCollection || !navigation.IsOnDependent)
            throw new InvalidOperationException($"Unsupported tenant ownership navigation {entity.Name}.{path[index]}.");
        var foreignKey = navigation.ForeignKey;
        var parent = foreignKey.PrincipalEntityType;
        if (foreignKey.Properties.Count != 1 || foreignKey.PrincipalKey.Properties.Count != 1 || !visited.Add(parent))
            throw new InvalidOperationException($"Invalid tenant ownership key or cycle at {entity.Name}.{path[index]}.");
        string parentTable = parent.GetTableName()
            ?? throw new InvalidOperationException($"Tenant ownership parent {parent.Name} has no table.");
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var parentStore = StoreObjectIdentifier.Table(parentTable, parent.GetSchema());
        string foreignColumn = foreignKey.Properties.Single().GetColumnName(store)
            ?? throw new InvalidOperationException($"Tenant ownership key for {entity.Name} is unmapped.");
        string parentColumn = foreignKey.PrincipalKey.Properties.Single().GetColumnName(parentStore)
            ?? throw new InvalidOperationException($"Tenant ownership key for {parent.Name} is unmapped.");
        if (index == path.Length - 2)
        {
            var tenant = parent.FindProperty("TenantId")
                ?? throw new InvalidOperationException($"Tenant ownership parent {parent.Name} has no tenant property.");
            string tenantColumn = tenant.GetColumnName(parentStore)
                ?? throw new InvalidOperationException($"Tenant ownership for {parent.Name} is unmapped.");
            TryGetTenantQueryFilter(parent, out bool parentAllowsNull, out _);
            return new(parentTable, parent.GetSchema(), foreignColumn, parentColumn, tenantColumn,
                parentAllowsNull || allowsNullTenant);
        }
        if (!foreignKey.IsRequired)
            throw new InvalidOperationException($"Intermediate tenant ownership for {entity.Name} must be required.");
        return new(parentTable, parent.GetSchema(), foreignColumn, parentColumn, null, false,
            BuildParentJoin(parent, path, index + 1, allowsNullTenant, visited));
    }

    private static string BuildParentCondition(
        TenantParentJoinMetadata join, string referencingRow, string? schema, int depth)
    {
        if ((join.ParentTenantIdColumn is null) == (join.ParentJoin is null))
            throw new InvalidOperationException("Tenant ownership must end in a tenant column or continue through one parent.");
        string alias = depth == 0 ? "\"p\"" : $"\"p{depth}\"";
        string parentTable = FormatTableIdentifier(join.ParentTableName, join.ParentSchema, schema);
        string parentCondition = join.ParentJoin is { } next
            ? BuildParentCondition(next, alias, schema, depth + 1)
            : join.ParentAllowsNullTenant
                ? $"({alias}.\"{join.ParentTenantIdColumn}\" IS NULL OR {alias}.\"{join.ParentTenantIdColumn}\" = nullif(current_setting('{PostgresTenantSessionInterceptor.CurrentTenantSettingName}', true), '')::uuid)"
                : $"{alias}.\"{join.ParentTenantIdColumn}\" = nullif(current_setting('{PostgresTenantSessionInterceptor.CurrentTenantSettingName}', true), '')::uuid";
        return $"EXISTS (SELECT 1 FROM {parentTable} AS {alias} WHERE {alias}.\"{join.ParentKeyColumn}\" = {referencingRow}.\"{join.ForeignKeyColumn}\" AND {parentCondition})";
    }

    private sealed class TenantOwnershipPaths(ParameterExpression parameter) : ExpressionVisitor
    {
        public Dictionary<string, string[]> Paths { get; } = new(StringComparer.Ordinal);

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Member.Name == "TenantId")
            {
                var names = new List<string>();
                Expression? current = node;
                while (current is MemberExpression member)
                {
                    names.Add(member.Member.Name);
                    current = member.Expression;
                }
                if (current == parameter)
                {
                    names.Reverse();
                    Paths.TryAdd(string.Join('.', names), names.ToArray());
                }
            }
            return base.VisitMember(node);
        }
    }

    private static bool TryGetTenantQueryFilter(
        IEntityType entity, out bool allowsNullTenant, out LambdaExpression filter)
    {
        allowsNullTenant = false;
        filter = null!;
        var qf = entity.FindAnnotation("QueryFilter")?.Value as IEnumerable;
        if (qf is null)
        {
            return false;
        }

        foreach (var item in qf)
        {
            if (item is null) continue;

            var keyProp = item.GetType().GetProperty("Key");
            var key = keyProp?.GetValue(item) as string;
            if (string.Equals(key, QueryFilterNames.Tenant, StringComparison.Ordinal))
            {
                var exprProp = item.GetType().GetProperty("Expression");
                filter = exprProp?.GetValue(item) as LambdaExpression
                    ?? throw new InvalidOperationException($"Tenant ownership for {entity.Name} has no expression.");
                var expressionStr = filter.ToString();
                allowsNullTenant = expressionStr.Contains("== null", StringComparison.Ordinal)
                    || expressionStr.Contains("== null", StringComparison.OrdinalIgnoreCase);
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Metadata describing a tenant-scoped database table.
/// </summary>
public sealed record TenantTableMetadata(
    string TableName,
    string? Schema,
    string? TenantIdColumn,
    bool AllowsNullTenant,
    TenantParentJoinMetadata? ParentJoin = null);

/// <summary>
/// Metadata describing how a child table joins to a tenant-scoped parent table.
/// </summary>
public sealed record TenantParentJoinMetadata(
    string ParentTableName,
    string? ParentSchema,
    string ForeignKeyColumn,
    string ParentKeyColumn,
    string? ParentTenantIdColumn,
    bool ParentAllowsNullTenant,
    TenantParentJoinMetadata? ParentJoin = null);
