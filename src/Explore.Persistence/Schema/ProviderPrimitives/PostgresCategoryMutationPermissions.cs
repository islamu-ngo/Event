using System.Data;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

/// <summary>
/// Enrolls exact tenant-isolated Runtime DML permissions for Category writes and CategoryTypeCategories reads/writes.
/// PostgreSQL RLS (<c>tenant_isolation</c>) remains mandatory on both tables; Global and PUBLIC stay denied.
/// </summary>
public static class PostgresCategoryMutationPermissions
{
    private const string Drift = "category_mutation_permissions_drift";
    private const string NotReady = "category_mutation_permissions_not_ready";

    private static readonly string[] CategoryInsertProperties =
    [
        nameof(Category.Id), nameof(Category.ConcurrencyStamp), nameof(Category.MasterCode),
        nameof(Category.FullName), nameof(Category.ParentId), nameof(Category.TenantId)
    ];

    private static readonly string[] CategoryUpdateProperties =
    [
        nameof(Category.ConcurrencyStamp), nameof(Category.MasterCode),
        nameof(Category.FullName), nameof(Category.ParentId)
    ];

    private static readonly string[] LinkAllProperties =
    [
        nameof(CategoryTypeCategories.Id), nameof(CategoryTypeCategories.CategoryId),
        nameof(CategoryTypeCategories.CategoryTypeId), nameof(CategoryTypeCategories.TenantId)
    ];

    private static readonly string[] LinkUpdateProperties =
    [
        nameof(CategoryTypeCategories.CategoryId), nameof(CategoryTypeCategories.CategoryTypeId)
    ];

    private static readonly string[] ColumnPrivileges = ["SELECT", "INSERT", "UPDATE"];

    internal static bool TryGetRuntimeColumns(ExploreDbContext context, Type type,
        out IReadOnlyList<string> read, out IReadOnlyList<string> insert, out IReadOnlyList<string> update, out bool delete)
    {
        if (type == typeof(Category))
        {
            var source = MapCategory(context);
            read = [];
            insert = Array.AsReadOnly(source.Insert);
            update = Array.AsReadOnly(source.Update);
            delete = true;
            return true;
        }
        if (type == typeof(CategoryTypeCategories))
        {
            var source = MapLinks(context);
            read = Array.AsReadOnly(source.Read);
            insert = Array.AsReadOnly(source.Insert);
            update = Array.AsReadOnly(source.Update);
            delete = true;
            return true;
        }
        read = [];
        insert = [];
        update = [];
        delete = false;
        return false;
    }

    internal static string RuntimeCategoryColumnPredicate(ExploreDbContext context, string runtimeLogin,
        string roleOid, string relationOid, string columnName, string privilege)
    {
        var category = MapCategory(context);
        string insertList = ArraySql(category.Insert);
        string updateList = ArraySql(category.Update);
        return $"""
            ({roleOid}={Role(runtimeLogin)} AND {relationOid}=pg_catalog.to_regclass({Text(category.Table)})
                AND (({privilege}='INSERT' AND {columnName}=ANY({insertList}))
                    OR ({privilege}='UPDATE' AND {columnName}=ANY({updateList})))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE roleid={Role(runtimeLogin)})
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_attribute column_info
                    CROSS JOIN LATERAL pg_catalog.aclexplode(column_info.attacl) acl
                    JOIN pg_catalog.pg_class relation ON relation.oid=column_info.attrelid
                    WHERE column_info.attrelid={relationOid} AND column_info.attname={columnName} AND column_info.attnum>0
                        AND acl.grantee={roleOid} AND acl.grantor=relation.relowner
                        AND acl.privilege_type={privilege} AND NOT acl.is_grantable)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute column_info
                    CROSS JOIN LATERAL pg_catalog.aclexplode(column_info.attacl) acl
                    WHERE column_info.attrelid={relationOid} AND column_info.attname={columnName}
                        AND acl.grantee=0 AND acl.privilege_type={privilege}))
            """;
    }

    internal static string RuntimeCategoryDeletePredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        string roleOid, string relationOid)
    {
        var category = MapCategory(context);
        return $"""
            ({roleOid}={Role(enrollment.RuntimeLogin)} AND {relationOid}=pg_catalog.to_regclass({Text(category.Table)})
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE roleid={Role(enrollment.RuntimeLogin)})
                AND NOT pg_catalog.has_table_privilege({roleOid},{relationOid},'DELETE WITH GRANT OPTION')
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                    CROSS JOIN LATERAL pg_catalog.aclexplode(relation.relacl) acl
                    WHERE relation.oid={relationOid} AND acl.grantee={roleOid} AND acl.grantor=relation.relowner
                        AND acl.privilege_type='DELETE' AND NOT acl.is_grantable)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                    CROSS JOIN LATERAL pg_catalog.aclexplode(relation.relacl) acl
                    WHERE relation.oid={relationOid} AND acl.grantee=0 AND acl.privilege_type='DELETE'))
            """;
    }

    internal static string OptionalCataloguePredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        $"(({Catalogue(context, enrollment, complete: false)}) AND (({Absent(context, enrollment)}) OR ({Catalogue(context, enrollment, complete: true)})))";

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        var sources = Sources(context);
        context.RequireBoundTenantLease();
        var connection = RelationalConnectionIdentity.Capture(context);
        var contextId = context.ContextId;
        var tenantContext = context.TenantContext;
        Guid? tenant = context.TenantFilterTenantId;
        bool bypass = context.IsTenantFilterBypassed;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change)
        {
            if (change.OriginalState == ConnectionState.Open && change.CurrentState != ConnectionState.Open)
                interrupted = true;
        }
        connection.StateChange += Changed;
        try
        {
            await using var owned = context.Database.CurrentTransaction is null
                ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
            var transaction = context.Database.CurrentTransaction
                ?? throw new UnauthorizedAccessException("category_mutation_installer_owner_changed");
            var physical = transaction.GetDbTransaction();
            int? backend = connection.BackendProcessId;
            void RequireOriginal()
            {
                context.RequireBoundTenantLease();
                if (context.ContextId != contextId || interrupted || !connection.Matches(context)
                    || connection.State != ConnectionState.Open || connection.BackendProcessId != backend
                    || !ReferenceEquals(tenantContext, context.TenantContext) || context.TenantFilterTenantId != tenant
                    || context.IsTenantFilterBypassed != bypass
                    || !ReferenceEquals(physical, context.Database.CurrentTransaction?.GetDbTransaction())
                    || !connection.Owns(physical))
                    throw new UnauthorizedAccessException("category_mutation_installer_owner_changed");
            }
            string savepoint = "category_mutation_permissions_" + Guid.CreateVersion7().ToString("N");
            RequireOriginal();
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            bool rolledBack = false;
            try
            {
                if (!await IsTrueAsync(context, Identity(enrollment.MigratorLogin), cancellationToken))
                    throw new UnauthorizedAccessException("category_mutation_installer_wrong_identity");
                RequireOriginal();
                if (!await IsTrueAsync(context, OptionalCataloguePredicate(context, enrollment), cancellationToken))
                    throw new InvalidOperationException(Drift);
                RequireOriginal();
                if (await IsTrueAsync(context, Absent(context, enrollment), cancellationToken))
                {
                    var names = context.GetService<ISqlGenerationHelper>();
                    string runtimeId = names.DelimitIdentifier(enrollment.RuntimeLogin);
                    foreach (var source in sources)
                    {
                        RequireOriginal();
                        await ExecuteAsync(context,
                            $"GRANT USAGE ON SCHEMA {names.DelimitIdentifier(source.Schema)} TO {runtimeId}",
                            cancellationToken);
                        foreach (string privilege in ColumnPrivileges)
                        {
                            string[] cols = privilege switch
                            {
                                "SELECT" => source.Read,
                                "INSERT" => source.Insert,
                                "UPDATE" => source.Update,
                                _ => []
                            };
                            if (cols.Length == 0) continue;
                            RequireOriginal();
                            await ExecuteAsync(context,
                                $"GRANT {privilege} ({string.Join(',', cols.Select(names.DelimitIdentifier))}) ON {source.Table} TO {runtimeId}",
                                cancellationToken);
                        }
                        RequireOriginal();
                        await ExecuteAsync(context, $"GRANT DELETE ON {source.Table} TO {runtimeId}", cancellationToken);
                    }
                }
                RequireOriginal();
                if (!await IsTrueAsync(context, $"({Identity(enrollment.MigratorLogin)}) AND ({Catalogue(context, enrollment, complete: true)})", cancellationToken))
                    throw new InvalidOperationException(Drift);
                RequireOriginal();
                cancellationToken.ThrowIfCancellationRequested();
                await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None);
                RequireOriginal();
                if (owned is not null) await owned.CommitAsync(cancellationToken);
            }
            catch (Exception failure)
            {
                if (!rolledBack)
                {
                    try
                    {
                        await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
                        await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None);
                        rolledBack = true;
                    }
                    catch (Exception cleanup)
                    {
                        failure.Data["category_mutation_savepoint_cleanup_error"] = cleanup.Message;
                    }
                }
                throw;
            }
        }
        finally { connection.StateChange -= Changed; }
    }

    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        if (enrollment is null) throw new UnauthorizedAccessException(NotReady);
        enrollment.Validate();
        context.RequireBoundTenantLease();
        var connection = RelationalConnectionIdentity.Capture(context);
        Guid? tenant = context.TenantFilterTenantId;
        if (tenant is null || context.IsTenantFilterBypassed)
            throw new UnauthorizedAccessException(NotReady);
        if (connection.State != ConnectionState.Open)
            await context.Database.OpenConnectionAsync(cancellationToken);
        if (!await IsTrueAsync(context,
            $"({Identity(enrollment.RuntimeLogin)}) AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')={Text(tenant.Value.ToString("D"))}",
            cancellationToken))
            throw new UnauthorizedAccessException(NotReady);
        if (!await IsTrueAsync(context, Catalogue(context, enrollment, complete: true), cancellationToken))
            throw new UnauthorizedAccessException(Drift);
    }

    private static string Absent(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var category = MapCategory(context);
        var links = MapLinks(context);
        string runtime = Role(enrollment.RuntimeLogin);
        return $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                WHERE a.attrelid=pg_catalog.to_regclass({Text(category.Table)}) AND acl.grantee={runtime}
                    AND acl.privilege_type IN ('INSERT','UPDATE'))
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class c CROSS JOIN LATERAL pg_catalog.aclexplode(c.relacl) acl
                WHERE c.oid=pg_catalog.to_regclass({Text(category.Table)}) AND acl.grantee={runtime}
                    AND acl.privilege_type='DELETE')
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                WHERE a.attrelid=pg_catalog.to_regclass({Text(links.Table)}) AND acl.grantee={runtime})
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class c CROSS JOIN LATERAL pg_catalog.aclexplode(c.relacl) acl
                WHERE c.oid=pg_catalog.to_regclass({Text(links.Table)}) AND acl.grantee={runtime})
            """;
    }

    private static string Catalogue(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        var sources = Sources(context);
        string runtime = Role(enrollment.RuntimeLogin);
        string global = Role(enrollment.GlobalLogin);
        string migrator = Role(enrollment.MigratorLogin);
        string sourceChecks = string.Join(" AND ", sources.Select(source =>
        {
            string shape = string.Join(" AND ", source.Columns.Select(column => $"""
                EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=t.oid AND a.attname={Text(column.Name)}
                    AND a.attnum>0 AND NOT a.attisdropped AND a.attgenerated='' AND a.attidentity=''
                    AND pg_catalog.format_type(a.atttypid,a.atttypmod)={Text(column.StoreType)}
                    AND a.attnotnull={(column.Required ? "true" : "false")})
                """));
            string[] allowedSelect = source.Type == typeof(Category) ? source.Insert : source.Read;
            string allowedColumnCheck = $"""
                ((permission.privilege='SELECT' AND a.attname=ANY({ArraySql(allowedSelect)}))
                    OR (permission.privilege='INSERT' AND a.attname=ANY({ArraySql(source.Insert)}))
                    OR (permission.privilege='UPDATE' AND a.attname=ANY({ArraySql(source.Update)})))
                """;
            string requiredGrants = complete ? string.Join(" AND ",
                source.Read.Select(column => RequiredColumnGrant(source.Table, column, "SELECT", runtime, migrator))
                .Concat(source.Insert.Select(column => RequiredColumnGrant(source.Table, column, "INSERT", runtime, migrator)))
                .Concat(source.Update.Select(column => RequiredColumnGrant(source.Table, column, "UPDATE", runtime, migrator)))
                .Append($"""
                    EXISTS (SELECT 1 FROM pg_catalog.pg_class relation CROSS JOIN LATERAL pg_catalog.aclexplode(relation.relacl) acl
                        WHERE relation.oid=pg_catalog.to_regclass({Text(source.Table)})
                            AND acl.grantee={runtime} AND acl.grantor={migrator}
                            AND acl.privilege_type='DELETE' AND NOT acl.is_grantable)
                    """)
                .Append($"pg_catalog.has_schema_privilege({runtime},n.oid,'USAGE')")) : "true";
            string deleteAllowed = $"""
                (r.oid={runtime} AND NOT pg_catalog.has_table_privilege(r.oid,t.oid,'DELETE WITH GRANT OPTION')
                    AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(t.relacl) acl
                        WHERE acl.grantee=r.oid AND acl.grantor=t.relowner AND acl.privilege_type='DELETE' AND NOT acl.is_grantable)
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(t.relacl) acl
                        WHERE acl.grantee=0 AND acl.privilege_type='DELETE'))
                """;
            return $"""
                EXISTS (SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid=t.relnamespace
                    WHERE t.oid=pg_catalog.to_regclass({Text(source.Table)}) AND t.relowner={migrator}
                        AND n.nspowner={migrator} AND t.relkind='r' AND t.relpersistence='p' AND NOT t.relispartition
                        AND (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid=t.oid AND a.attnum>0 AND NOT a.attisdropped)={source.Columns.Length}
                        AND ({shape})
                        AND ({PostgresCategoryReadPermissions.TenantPolicySql(context, source.Schema, source.Name, source.TenantColumn, enrollment.RuntimeLogin)})
                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits WHERE inhrelid=t.oid OR inhparent=t.oid)
                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite WHERE ev_class=t.oid)
                        AND NOT EXISTS (SELECT 1 FROM members r WHERE r.oid=t.relowner OR r.rolsuper OR r.rolbypassrls
                            OR r.rolcreaterole OR r.rolcreatedb OR r.rolreplication OR pg_catalog.has_schema_privilege(r.oid,n.oid,'CREATE')
                            OR pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,TRUNCATE,REFERENCES,TRIGGER')
                            OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", "t.oid", false)}
                            OR (pg_catalog.has_table_privilege(r.oid,t.oid,'DELETE') AND NOT ({deleteAllowed}))
                            OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                                CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                                WHERE a.attrelid=t.oid AND a.attnum<>0 AND NOT a.attisdropped
                                    AND (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                                        OR (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege) AND NOT (
                                            r.oid={runtime} AND a.attnum>0 AND ({allowedColumnCheck})
                                            AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                                WHERE acl.grantee=r.oid AND acl.grantor=t.relowner
                                                    AND acl.privilege_type=permission.privilege AND NOT acl.is_grantable)
                                            AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                                WHERE acl.grantee=0 AND acl.privilege_type=permission.privilege))))))
                        AND NOT EXISTS (SELECT 1 FROM global_members r
                            WHERE pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                                OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", "t.oid", false)}
                                OR pg_catalog.has_any_column_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,REFERENCES'))
                        AND ({requiredGrants}))
                """;
        }));
        return $"""
            (WITH members AS (SELECT r.* FROM pg_catalog.pg_roles r WHERE pg_catalog.pg_has_role({runtime},r.oid,'MEMBER')),
                global_members AS (SELECT r.* FROM pg_catalog.pg_roles r WHERE pg_catalog.pg_has_role({global},r.oid,'MEMBER'))
            SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE oid={runtime} AND rolcanlogin)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE roleid={runtime})
                AND NOT pg_catalog.pg_has_role({runtime},{global},'MEMBER')
                AND NOT pg_catalog.pg_has_role({global},{runtime},'MEMBER')
                AND ({sourceChecks}))
            """;
    }

    private static string RequiredColumnGrant(string table, string column, string privilege, string runtime, string migrator) => $"""
        EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
            WHERE a.attrelid=pg_catalog.to_regclass({Text(table)}) AND a.attname={Text(column)}
                AND acl.grantee={runtime} AND acl.grantor={migrator}
                AND acl.privilege_type={Text(privilege)} AND NOT acl.is_grantable)
        """;

    private static Source[] Sources(ExploreDbContext context) => [MapCategory(context), MapLinks(context)];

    private static Source MapCategory(ExploreDbContext context) =>
        MapEntity(context, typeof(Category), CategoryInsertProperties, [], CategoryInsertProperties, CategoryUpdateProperties, nameof(Category.TenantId));

    private static Source MapLinks(ExploreDbContext context) =>
        MapEntity(context, typeof(CategoryTypeCategories), LinkAllProperties, LinkAllProperties, LinkAllProperties, LinkUpdateProperties, nameof(CategoryTypeCategories.TenantId));

    private static Source MapEntity(ExploreDbContext context, Type type, string[] allProperties,
        string[] readProperties, string[] insertProperties, string[] updateProperties, string tenantProperty)
    {
        var entity = context.Model.FindEntityType(type) ?? throw new InvalidOperationException(Drift);
        if (entity.BaseType is not null || entity.GetDerivedTypes().Any() || entity.GetComplexProperties().Any()
            || entity.GetSkipNavigations().Any() || entity.GetNavigations().Any(navigation => navigation.IsEagerLoaded)
            || entity.FindPrimaryKey()?.Properties is not [var key] || key.Name != "Id"
            || !entity.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(allProperties.Order(StringComparer.Ordinal)))
            throw new InvalidOperationException(Drift);
        string name = entity.GetTableName() ?? throw new InvalidOperationException(Drift);
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? entity.GetSchema() ?? "public";
        var store = StoreObjectIdentifier.Table(name, entity.GetSchema());
        string ColumnName(string propertyName) =>
            entity.FindProperty(propertyName)?.GetColumnName(store) ?? throw new InvalidOperationException(Drift);
        var columns = allProperties.Select(propertyName =>
        {
            var property = entity.FindProperty(propertyName) ?? throw new InvalidOperationException(Drift);
            if (property.IsShadowProperty()) throw new InvalidOperationException(Drift);
            return new MappedColumn(propertyName, ColumnName(propertyName),
                property.GetRelationalTypeMapping().StoreType, !property.IsNullable);
        }).ToArray();
        return new Source(type, schema, name,
            context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name, schema),
            ColumnName(tenantProperty), columns,
            readProperties.Select(ColumnName).ToArray(),
            insertProperties.Select(ColumnName).ToArray(),
            updateProperties.Select(ColumnName).ToArray());
    }

    private static string ArraySql(string[] values) =>
        values.Length == 0 ? "ARRAY[]::text[]" : "ARRAY[" + string.Join(',', values.Select(Text)) + "]::text[]";

    private static string Identity(string login) => PostgresGlobalReadPermissionsHelper.IdentitySql(login);
    private static string Role(string login) => $"(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(login)})";
    private static string Text(string value) => PostgresGlobalReadPermissionsHelper.Text(value);
    private static Task<bool> IsTrueAsync(ExploreDbContext context, string predicate, CancellationToken token) =>
        PostgresGlobalReadPermissionsHelper.IsTrueAsync(context, predicate, token);

    private static async Task ExecuteAsync(ExploreDbContext context, string sql, CancellationToken token)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(token);
    }

    private sealed record MappedColumn(string Property, string Name, string StoreType, bool Required);
    private sealed record Source(Type Type, string Schema, string Name, string Table, string TenantColumn,
        MappedColumn[] Columns, string[] Read, string[] Insert, string[] Update);
}
