using System.Collections;
using System.Text;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Explore.Persistence.Security;

/// <summary>
/// Derives the canonical set of tenant-scoped relational tables requiring PostgreSQL Row-Level Security.
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
            if (!TryGetTenantQueryFilter(entity, out var allowsNullTenant))
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
                // Entity is tenant-scoped via navigation to a principal entity (e.g. 1:1 vertical partition)
                TenantParentJoinMetadata? parentJoin = null;
                foreach (var fk in entity.GetForeignKeys())
                {
                    var principalEntity = fk.PrincipalEntityType;
                    var principalTenantProp = principalEntity.FindProperty("TenantId");
                    if (principalTenantProp != null)
                    {
                        var parentTableName = principalEntity.GetTableName();
                        if (string.IsNullOrEmpty(parentTableName)) continue;

                        var parentSchema = principalEntity.GetSchema();
                        var fkProp = fk.Properties[0];
                        var pkProp = fk.PrincipalKey.Properties[0];
                        var fkColumn = fkProp.GetColumnName(StoreObjectIdentifier.Table(tableName, schema)) ?? fkProp.Name;
                        var pkColumn = pkProp.GetColumnName(StoreObjectIdentifier.Table(parentTableName, parentSchema)) ?? pkProp.Name;
                        var parentTenantColumn = principalTenantProp.GetColumnName(StoreObjectIdentifier.Table(parentTableName, parentSchema)) ?? DefaultTenantIdColumn;
                        TryGetTenantQueryFilter(principalEntity, out var parentAllowsNull);

                        parentJoin = new TenantParentJoinMetadata(
                            parentTableName,
                            parentSchema,
                            fkColumn,
                            pkColumn,
                            parentTenantColumn,
                            parentAllowsNull || allowsNullTenant);
                        break;
                    }
                }

                if (parentJoin != null)
                {
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
        }

        return tables.Values.OrderBy(t => t.TableName, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Generates the SQL statement to enable and force RLS and attach the tenant isolation policy for a table.
    /// </summary>
    public static string BuildEnableRlsSql(TenantTableMetadata table, string? defaultSchema = null)
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
            var parentTable = FormatTableIdentifier(table.ParentJoin.ParentTableName, table.ParentJoin.ParentSchema, defaultSchema);
            var parentCondition = table.ParentJoin.ParentAllowsNullTenant
                ? $"(\"p\".\"{table.ParentJoin.ParentTenantIdColumn}\" IS NULL OR \"p\".\"{table.ParentJoin.ParentTenantIdColumn}\" = nullif(current_setting('{PostgresTenantSessionInterceptor.CurrentTenantSettingName}', true), '')::uuid)"
                : $"\"p\".\"{table.ParentJoin.ParentTenantIdColumn}\" = nullif(current_setting('{PostgresTenantSessionInterceptor.CurrentTenantSettingName}', true), '')::uuid";

            condition = $"EXISTS (SELECT 1 FROM {parentTable} AS \"p\" WHERE \"p\".\"{table.ParentJoin.ParentKeyColumn}\" = {tableIdentifier}.\"{table.ParentJoin.ForeignKeyColumn}\" AND {parentCondition})";
        }

        return $"""
            ALTER TABLE {tableIdentifier} ENABLE ROW LEVEL SECURITY;
            ALTER TABLE {tableIdentifier} FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS "{PolicyName}" ON {tableIdentifier};
            CREATE POLICY "{PolicyName}" ON {tableIdentifier}
                FOR ALL
                USING ({condition})
                WITH CHECK ({condition});
            """;
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

    private static string FormatTableIdentifier(string tableName, string? schema, string? defaultSchema)
    {
        var effectiveSchema = schema ?? defaultSchema;
        return string.IsNullOrEmpty(effectiveSchema)
            ? $"\"{tableName}\""
            : $"\"{effectiveSchema}\".\"{tableName}\"";
    }

    private static bool TryGetTenantQueryFilter(IEntityType entity, out bool allowsNullTenant)
    {
        allowsNullTenant = false;
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
                var expressionStr = exprProp?.GetValue(item)?.ToString() ?? string.Empty;
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
    string ParentTenantIdColumn,
    bool ParentAllowsNullTenant);
