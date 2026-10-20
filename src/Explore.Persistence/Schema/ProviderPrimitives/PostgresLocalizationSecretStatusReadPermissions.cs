using System.Data;
using Explore.Domain.Secrets;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Schema;

/// <summary>
/// Fixed binding metadata access, not secret resolution or human authorization.
/// The existing non-RLS source remains unchanged; C# selects the requested key and scope.
/// </summary>
public static class PostgresLocalizationSecretStatusReadPermissions
{
    private const string Drift = "localization_status_read_permissions_drift";
    private static readonly string[] Properties =
        [nameof(SecretBinding.SettingKey), nameof(SecretBinding.Qualifier),
            nameof(SecretBinding.SettingScopeId), nameof(SecretBinding.ScopeId)];

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        var source = Mapping(context);
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        var transaction = context.Database.CurrentTransaction!;
        var physical = transaction.GetDbTransaction();
        var connection = RelationalConnectionIdentity.Capture(context);
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
                throw new UnauthorizedAccessException("localization_status_installer_owner_changed");
        }
        connection.StateChange += Changed;
        string savepoint = "localization_metadata_" + Guid.CreateVersion7().ToString("N");
        try
        {
            RequireOriginal();
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            try
            {
                if (!await IsTrueAsync(context, Identity(enrollment.MigratorLogin), cancellationToken))
                    throw new UnauthorizedAccessException("localization_status_installer_wrong_identity");
                RequireOriginal();
                if (!await IsTrueAsync(context, NoObsoleteInstallation(source), cancellationToken))
                    throw new InvalidOperationException("localization_status_requires_fresh_development_cluster");
                RequireOriginal();
                string fresh = Fresh(source, enrollment);
                string complete = Manifest(source, enrollment, complete: true);
                if (!await IsTrueAsync(context,
                    $"({Manifest(source, enrollment, complete: false)}) AND (({fresh}) OR ({complete}))", cancellationToken))
                    throw new InvalidOperationException(Drift);
                RequireOriginal();
                if (await IsTrueAsync(context, fresh, cancellationToken))
                {
                    RequireOriginal();
                    var names = context.GetService<ISqlGenerationHelper>();
                    string logins = string.Join(',', new[] { enrollment.RuntimeLogin, enrollment.GlobalLogin }
                        .Select(names.DelimitIdentifier));
                    await using var command = context.Database.GetDbConnection().CreateCommand();
                    command.Transaction = physical;
                    command.CommandText = $"""
                        GRANT USAGE ON SCHEMA {names.DelimitIdentifier(source.Schema)} TO {logins};
                        GRANT SELECT ({string.Join(',', source.Columns.Select(column => names.DelimitIdentifier(column.Name)))})
                            ON {source.Table} TO {logins};
                        """;
                    await command.ExecuteNonQueryAsync(cancellationToken);
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

    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        if (enrollment is null) throw new UnauthorizedAccessException("localization_status_enrollment_required");
        enrollment.Validate();
        context.RequireBoundTenantLease();
        var tenant = context.TenantFilterTenantId;
        var contextId = context.ContextId;
        var connection = RelationalConnectionIdentity.Capture(context);
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
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
            if (context.ContextId != contextId || context.TenantFilterTenantId != tenant
                || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
                || interrupted || !connection.Matches(context) || connection.State != ConnectionState.Open
                || connection.BackendProcessId != backend
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction())
                || transaction is not null && !connection.Owns(transaction))
                throw new UnauthorizedAccessException("localization_status_original_owner_changed");
        }
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            // Check the actual selected login and tenant session before catalogue admission.
            await using var identity = context.Database.GetDbConnection().CreateCommand();
            identity.Transaction = transaction;
            identity.CommandText = $"""
                SELECT ({Identity(tenant is null ? enrollment.GlobalLogin : enrollment.RuntimeLogin)})
                    AND pg_catalog.current_setting('session_replication_role')='origin'
                    AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid IS NOT DISTINCT FROM @tenant
                """;
            identity.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid)
                { Value = (object?)tenant ?? DBNull.Value });
            if (await identity.ExecuteScalarAsync(cancellationToken) is not true)
                throw new UnauthorizedAccessException("localization_status_read_permissions_not_ready");
            RequireOriginal();
            var source = Mapping(context);
            bool ready = await IsTrueAsync(context,
                $"({NoObsoleteInstallation(source)}) AND ({Manifest(source, enrollment, complete: true)})", cancellationToken);
            RequireOriginal();
            if (!ready) throw new UnauthorizedAccessException("localization_status_read_permissions_not_ready");
        }
        finally { connection.StateChange -= Changed; }
    }

    private static string Manifest(Source source, PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        string runtime = Text(enrollment.RuntimeLogin), global = Text(enrollment.GlobalLogin);
        string allowed = "ARRAY[" + string.Join(',', source.Columns.Select(column => Text(column.Name))) + "]::text[]";
        string required = complete ? $"""
            AND NOT EXISTS (SELECT 1 FROM callers CROSS JOIN unnest({allowed}) expected(name)
                WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                    CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                    WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND a.attname=expected.name
                        AND acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=callers.login)
                        AND acl.grantor={RoleOid(enrollment.MigratorLogin)}
                        AND acl.privilege_type='SELECT' AND NOT acl.is_grantable))
            AND NOT EXISTS (SELECT 1 FROM callers WHERE NOT pg_catalog.has_schema_privilege(login,{Text(source.Schema)},'USAGE'))
            """ : "";
        return $"""
            (WITH callers(login) AS (VALUES ({runtime}),({global})), members AS
                (SELECT callers.login,r.* FROM callers CROSS JOIN pg_catalog.pg_roles r
                    WHERE pg_catalog.pg_has_role(callers.login,r.oid,'MEMBER'))
            SELECT (SELECT count(*)=2 FROM pg_catalog.pg_roles WHERE rolname IN ({runtime},{global}) AND rolcanlogin)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE roleid IN ({RoleOid(enrollment.RuntimeLogin)},{RoleOid(enrollment.GlobalLogin)}))
                AND NOT EXISTS (SELECT 1 FROM members r
                    WHERE r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                        OR pg_catalog.has_schema_privilege(r.oid,{Text(source.Schema)},'CREATE')
                        OR r.oid={RoleOid(enrollment.MigratorLogin)}
                        OR pg_catalog.has_table_privilege(r.oid,pg_catalog.to_regclass({Text(source.Table)}),'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                            CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                            WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND a.attnum<>0 AND NOT a.attisdropped
                                AND (pg_catalog.has_column_privilege(r.oid,a.attrelid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                                    OR (pg_catalog.has_column_privilege(r.oid,a.attrelid,a.attnum,permission.privilege) AND NOT (
                                        a.attnum>0 AND r.rolname=r.login AND permission.privilege='SELECT' AND a.attname=ANY({allowed})
                                        AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl WHERE acl.grantee=r.oid
                                            AND acl.grantor={RoleOid(enrollment.MigratorLogin)} AND acl.privilege_type='SELECT' AND NOT acl.is_grantable)
                                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl WHERE acl.grantee=0 AND acl.privilege_type='SELECT'))))))
                AND ({SourcePredicate(source, enrollment)}) {required})
            """;
    }

    private static string SourcePredicate(Source source, PostgresNativeQueueEnrollment enrollment) => $"""
        EXISTS (SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid=t.relnamespace
            WHERE t.oid=pg_catalog.to_regclass({Text(source.Table)}) AND t.relkind='r' AND t.relpersistence='p'
                AND NOT t.relispartition AND NOT t.relrowsecurity AND NOT t.relforcerowsecurity
                AND t.relowner={RoleOid(enrollment.MigratorLogin)} AND n.nspowner={RoleOid(enrollment.MigratorLogin)}
                AND n.nspname={Text(source.Schema)})
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits WHERE inhrelid=pg_catalog.to_regclass({Text(source.Table)}) OR inhparent=pg_catalog.to_regclass({Text(source.Table)}))
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite WHERE ev_class=pg_catalog.to_regclass({Text(source.Table)}))
        AND ({string.Join(" AND ", source.Columns.Select(column => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)})
                AND a.attname={Text(column.Name)} AND a.attnum>0 AND NOT a.attisdropped AND a.attidentity='' AND a.attgenerated=''
                AND pg_catalog.format_type(a.atttypid,a.atttypmod)={Text(column.StoreType)} AND a.attnotnull={(column.Required ? "true" : "false")})
            """))})
        """;

    private static string Fresh(Source source, PostgresNativeQueueEnrollment enrollment) => $"""
        NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
            WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)})
                AND acl.grantee IN ({RoleOid(enrollment.RuntimeLogin)},{RoleOid(enrollment.GlobalLogin)}))
        """;

    private static string NoObsoleteInstallation(Source source) => $"""
        NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN
            ('event_localization_secret_status_owner','event_localization_secret_status_migrator',
                'event_localization_secret_status_runtime','event_localization_secret_status_global'))
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace
            WHERE n.nspname={Text(source.Schema)} AND p.proname='localization_secret_status')
        """;

    private static Source Mapping(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(SecretBinding))!;
        string table = entity.GetTableName()!;
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? entity.GetSchema() ?? "public";
        var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
        if (entity.BaseType is not null || entity.GetDerivedTypes().Any() || entity.GetComplexProperties().Any()
            || entity.FindPrimaryKey()?.Properties is not [var key] || key.Name != nameof(SecretBinding.Id))
            throw new InvalidOperationException("localization_status_model_shape_drift");
        var columns = Properties.Select(name =>
        {
            var property = entity.FindProperty(name)!;
            if (property.IsShadowProperty()) throw new InvalidOperationException("localization_status_model_shape_drift");
            return new Column(property.GetColumnName(store)!, property.GetRelationalTypeMapping().StoreType, !property.IsNullable);
        }).ToArray();
        return new Source(schema, context.GetService<ISqlGenerationHelper>().DelimitIdentifier(table, schema), columns);
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
    private sealed record Column(string Name, string StoreType, bool Required);
    private sealed record Source(string Schema, string Table, Column[] Columns);
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
