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

/// <summary>Fixed Global recovery metadata reads; no identity evidence or enrollment mutation authority.</summary>
public static class PostgresVisitorEnrollmentRecoveryReadPermissions
{
    private const string Policy = "visitor_enrollment_recovery_metadata_global_read";
    private const string Drift = "visitor_enrollment_recovery_read_permissions_drift";
    private static readonly string[] ReadProperties =
    [
        nameof(VisitorEnrollmentOperation.Id), nameof(VisitorEnrollmentOperation.TenantId),
        nameof(VisitorEnrollmentOperation.Stage), nameof(VisitorEnrollmentOperation.Generation),
        nameof(VisitorEnrollmentOperation.ExpiresAt), nameof(VisitorEnrollmentOperation.CleanupCompletedAt)
    ];

    /// <summary>Recognizes only this fixed SELECT policy, without composed application admission.</summary>
    internal static string GlobalPolicyException(ExploreDbContext context, string login)
    {
        var source = Source(context);
        return $"(session_user={Text(login)} AND current_user=session_user AND n.nspname={Text(source.Schema)} AND c.relname={Text(source.Name)} AND ({PolicyPredicate(source, login)}))";
    }

    internal static string CompleteGlobalCataloguePredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        Manifest(Source(context), enrollment, complete: true);

    internal static IReadOnlyList<string> GlobalCatalogueRelations(ExploreDbContext context) =>
        [Source(context).Table];

    /// <summary>Migrator-only, atomic installation from absence; partial or unknown profiles are rejected.</summary>
    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        var source = Source(context);
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
                throw new UnauthorizedAccessException("visitor_recovery_installer_owner_changed");
        }
        connection.StateChange += Changed;
        string savepoint = "visitor_recovery_permissions_" + Guid.CreateVersion7().ToString("N");
        try
        {
            RequireOriginal();
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            try
            {
                if (!await IsTrueAsync(context, Identity(enrollment.MigratorLogin), cancellationToken))
                    throw new UnauthorizedAccessException("visitor_recovery_installer_wrong_identity");
                RequireOriginal();
                string fresh = Fresh(source, enrollment.GlobalLogin);
                string complete = Manifest(source, enrollment, complete: true);
                if (!await IsTrueAsync(context,
                    $"({Manifest(source, enrollment, complete: false)}) AND (({fresh}) OR ({complete}))", cancellationToken))
                    throw new InvalidOperationException(Drift);
                RequireOriginal();
                if (await IsTrueAsync(context, fresh, cancellationToken))
                {
                    RequireOriginal();
                    var names = context.GetService<ISqlGenerationHelper>();
                    await ExecuteAsync(context, $"""
                        GRANT USAGE ON SCHEMA {names.DelimitIdentifier(source.Schema)} TO {names.DelimitIdentifier(enrollment.GlobalLogin)};
                        GRANT SELECT ({string.Join(',', source.ReadColumns.Select(column => names.DelimitIdentifier(column.Name)))})
                            ON {source.Table} TO {names.DelimitIdentifier(enrollment.GlobalLogin)};
                        CREATE POLICY {names.DelimitIdentifier(Policy)} ON {source.Table} FOR SELECT
                            TO {names.DelimitIdentifier(enrollment.GlobalLogin)} USING (true);
                        """, cancellationToken);
                    RequireOriginal();
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

    /// <summary>Read-only admission on the original Global connector and its identity-free tenant lease.</summary>
    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        if (enrollment is null) throw new UnauthorizedAccessException("visitor_recovery_enrollment_required");
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
                throw new UnauthorizedAccessException("visitor_recovery_original_owner_changed");
        }
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            string identity = Identity(enrollment.GlobalLogin)
                + " AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NULL";
            if (!await IsTrueAsync(context, identity, cancellationToken))
                throw new UnauthorizedAccessException("visitor_recovery_read_permissions_not_ready");
            RequireOriginal();
            bool ready = await IsTrueAsync(context,
                $"({identity}) AND ({Manifest(Source(context), enrollment, complete: true)})", cancellationToken);
            RequireOriginal();
            if (!ready) throw new UnauthorizedAccessException("visitor_recovery_read_permissions_not_ready");
        }
        finally { connection.StateChange -= Changed; }
    }

    private static string Manifest(ReadSource source, PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        string global = Text(enrollment.GlobalLogin), runtime = Text(enrollment.RuntimeLogin);
        string expected = string.Join(',', source.ReadColumns.Select(column => Text(column.Name)));
        string required = complete ? string.Join(" AND ", source.ReadColumns.Select(column => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND a.attname={Text(column.Name)}
                    AND acl.grantee={RoleOid(enrollment.GlobalLogin)} AND acl.grantor={RoleOid(enrollment.MigratorLogin)}
                    AND acl.privilege_type='SELECT' AND NOT acl.is_grantable)
            """)) + $" AND pg_catalog.has_schema_privilege({global},{Text(source.Schema)},'USAGE')" : "true";
        return $"""
            (WITH members AS
                (SELECT r.* FROM pg_catalog.pg_roles r WHERE pg_catalog.pg_has_role({RoleOid(enrollment.GlobalLogin)},r.oid,'MEMBER'))
            SELECT (SELECT count(*)=2 FROM pg_catalog.pg_roles WHERE rolname IN ({global},{runtime}) AND rolcanlogin)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE roleid={RoleOid(enrollment.GlobalLogin)})
                AND NOT pg_catalog.pg_has_role({RoleOid(enrollment.GlobalLogin)},{RoleOid(enrollment.RuntimeLogin)},'MEMBER')
                AND NOT pg_catalog.pg_has_role({RoleOid(enrollment.RuntimeLogin)},{RoleOid(enrollment.GlobalLogin)},'MEMBER')
                AND NOT EXISTS (SELECT 1 FROM members r
                    WHERE r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                        OR pg_catalog.has_schema_privilege(r.oid,{Text(source.Schema)},'CREATE'))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t CROSS JOIN members r
                    WHERE t.oid=pg_catalog.to_regclass({Text(source.Table)})
                        AND (t.relowner=r.oid OR pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                            OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", "t.oid", false)}))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                    JOIN pg_catalog.pg_class t ON t.oid=a.attrelid CROSS JOIN members r
                    CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                    WHERE t.oid=pg_catalog.to_regclass({Text(source.Table)}) AND a.attnum<>0 AND NOT a.attisdropped
                        AND (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                            OR (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege) AND NOT (
                                a.attnum>0 AND r.rolname={global} AND permission.privilege='SELECT' AND a.attname IN ({expected})
                                AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl WHERE acl.grantee=r.oid
                                    AND acl.grantor=t.relowner AND acl.privilege_type='SELECT' AND NOT acl.is_grantable)
                                AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                    WHERE acl.grantee=0 OR (acl.grantee=r.oid
                                        AND (acl.grantor<>t.relowner OR acl.privilege_type<>'SELECT' OR acl.is_grantable)))))))
                AND ({required}) AND ({SourcePredicate(source, enrollment, complete)}))
            """;
    }

    private static string SourcePredicate(ReadSource source, PostgresNativeQueueEnrollment enrollment, bool complete)
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

    private static string Fresh(ReadSource source, string global) => $"""
        NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
            WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND acl.grantee={RoleOid(global)})
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(source.Table)})
            AND p.polname={Text(Policy)})
        """;

    private static string PolicyPredicate(ReadSource source, string login) => $"""
        p.polrelid=pg_catalog.to_regclass({Text(source.Table)}) AND p.polname={Text(Policy)}
        AND p.polcmd='r' AND p.polpermissive AND p.polroles=ARRAY[{RoleOid(login)}]
        AND pg_catalog.pg_get_expr(p.polqual,p.polrelid)='true' AND p.polwithcheck IS NULL
        """;

    private static ReadSource Source(ExploreDbContext context)
    {
        var names = context.GetService<ISqlGenerationHelper>();
        var entity = context.Model.FindEntityType(typeof(VisitorEnrollmentOperation))
            ?? throw new InvalidOperationException("visitor_recovery_model_missing");
        string table = entity.GetTableName() ?? throw new InvalidOperationException("visitor_recovery_table_missing");
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? entity.GetSchema() ?? "public";
        var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
        if (entity.BaseType is not null || entity.GetDerivedTypes().Any() || entity.GetComplexProperties().Any()
            || entity.FindPrimaryKey()?.Properties is not [var key] || key.Name != nameof(VisitorEnrollmentOperation.Id))
            throw new InvalidOperationException("visitor_recovery_model_shape_drift");
        var metadata = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
            .SingleOrDefault(value => value.TableName == table && (value.Schema ?? schema) == schema);
        if (metadata is null || metadata.AllowsNullTenant || metadata.ParentJoin is not null)
            throw new InvalidOperationException("visitor_recovery_tenant_shape_drift");
        var columns = ReadProperties.Select(propertyName =>
        {
            var property = entity.FindProperty(propertyName) ?? throw new InvalidOperationException("visitor_recovery_property_missing");
            if (property.IsShadowProperty()) throw new InvalidOperationException("visitor_recovery_property_shape_drift");
            return new Column(property.GetColumnName(store)!, property.GetRelationalTypeMapping().StoreType, !property.IsNullable);
        }).ToArray();
        return new ReadSource(schema, table, names.DelimitIdentifier(table, schema), columns, metadata);
    }

    private static string Identity(string login) =>
        $"session_user={Text(login)} AND current_user=session_user AND pg_catalog.current_setting('role') IN ('none',session_user::text)";
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
    private sealed record ReadSource(string Schema, string Name, string Table, Column[] ReadColumns, TenantTableMetadata TenantMetadata);
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
