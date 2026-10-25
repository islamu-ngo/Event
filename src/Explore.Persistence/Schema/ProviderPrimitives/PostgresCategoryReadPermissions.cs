using System.Data;
using System.Runtime.ExceptionServices;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

/// <summary>Enrolls only the mapped tenant-category columns needed by ordinary Runtime EF reads.</summary>
public static class PostgresCategoryReadPermissions
{
    private const string Drift = "category_read_permissions_drift";
    private static readonly string[] Properties =
    [
        nameof(Category.Id), nameof(Category.ConcurrencyStamp), nameof(Category.MasterCode),
        nameof(Category.FullName), nameof(Category.ParentId), nameof(Category.TenantId)
    ];

    /// <summary>Recognizes this owner's direct, non-grantable Category reads in another native source guard.</summary>
    internal static string RuntimeReadPredicate(ExploreDbContext context, Type type, string runtimeLogin,
        string roleOid, string relationOid, string columnName, string privilege)
    {
        if (type != typeof(Category)) return "false";
        var source = Mapping(context);
        return $"""
            ({roleOid}={Role(runtimeLogin)} AND {relationOid}=pg_catalog.to_regclass({Text(source.Table)})
                AND {privilege}='SELECT' AND {columnName}=ANY(ARRAY[{string.Join(',', source.Columns.Select(column => Text(column.Name)))}]::text[])
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE roleid={Role(runtimeLogin)})
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_attribute column_info
                    CROSS JOIN LATERAL pg_catalog.aclexplode(column_info.attacl) acl
                    JOIN pg_catalog.pg_class relation ON relation.oid=column_info.attrelid
                    WHERE column_info.attrelid={relationOid} AND column_info.attname={columnName} AND column_info.attnum>0
                        AND acl.grantee={roleOid} AND acl.grantor=relation.relowner
                        AND acl.privilege_type='SELECT' AND NOT acl.is_grantable)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute column_info
                    CROSS JOIN LATERAL pg_catalog.aclexplode(column_info.attacl) acl
                    WHERE column_info.attrelid={relationOid} AND column_info.attname={columnName}
                        AND acl.grantee=0 AND acl.privilege_type='SELECT'))
            """;
    }

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        var source = Mapping(context);
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
        IDbContextTransaction? owned = null;
        Exception? primary = null;
        try
        {
            try
            {
                var original = context.Database.CurrentTransaction?.GetDbTransaction();
                owned = original is null
                    ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
                var transaction = context.Database.CurrentTransaction
                    ?? throw new UnauthorizedAccessException("category_read_installer_owner_changed");
                var physical = transaction.GetDbTransaction();
                int? backend = connection.BackendProcessId;
                void RequireConnection()
                {
                    context.RequireBoundTenantLease();
                    if (context.ContextId != contextId || interrupted || !connection.Matches(context)
                        || connection.State != ConnectionState.Open || connection.BackendProcessId != backend
                        || !ReferenceEquals(tenantContext, context.TenantContext) || context.TenantFilterTenantId != tenant
                        || context.IsTenantFilterBypassed != bypass)
                        throw new UnauthorizedAccessException("category_read_installer_owner_changed");
                }
                void RequireOriginal()
                {
                    RequireConnection();
                    if (!ReferenceEquals(physical, context.Database.CurrentTransaction?.GetDbTransaction())
                        || !connection.Owns(physical) || original is not null && !ReferenceEquals(original, physical))
                        throw new UnauthorizedAccessException("category_read_installer_owner_changed");
                }

                RequireOriginal();
                string savepoint = "category_read_permissions_" + Guid.CreateVersion7().ToString("N");
                bool created = false;
                bool released = false;
                try
                {
                    await transaction.CreateSavepointAsync(savepoint, cancellationToken);
                    created = true;
                    RequireOriginal();
                    if (!await IsTrueAsync(context, Identity(enrollment.MigratorLogin), cancellationToken))
                        throw new UnauthorizedAccessException("category_read_installer_wrong_identity");
                    RequireOriginal();
                    string complete = Catalogue(context, source, enrollment, complete: true);
                    string fresh = $"""
                        NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                            CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                            WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND acl.grantee={Role(enrollment.RuntimeLogin)})
                        """;
                    if (!await IsTrueAsync(context,
                        $"({Catalogue(context, source, enrollment, complete: false)}) AND (({fresh}) OR ({complete}))", cancellationToken))
                        throw new InvalidOperationException(Drift);
                    RequireOriginal();
                    if (await IsTrueAsync(context, fresh, cancellationToken))
                    {
                        RequireOriginal();
                        var names = context.GetService<ISqlGenerationHelper>();
                        await ExecuteAsync(context, $"""
                            GRANT USAGE ON SCHEMA {names.DelimitIdentifier(source.Schema)} TO {names.DelimitIdentifier(enrollment.RuntimeLogin)};
                            GRANT SELECT ({string.Join(',', source.Columns.Select(column => names.DelimitIdentifier(column.Name)))})
                                ON {source.Table} TO {names.DelimitIdentifier(enrollment.RuntimeLogin)}
                            """, cancellationToken);
                        RequireOriginal();
                    }
                    if (!await IsTrueAsync(context, $"({Identity(enrollment.MigratorLogin)}) AND ({complete})", cancellationToken))
                        throw new InvalidOperationException(Drift);
                    RequireOriginal();
                    cancellationToken.ThrowIfCancellationRequested();
                    await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None);
                    released = true;
                    RequireOriginal();
                    cancellationToken.ThrowIfCancellationRequested();
                    if (owned is not null)
                        await owned.CommitAsync(cancellationToken);
                }
                catch (Exception failure)
                {
                    var failures = new List<Exception> { failure };
                    bool restored = false;
                    if (created && !released)
                    {
                        try
                        {
                            await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
                            await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None);
                            restored = true;
                        }
                        catch (Exception cleanup) { failures.Add(cleanup); }
                    }
                    // A released or uncertain savepoint cannot protect the caller from committing denied grants.
                    if (!restored)
                    {
                        try { await physical.RollbackAsync(CancellationToken.None); }
                        catch (Exception cleanup)
                        {
                            failures.Add(cleanup);
                            try { await connection.Connection.CloseAsync(); }
                            catch (Exception close) { failures.Add(close); }
                        }
                    }
                    if (failures.Count > 1)
                        throw new AggregateException("category_read_installer_cleanup_failed", failures);
                    throw;
                }
            }
            catch (Exception failure) { primary = failure; }
            try
            {
                if (owned is not null) await owned.DisposeAsync();
            }
            catch (Exception cleanup)
            {
                if (primary is not null)
                    throw new AggregateException("category_read_installer_disposal_failed", primary, cleanup);
                throw;
            }
            if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
        }
        finally { connection.StateChange -= Changed; }
    }

    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        if (enrollment is null) throw new UnauthorizedAccessException("category_read_enrollment_required");
        enrollment.Validate();
        var source = Mapping(context);
        context.RequireBoundTenantLease();
        var connection = RelationalConnectionIdentity.Capture(context);
        var contextId = context.ContextId;
        var tenantContext = context.TenantContext;
        Guid? tenant = context.TenantFilterTenantId;
        var physical = context.Database.CurrentTransaction?.GetDbTransaction();
        if (connection.State != ConnectionState.Open)
            await context.Database.OpenConnectionAsync(cancellationToken);
        int? backend = connection.BackendProcessId;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change)
        {
            if (change.CurrentState != ConnectionState.Open) interrupted = true;
        }
        void RequireOriginal()
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.RequireBoundTenantLease();
            if (tenant is null || context.TenantFilterTenantId != tenant || context.IsTenantFilterBypassed
                || !ReferenceEquals(tenantContext, context.TenantContext) || context.ContextId != contextId
                || interrupted || !connection.Matches(context) || connection.State != ConnectionState.Open
                || connection.BackendProcessId != backend
                || !ReferenceEquals(physical, context.Database.CurrentTransaction?.GetDbTransaction())
                || physical is not null && !connection.Owns(physical))
                throw new UnauthorizedAccessException("category_read_original_owner_changed");
        }
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            if (!await IsTrueAsync(context,
                $"({Identity(enrollment.RuntimeLogin)}) AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')={Text(tenant!.Value.ToString("D"))}", cancellationToken))
                throw new UnauthorizedAccessException("category_read_wrong_identity");
            RequireOriginal();
            bool ready = await IsTrueAsync(context, Catalogue(context, source, enrollment, complete: true), cancellationToken);
            RequireOriginal();
            if (!ready) throw new UnauthorizedAccessException(Drift);
        }
        finally { connection.StateChange -= Changed; }
    }

    private static string Catalogue(ExploreDbContext context, Source source, PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        string runtime = Role(enrollment.RuntimeLogin);
        string global = Role(enrollment.GlobalLogin);
        string shape = string.Join(" AND ", source.Columns.Select(column => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=t.oid AND a.attname={Text(column.Name)}
                AND a.attnum>0 AND NOT a.attisdropped AND a.attgenerated='' AND a.attidentity=''
                AND pg_catalog.format_type(a.atttypid,a.atttypmod)={Text(column.StoreType)}
                AND a.attnotnull={(column.Required ? "true" : "false")})
            """));
        string required = complete ? string.Join(" AND ", source.Columns.Select(column => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND a.attname={Text(column.Name)}
                    AND acl.grantee={runtime} AND acl.grantor={Role(enrollment.MigratorLogin)}
                    AND acl.privilege_type='SELECT' AND NOT acl.is_grantable)
            """)) : "true";
        return $"""
            (WITH members AS (SELECT r.* FROM pg_catalog.pg_roles r WHERE pg_catalog.pg_has_role({runtime},r.oid,'MEMBER')),
                global_members AS (SELECT r.* FROM pg_catalog.pg_roles r WHERE pg_catalog.pg_has_role({global},r.oid,'MEMBER'))
            SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE oid={runtime} AND rolcanlogin)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE roleid={runtime})
                AND NOT pg_catalog.pg_has_role({runtime},{global},'MEMBER')
                AND NOT pg_catalog.pg_has_role({global},{runtime},'MEMBER')
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid=t.relnamespace
                    WHERE t.oid=pg_catalog.to_regclass({Text(source.Table)}) AND t.relowner={Role(enrollment.MigratorLogin)}
                        AND n.nspowner={Role(enrollment.MigratorLogin)} AND t.relkind='r' AND t.relpersistence='p' AND NOT t.relispartition
                        AND (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid=t.oid AND a.attnum>0 AND NOT a.attisdropped)=6
                        AND ({shape}) AND ({TenantPolicy(context, source, enrollment.RuntimeLogin)})
                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits WHERE inhrelid=t.oid OR inhparent=t.oid)
                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite WHERE ev_class=t.oid)
                        AND NOT EXISTS (SELECT 1 FROM members r WHERE r.oid=t.relowner OR r.rolsuper OR r.rolbypassrls
                            OR r.rolcreaterole OR r.rolcreatedb OR r.rolreplication OR pg_catalog.has_schema_privilege(r.oid,n.oid,'CREATE')
                            OR (pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                                OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", "t.oid", false)})
                            OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                                CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                                WHERE a.attrelid=t.oid AND a.attnum<>0 AND NOT a.attisdropped
                                    AND (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                                        OR (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege) AND NOT (
                                            r.oid={runtime} AND a.attnum>0 AND permission.privilege='SELECT'
                                            AND a.attname=ANY(ARRAY[{string.Join(',', source.Columns.Select(column => Text(column.Name)))}]::text[])
                                            AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                                WHERE acl.grantee=r.oid AND acl.grantor=t.relowner
                                                    AND acl.privilege_type='SELECT' AND NOT acl.is_grantable)
                                            AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                                WHERE acl.grantee=0 AND acl.privilege_type='SELECT'))))))
                        AND NOT EXISTS (SELECT 1 FROM global_members r
                            WHERE (pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                                OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", "t.oid", false)})
                                OR pg_catalog.has_any_column_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,REFERENCES'))
                        {(complete ? $"AND pg_catalog.has_schema_privilege({runtime},n.oid,'USAGE')" : "")})
                AND ({required}))
            """;
    }

    private static string TenantPolicy(ExploreDbContext context, Source source, string runtimeLogin)
    {
        var metadata = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
            .Single(table => table.TableName == source.Name && (table.Schema ?? source.Schema) == source.Schema);
        if (metadata.AllowsNullTenant || metadata.ParentJoin is not null
            || metadata.TenantIdColumn != source.Columns.Single(column => column.Property == nameof(Category.TenantId)).Name)
            throw new InvalidOperationException(Drift);
        string column = metadata.TenantIdColumn ?? throw new InvalidOperationException(Drift);
        string condition = $"({Text("(")} || pg_catalog.quote_ident({Text(column)}) || {Text(" = (NULLIF(current_setting('app.current_tenant_id'::text, true), ''::text))::uuid)")})";
        return $"""
            t.relrowsecurity AND t.relforcerowsecurity
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=t.oid AND p.polname='tenant_isolation'
                AND p.polcmd='*' AND p.polpermissive AND p.polroles=ARRAY[0::oid]
                AND pg_catalog.pg_get_expr(p.polqual,p.polrelid)={condition}
                AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid)={condition}
                AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(metadata, source.Schema))}
                    || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_depend dependency
                    WHERE dependency.classid='pg_catalog.pg_policy'::regclass AND dependency.objid=p.oid
                        AND ((dependency.refclassid='pg_catalog.pg_proc'::regclass AND NOT EXISTS (
                            SELECT 1 FROM pg_catalog.pg_proc routine WHERE routine.oid=dependency.refobjid
                                AND routine.pronamespace='pg_catalog'::regnamespace))
                        OR (dependency.refclassid='pg_catalog.pg_operator'::regclass AND NOT EXISTS (
                            SELECT 1 FROM pg_catalog.pg_operator operation WHERE operation.oid=dependency.refobjid
                                AND operation.oprnamespace='pg_catalog'::regnamespace)))))
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=t.oid AND p.polname<>'tenant_isolation'
                AND (0::oid=ANY(p.polroles) OR EXISTS (SELECT 1 FROM unnest(p.polroles) role_id
                    WHERE pg_catalog.pg_has_role({Role(runtimeLogin)},role_id,'MEMBER'))))
            """;
    }

    private static Source Mapping(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(Category)) ?? throw new InvalidOperationException(Drift);
        if (entity.BaseType is not null || entity.GetDerivedTypes().Any() || entity.GetComplexProperties().Any()
            || entity.GetSkipNavigations().Any() || entity.GetNavigations().Any(navigation => navigation.IsEagerLoaded)
            || !entity.GetNavigations().Select(navigation => navigation.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(new[] { nameof(Category.Parent), nameof(Category.Tenant) }.Order(StringComparer.Ordinal))
            || entity.FindPrimaryKey()?.Properties is not [var key] || key.Name != nameof(Category.Id)
            || !entity.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(Properties.Order(StringComparer.Ordinal))
            || entity.FindProperty(nameof(Category.ConcurrencyStamp))?.IsConcurrencyToken != true)
            throw new InvalidOperationException(Drift);
        var foreignKeys = entity.GetForeignKeys().ToArray();
        if (foreignKeys.Length != 2
            || !foreignKeys.Any(foreign => foreign.PrincipalEntityType.ClrType == typeof(Category)
                && foreign.Properties.Select(property => property.Name).SequenceEqual([nameof(Category.TenantId), nameof(Category.ParentId)])
                && foreign.PrincipalKey.Properties.Select(property => property.Name).SequenceEqual([nameof(Category.TenantId), nameof(Category.Id)])
                && foreign.DeleteBehavior == DeleteBehavior.Restrict)
            || !foreignKeys.Any(foreign => foreign.PrincipalEntityType.ClrType == typeof(Tenant) && foreign.IsRequired
                && foreign.Properties.Select(property => property.Name).SequenceEqual([nameof(Category.TenantId)])
                && foreign.PrincipalKey.Properties.Select(property => property.Name).SequenceEqual([nameof(Tenant.Id)])
                && foreign.DeleteBehavior == DeleteBehavior.Restrict))
            throw new InvalidOperationException(Drift);
        string name = entity.GetTableName() ?? throw new InvalidOperationException(Drift);
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? entity.GetSchema() ?? "public";
        var columns = Properties.Select(propertyName =>
        {
            var property = entity.FindProperty(propertyName) ?? throw new InvalidOperationException(Drift);
            Type expected = propertyName switch
            {
                nameof(Category.MasterCode) or nameof(Category.FullName) => typeof(string),
                nameof(Category.ParentId) => typeof(Guid?),
                _ => typeof(Guid)
            };
            if (property.IsShadowProperty() || property.ClrType != expected
                || property.IsNullable != (propertyName == nameof(Category.ParentId)))
                throw new InvalidOperationException(Drift);
            string column = property.GetColumnName(StoreObjectIdentifier.Table(name, entity.GetSchema()))
                ?? throw new InvalidOperationException(Drift);
            return new MappedColumn(propertyName, column, property.GetRelationalTypeMapping().StoreType, !property.IsNullable);
        }).ToArray();
        return new Source(schema, name, context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name, schema), columns);
    }

    private static string Identity(string login) => $"session_user={Text(login)} AND current_user=session_user AND pg_catalog.current_setting('role') IN ('none',session_user::text)";
    private static string Role(string login) => $"(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(login)})";
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static async Task<bool> IsTrueAsync(ExploreDbContext context, string predicate, CancellationToken cancellationToken)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT ({predicate})";
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task ExecuteAsync(ExploreDbContext context, string sql, CancellationToken cancellationToken)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record MappedColumn(string Property, string Name, string StoreType, bool Required);
    private sealed record Source(string Schema, string Name, string Table, MappedColumn[] Columns);
}
