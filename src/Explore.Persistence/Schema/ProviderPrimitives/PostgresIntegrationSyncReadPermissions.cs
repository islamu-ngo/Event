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

/// <summary>Seven queue metadata columns for Global discovery; no subscriber reads or Runtime claim authority.</summary>
public static class PostgresIntegrationSyncReadPermissions
{
    private const string Policy = "integration_sync_metadata_global_read";
    private const string Drift = "integration_sync_read_permissions_drift";
    private static readonly string[] Properties =
    [
        nameof(IntegrationSyncOutbox.Id), nameof(IntegrationSyncOutbox.TenantId), nameof(IntegrationSyncOutbox.Status),
        nameof(IntegrationSyncOutbox.NextAttemptAt), nameof(IntegrationSyncOutbox.ProcessingStartedAt),
        nameof(IntegrationSyncOutbox.CreatedAt), nameof(IntegrationSyncOutbox.IsDeleted)
    ];

    internal static string GlobalPolicyException(ExploreDbContext context, string login)
    {
        var source = Map(context);
        return $"(session_user={Text(login)} AND current_user=session_user AND n.nspname={Text(source.Schema)} AND c.relname={Text(source.Name)} AND ({PolicyPredicate(source, login)}))";
    }

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        var source = Map(context);
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        var transaction = context.Database.CurrentTransaction!;
        using var original = new OriginalConnection(context);
        string savepoint = "integration_discovery_" + Guid.CreateVersion7().ToString("N");
        original.Require(cancellationToken);
        await transaction.CreateSavepointAsync(savepoint, cancellationToken);
        try
        {
            if (!await ScalarAsync(context, Identity(enrollment.MigratorLogin), cancellationToken))
                throw new UnauthorizedAccessException("integration_sync_installer_wrong_identity");
            original.Require(cancellationToken);
            if (!await ScalarAsync(context, NoRetiredAuthority(source), cancellationToken))
                throw new InvalidOperationException("integration_sync_requires_fresh_development_cluster");
            original.Require(cancellationToken);
            string complete = Catalogue(source, enrollment, complete: true);
            string fresh = Fresh(source, enrollment.GlobalLogin);
            if (!await ScalarAsync(context,
                $"({Catalogue(source, enrollment, complete: false)}) AND (({fresh}) OR ({complete}))", cancellationToken))
                throw new InvalidOperationException(Drift);
            original.Require(cancellationToken);
            if (await ScalarAsync(context, fresh, cancellationToken))
            {
                original.Require(cancellationToken);
                var names = context.GetService<ISqlGenerationHelper>();
                await using var command = context.Database.GetDbConnection().CreateCommand();
                command.Transaction = transaction.GetDbTransaction();
                command.CommandText = $"""
                    GRANT USAGE ON SCHEMA {names.DelimitIdentifier(source.Schema)} TO {names.DelimitIdentifier(enrollment.GlobalLogin)};
                    GRANT SELECT ({string.Join(',', source.Columns.Select(column => names.DelimitIdentifier(column.Name)))})
                        ON {source.Table} TO {names.DelimitIdentifier(enrollment.GlobalLogin)};
                    CREATE POLICY {names.DelimitIdentifier(Policy)} ON {source.Table} FOR SELECT
                        TO {names.DelimitIdentifier(enrollment.GlobalLogin)} USING (true);
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken);
                original.Require(cancellationToken);
                if (!await ScalarAsync(context, complete, cancellationToken))
                    throw new InvalidOperationException(Drift);
            }
            original.Require(cancellationToken);
        }
        catch
        {
            await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
            throw;
        }
        finally { await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None); }
        original.Require(cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        if (enrollment is null) throw new UnauthorizedAccessException("integration_sync_enrollment_required");
        enrollment.Validate();
        context.RequireBoundTenantLease();
        void RequireOwner()
        {
            context.RequireBoundTenantLease();
            if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner)
                throw new UnauthorizedAccessException("integration_sync_original_global_required");
        }
        RequireOwner();
        using var original = new OriginalConnection(context);
        original.Require(cancellationToken);
        if (!await ScalarAsync(context, Identity(enrollment.GlobalLogin)
            + " AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NULL", cancellationToken))
            throw new UnauthorizedAccessException("integration_sync_read_permissions_not_ready");
        original.Require(cancellationToken);
        RequireOwner();
        var source = Map(context);
        bool ready = await ScalarAsync(context,
            $"({NoRetiredAuthority(source)}) AND ({Catalogue(source, enrollment, complete: true)})", cancellationToken);
        original.Require(cancellationToken);
        RequireOwner();
        if (!ready) throw new UnauthorizedAccessException("integration_sync_read_permissions_not_ready");
    }

    private static string Catalogue(Source source, PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        string global = Text(enrollment.GlobalLogin), runtime = Text(enrollment.RuntimeLogin);
        string expected = "ARRAY[" + string.Join(',', source.Columns.Select(column => Text(column.Name))) + "]::text[]";
        string required = complete ? $"""
            AND pg_catalog.has_schema_privilege({global},{Text(source.Schema)},'USAGE')
            AND NOT EXISTS (SELECT 1 FROM unnest({expected}) expected(name)
                WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                    WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND a.attname=expected.name
                        AND acl.grantee={RoleId(enrollment.GlobalLogin)} AND acl.grantor={RoleId(enrollment.MigratorLogin)}
                        AND acl.privilege_type='SELECT' AND NOT acl.is_grantable))
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE {PolicyPredicate(source, enrollment.GlobalLogin)})
            """ : "";
        return $"""
            (SELECT (SELECT count(*)=2 FROM pg_catalog.pg_roles WHERE rolname IN ({global},{runtime}) AND rolcanlogin)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE roleid={RoleId(enrollment.GlobalLogin)})
                AND NOT pg_catalog.pg_has_role({RoleId(enrollment.GlobalLogin)},{RoleId(enrollment.RuntimeLogin)},'MEMBER')
                AND NOT pg_catalog.pg_has_role({RoleId(enrollment.RuntimeLogin)},{RoleId(enrollment.GlobalLogin)},'MEMBER')
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                    WHERE pg_catalog.pg_has_role({RoleId(enrollment.GlobalLogin)},r.oid,'MEMBER')
                        AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                            OR pg_catalog.has_schema_privilege(r.oid,{Text(source.Schema)},'CREATE')
                            OR r.oid={RoleId(enrollment.MigratorLogin)}
                            OR (pg_catalog.has_table_privilege(r.oid,pg_catalog.to_regclass({Text(source.Table)}),'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                                OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", $"pg_catalog.to_regclass({Text(source.Table)})", false)})
                            OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                                CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                                WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND a.attnum<>0 AND NOT a.attisdropped
                                    AND (pg_catalog.has_column_privilege(r.oid,a.attrelid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                                        OR (pg_catalog.has_column_privilege(r.oid,a.attrelid,a.attnum,permission.privilege) AND NOT (
                                            r.rolname={global} AND a.attnum>0 AND a.attname=ANY({expected}) AND permission.privilege='SELECT'
                                            AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                                WHERE acl.grantee=r.oid AND acl.grantor={RoleId(enrollment.MigratorLogin)}
                                                    AND acl.privilege_type='SELECT' AND NOT acl.is_grantable)
                                            AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                                WHERE acl.grantee=0 AND acl.privilege_type='SELECT')))))))
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                    WHERE c.oid=pg_catalog.to_regclass({Text(source.Table)}) AND c.relkind='r' AND c.relpersistence='p'
                        AND NOT c.relispartition AND c.relowner={RoleId(enrollment.MigratorLogin)}
                        AND n.nspowner={RoleId(enrollment.MigratorLogin)} AND n.nspname={Text(source.Schema)}
                        AND c.relrowsecurity AND c.relforcerowsecurity)
                AND ({string.Join(" AND ", source.Columns.Select(column => $"""
                    EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)})
                        AND a.attname={Text(column.Name)} AND a.attnum>0 AND NOT a.attisdropped AND a.attidentity='' AND a.attgenerated=''
                        AND pg_catalog.format_type(a.atttypid,a.atttypmod)={Text(column.StoreType)} AND a.attnotnull={(column.Required ? "true" : "false")})
                    """))})
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(source.Table)})
                    AND p.polname='tenant_isolation' AND p.polcmd='*' AND p.polpermissive AND p.polroles=ARRAY[0::oid]
                    AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                    AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(source.TenantFingerprint)}
                        || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(source.Table)})
                    AND (p.polname={Text(Policy)} OR (p.polname<>'tenant_isolation' AND EXISTS (
                        SELECT 1 FROM unnest(p.polroles) policy_role(oid)
                        WHERE CASE WHEN policy_role.oid=0 THEN true
                            ELSE pg_catalog.pg_has_role({RoleId(enrollment.GlobalLogin)},policy_role.oid,'MEMBER') END)))
                    AND NOT COALESCE(({PolicyPredicate(source, enrollment.GlobalLogin)}),false))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits WHERE inhrelid=pg_catalog.to_regclass({Text(source.Table)}) OR inhparent=pg_catalog.to_regclass({Text(source.Table)}))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite WHERE ev_class=pg_catalog.to_regclass({Text(source.Table)}))
                {required})
            """;
    }

    private static string Fresh(Source source, string login) => $"""
        NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
            WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND acl.grantee={RoleId(login)})
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy WHERE polrelid=pg_catalog.to_regclass({Text(source.Table)}) AND polname={Text(Policy)})
        """;
    private static string PolicyPredicate(Source source, string login) => $"""
        p.polrelid=pg_catalog.to_regclass({Text(source.Table)}) AND p.polname={Text(Policy)} AND p.polcmd='r'
        AND p.polpermissive AND p.polroles=ARRAY[{RoleId(login)}]
        AND pg_catalog.pg_get_expr(p.polqual,p.polrelid)='true' AND p.polwithcheck IS NULL
        """;
    private static string NoRetiredAuthority(Source source) => $"""
        NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN
            ('event_integration_sync_discovery_owner','event_integration_sync_discovery_migrator','event_integration_sync_discovery_runtime'))
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace
            WHERE n.nspname={Text(source.Schema)} AND p.proname={Text(source.Name + "_pending_owners")})
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy WHERE polrelid=pg_catalog.to_regclass({Text(source.Table)}) AND polname='integration_sync_queue_discovery')
        """;

    private static Source Map(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(IntegrationSyncOutbox))!;
        string table = entity.GetTableName()!;
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? entity.GetSchema() ?? "public";
        var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
        if (entity.BaseType is not null || entity.GetDerivedTypes().Any() || entity.GetComplexProperties().Any()
            || entity.FindPrimaryKey()?.Properties is not [var key] || key.Name != nameof(IntegrationSyncOutbox.Id))
            throw new InvalidOperationException(Drift);
        var tenant = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
            .Single(value => value.TableName == table && (value.Schema ?? schema) == schema);
        if (tenant.AllowsNullTenant || tenant.ParentJoin is not null) throw new InvalidOperationException(Drift);
        var columns = Properties.Select(name =>
        {
            var property = entity.FindProperty(name)!;
            if (property.IsShadowProperty()) throw new InvalidOperationException(Drift);
            return new Column(property.GetColumnName(store)!, property.GetRelationalTypeMapping().StoreType, !property.IsNullable);
        }).ToArray();
        return new Source(schema, table, context.GetService<ISqlGenerationHelper>().DelimitIdentifier(table, schema),
            columns, PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenant, schema));
    }

    private static async Task<bool> ScalarAsync(ExploreDbContext context, string predicate, CancellationToken token)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT ({predicate})";
        return await command.ExecuteScalarAsync(token) is true;
    }
    private static string Identity(string login) => $"session_user={Text(login)} AND current_user=session_user AND pg_catalog.current_setting('role') IN ('none',session_user::text) AND pg_catalog.current_setting('session_replication_role')='origin'";
    private static string RoleId(string login) => $"(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(login)})";
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private sealed record Column(string Name, string StoreType, bool Required);
    private sealed record Source(string Schema, string Name, string Table, Column[] Columns, string TenantFingerprint);

    private sealed class OriginalConnection : IDisposable
    {
        private readonly ExploreDbContext context;
        private readonly RelationalConnectionIdentity connection;
        private readonly DbContextId contextId;
        private readonly System.Data.Common.DbTransaction? transaction;
        private readonly int? backend;
        private bool interrupted;

        internal OriginalConnection(ExploreDbContext owner)
        {
            context = owner;
            contextId = owner.ContextId;
            connection = RelationalConnectionIdentity.Capture(owner);
            transaction = owner.Database.CurrentTransaction?.GetDbTransaction();
            backend = connection.BackendProcessId;
            connection.StateChange += Changed;
        }
        private void Changed(object? sender, StateChangeEventArgs change)
        {
            if (change.CurrentState != ConnectionState.Open) interrupted = true;
        }
        internal void Require(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (interrupted || context.ContextId != contextId || !connection.Matches(context)
                || connection.State != ConnectionState.Open || connection.BackendProcessId != backend
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction())
                || transaction is not null && !connection.Owns(transaction))
                throw new UnauthorizedAccessException("integration_sync_original_connection_changed");
        }
        public void Dispose() => connection.StateChange -= Changed;
    }
}
