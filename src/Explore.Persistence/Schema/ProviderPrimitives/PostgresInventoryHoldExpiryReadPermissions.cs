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

/// <summary>Fixed hold-expiry identity metadata reads; no order content or inventory mutation authority.</summary>
public static class PostgresInventoryHoldExpiryReadPermissions
{
    private const string Policy = "hold_expiry_metadata_global_read";
    private const string Drift = "hold_expiry_read_permissions_drift";
    private static readonly (Type Type, string[] Properties)[] Inventory =
    [
        (typeof(RegistrationInventoryHold), [nameof(RegistrationInventoryHold.Id), nameof(RegistrationInventoryHold.TenantId),
            nameof(RegistrationInventoryHold.RegistrationOrderId), nameof(RegistrationInventoryHold.RegistrationInventoryHoldStatusId),
            nameof(RegistrationInventoryHold.ExpiresAt), nameof(RegistrationInventoryHold.IsDeleted)]),
        (typeof(RegistrationOrder), [nameof(RegistrationOrder.Id), nameof(RegistrationOrder.TenantId),
            nameof(RegistrationOrder.RegistrationOrderStatusId), nameof(RegistrationOrder.UpdatedAt), nameof(RegistrationOrder.IsDeleted)])
    ];

    /// <summary>Pure recognition of the two fixed metadata SELECT policies, not composed admission.</summary>
    internal static string GlobalPolicyException(ExploreDbContext context, string login) =>
        "(" + string.Join(" OR ", Sources(context).Select(source =>
            $"(session_user={Text(login)} AND current_user=session_user AND n.nspname={Text(source.Schema)} AND c.relname={Text(source.Name)} AND ({PolicyPredicate(source, login)}))")) + ")";

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        var sources = Sources(context);
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        var transaction = context.Database.CurrentTransaction!;
        var connection = RelationalConnectionIdentity.Capture(context);
        var physical = transaction.GetDbTransaction();
        var contextId = context.ContextId;
        int? backend = connection.BackendProcessId;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change)
        {
            if (change.CurrentState != ConnectionState.Open) interrupted = true;
        }
        void RequireOriginal()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context.ContextId != contextId || interrupted || !connection.Matches(context)
                || connection.State != ConnectionState.Open || connection.BackendProcessId != backend
                || !ReferenceEquals(physical, context.Database.CurrentTransaction?.GetDbTransaction())
                || !connection.Owns(physical))
                throw new UnauthorizedAccessException("hold_expiry_installer_owner_changed");
        }
        connection.StateChange += Changed;
        string savepoint = "hold_expiry_permissions_" + Guid.CreateVersion7().ToString("N");
        try
        {
            RequireOriginal();
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            try
            {
                if (!await IsTrueAsync(context, Identity(enrollment.MigratorLogin), cancellationToken))
                    throw new UnauthorizedAccessException("hold_expiry_installer_wrong_identity");
                RequireOriginal();
                if (!await IsTrueAsync(context, NoObsoleteInstallation(sources), cancellationToken))
                    throw new InvalidOperationException("hold_expiry_requires_fresh_development_cluster");
                RequireOriginal();
                string fresh = Fresh(sources, enrollment.GlobalLogin);
                string complete = Manifest(sources, enrollment, complete: true);
                if (!await IsTrueAsync(context, $"({Manifest(sources, enrollment, complete: false)}) AND (({fresh}) OR ({complete}))", cancellationToken))
                    throw new InvalidOperationException(Drift);
                RequireOriginal();
                if (await IsTrueAsync(context, fresh, cancellationToken))
                {
                    RequireOriginal();
                    var names = context.GetService<ISqlGenerationHelper>();
                    foreach (var source in sources)
                    {
                        await ExecuteAsync(context, $"""
                            GRANT USAGE ON SCHEMA {names.DelimitIdentifier(source.Schema)} TO {names.DelimitIdentifier(enrollment.GlobalLogin)};
                            GRANT SELECT ({string.Join(',', source.ReadColumns.Select(column => names.DelimitIdentifier(column.Name)))})
                                ON {source.Table} TO {names.DelimitIdentifier(enrollment.GlobalLogin)};
                            CREATE POLICY {names.DelimitIdentifier(Policy)} ON {source.Table} FOR SELECT
                                TO {names.DelimitIdentifier(enrollment.GlobalLogin)} USING (true);
                            """, cancellationToken);
                        RequireOriginal();
                    }
                    if (!await IsTrueAsync(context, complete, cancellationToken))
                        throw new InvalidOperationException(Drift);
                }
                RequireOriginal();
            }
            catch
            {
                await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
                throw;
            }
            finally { await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None); }
            RequireOriginal();
            if (owned is not null) await owned.CommitAsync(cancellationToken);
        }
        finally { connection.StateChange -= Changed; }
    }

    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        if (enrollment is null) throw new UnauthorizedAccessException("hold_expiry_enrollment_required");
        enrollment.Validate();
        context.RequireBoundTenantLease();
        var connection = RelationalConnectionIdentity.Capture(context);
        var physical = context.Database.CurrentTransaction?.GetDbTransaction();
        var contextId = context.ContextId;
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
            if (context.ContextId != contextId || context.TenantFilterTenantId is not null
                || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
                || interrupted || !connection.Matches(context) || connection.State != ConnectionState.Open
                || connection.BackendProcessId != backend
                || !ReferenceEquals(physical, context.Database.CurrentTransaction?.GetDbTransaction())
                || physical is not null && !connection.Owns(physical))
                throw new UnauthorizedAccessException("hold_expiry_original_owner_changed");
        }
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            if (!await IsTrueAsync(context, Identity(enrollment.GlobalLogin)
                + " AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NULL", cancellationToken))
                throw new UnauthorizedAccessException("hold_expiry_read_permissions_not_ready");
            RequireOriginal();
            var sources = Sources(context);
            bool ready = await IsTrueAsync(context,
                $"({NoObsoleteInstallation(sources)}) AND ({Manifest(sources, enrollment, complete: true)})", cancellationToken);
            RequireOriginal();
            if (!ready) throw new UnauthorizedAccessException("hold_expiry_read_permissions_not_ready");
        }
        finally { connection.StateChange -= Changed; }
    }

    internal static string CompleteGlobalCataloguePredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sources = Sources(context);
        return $"({NoObsoleteInstallation(sources)}) AND ({Manifest(sources, enrollment, complete: true)})";
    }

    internal static IReadOnlyList<string> GlobalCatalogueRelations(ExploreDbContext context) =>
        Sources(context).Select(source => source.Table).ToArray();

    private static string Manifest(Source[] sources, PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        string global = Text(enrollment.GlobalLogin), runtime = Text(enrollment.RuntimeLogin);
        string tables = "ARRAY[" + string.Join(',', sources.Select(source => Text(source.Table))) + "]::text[]";
        string expected = "VALUES " + string.Join(',', sources.SelectMany(source => source.ReadColumns.Select(column =>
            $"({Text(source.Table)},{Text(column.Name)})")));
        string required = complete ? string.Join(" AND ", sources.Select(source =>
            string.Join(" AND ", source.ReadColumns.Select(column => $"""
                EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                    WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND a.attname={Text(column.Name)}
                        AND acl.grantee={RoleOid(enrollment.GlobalLogin)} AND acl.grantor={RoleOid(enrollment.MigratorLogin)}
                        AND acl.privilege_type='SELECT' AND NOT acl.is_grantable)
                """)) + $" AND pg_catalog.has_schema_privilege({global},{Text(source.Schema)},'USAGE')")) : "true";
        return $"""
            (WITH expected(relation,column_name) AS ({expected}), members AS
                (SELECT r.* FROM pg_catalog.pg_roles r WHERE pg_catalog.pg_has_role({RoleOid(enrollment.GlobalLogin)},r.oid,'MEMBER'))
            SELECT (SELECT count(*)=2 FROM pg_catalog.pg_roles WHERE rolname IN ({global},{runtime}) AND rolcanlogin)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE roleid={RoleOid(enrollment.GlobalLogin)})
                AND NOT pg_catalog.pg_has_role({RoleOid(enrollment.GlobalLogin)},{RoleOid(enrollment.RuntimeLogin)},'MEMBER')
                AND NOT pg_catalog.pg_has_role({RoleOid(enrollment.RuntimeLogin)},{RoleOid(enrollment.GlobalLogin)},'MEMBER')
                AND NOT EXISTS (SELECT 1 FROM members r
                    WHERE r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                        OR EXISTS (SELECT 1 FROM unnest(ARRAY[{string.Join(',', sources.Select(source => Text(source.Schema)).Distinct())}]::text[]) s(name)
                            WHERE pg_catalog.has_schema_privilege(r.oid,s.name,'CREATE')))
                AND NOT EXISTS (SELECT 1 FROM unnest({tables}) source(relation)
                    JOIN pg_catalog.pg_class t ON t.oid=pg_catalog.to_regclass(source.relation) CROSS JOIN members r
                    WHERE t.relowner=r.oid OR (pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                        OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", "t.oid", false)})
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                            CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                            WHERE a.attrelid=t.oid AND a.attnum<>0 AND NOT a.attisdropped
                                AND (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                                    OR (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege) AND NOT (
                                        a.attnum>0 AND r.rolname={global} AND permission.privilege='SELECT'
                                        AND EXISTS (SELECT 1 FROM expected e WHERE e.relation=source.relation AND e.column_name=a.attname)
                                        AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl WHERE acl.grantee=r.oid
                                            AND acl.grantor=t.relowner AND acl.privilege_type='SELECT' AND NOT acl.is_grantable)
                                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl WHERE acl.grantee=0 AND acl.privilege_type='SELECT'))))))
                AND ({required}) AND ({string.Join(" AND ", sources.Select(source => SourcePredicate(source, enrollment, complete)))}))
            """;
    }

    private static string SourcePredicate(Source source, PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        string columns = string.Join(" AND ", source.ReadColumns.Select(column => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=c.oid AND a.attname={Text(column.Name)}
                AND a.attnum>0 AND NOT a.attisdropped AND a.attidentity='' AND a.attgenerated=''
                AND pg_catalog.format_type(a.atttypid,a.atttypmod)={Text(column.StoreType)} AND a.attnotnull={(column.Required ? "true" : "false")})
            """));
        string ownPolicy = PolicyPredicate(source, enrollment.GlobalLogin);
        return $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                WHERE c.oid=pg_catalog.to_regclass({Text(source.Table)}) AND c.relkind='r' AND c.relpersistence='p' AND NOT c.relispartition
                    AND c.relowner={RoleOid(enrollment.MigratorLogin)} AND n.nspname={Text(source.Schema)}
                    AND n.nspowner={RoleOid(enrollment.MigratorLogin)}
                    AND c.relrowsecurity AND c.relforcerowsecurity AND ({columns})
                    AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=c.oid
                        AND p.polname='tenant_isolation' AND p.polcmd='*' AND p.polpermissive
                        AND p.polroles=ARRAY[0::oid] AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                        AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(source.TenantMetadata, source.Schema))}
                            || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=c.oid
                        AND (p.polname={Text(Policy)} OR (p.polname<>'tenant_isolation'
                            AND EXISTS (SELECT 1 FROM unnest(p.polroles) role_id
                                WHERE role_id=0 OR pg_catalog.pg_has_role({RoleOid(enrollment.GlobalLogin)},role_id,'MEMBER'))))
                        AND NOT COALESCE(({ownPolicy}),false))
                    {(complete ? $"AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE {ownPolicy})" : "")}
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i WHERE i.inhrelid=c.oid OR i.inhparent=c.oid)
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class=c.oid))
            """;
    }

    private static string Fresh(Source[] sources, string global) => string.Join(" AND ", sources.Select(source => $"""
        NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
            WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND acl.grantee={RoleOid(global)})
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(source.Table)})
            AND p.polname={Text(Policy)})
        """));

    private static string PolicyPredicate(Source source, string login) => $"""
        p.polrelid=pg_catalog.to_regclass({Text(source.Table)}) AND p.polname={Text(Policy)}
        AND p.polcmd='r' AND p.polpermissive AND p.polroles=ARRAY[{RoleOid(login)}]
        AND pg_catalog.pg_get_expr(p.polqual,p.polrelid)='true' AND p.polwithcheck IS NULL
        """;

    private static string NoObsoleteInstallation(Source[] sources) => $"""
        NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN
            ('hold_expiry_discovery_owner','hold_expiry_discovery_migrator','hold_expiry_discovery_executor'))
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace
            WHERE n.nspname={Text(sources[0].Schema)} AND p.proname='inventory_hold_expiry_targets')
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid IN ({string.Join(',', sources.Select(source => $"pg_catalog.to_regclass({Text(source.Table)})"))})
            AND p.polname='hold_expiry_identity_discovery')
        """;

    private static Source[] Sources(ExploreDbContext context)
    {
        var names = context.GetService<ISqlGenerationHelper>();
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        return Inventory.Select(item =>
        {
            var entity = context.Model.FindEntityType(item.Type) ?? throw new InvalidOperationException("hold_expiry_model_missing");
            string table = entity.GetTableName() ?? throw new InvalidOperationException("hold_expiry_table_missing");
            string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
                ?? entity.GetSchema() ?? "public";
            var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
            if (entity.BaseType is not null || entity.GetDerivedTypes().Any() || entity.GetComplexProperties().Any()
                || entity.FindPrimaryKey()?.Properties is not [var key] || key.Name != "Id")
                throw new InvalidOperationException("hold_expiry_model_shape_drift");
            var metadata = tenantTables.SingleOrDefault(value => value.TableName == table && (value.Schema ?? schema) == schema);
            if (metadata is null || metadata.AllowsNullTenant || metadata.ParentJoin is not null)
                throw new InvalidOperationException("hold_expiry_tenant_shape_drift");
            var columns = item.Properties.Select(propertyName =>
            {
                var property = entity.FindProperty(propertyName) ?? throw new InvalidOperationException("hold_expiry_property_missing");
                if (property.IsShadowProperty()) throw new InvalidOperationException("hold_expiry_property_shape_drift");
                return new Column(property.GetColumnName(store)!, property.GetRelationalTypeMapping().StoreType, !property.IsNullable);
            }).ToArray();
            return new Source(schema, table, names.DelimitIdentifier(table, schema), columns, metadata);
        }).ToArray();
    }

    private static string Identity(string login) => $"session_user={Text(login)} AND current_user=session_user AND pg_catalog.current_setting('role') IN ('none',session_user::text)";
    private static string RoleOid(string login) => $"(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(login)})";
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

    private sealed record Column(string Name, string StoreType, bool Required);
    private sealed record Source(string Schema, string Name, string Table, Column[] ReadColumns, TenantTableMetadata TenantMetadata);
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
