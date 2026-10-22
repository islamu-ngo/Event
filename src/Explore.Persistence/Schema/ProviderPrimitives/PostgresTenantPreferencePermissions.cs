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

/// <summary>Fixed preference storage rights. Resolved-self admission and the preference effect remain C# owned.</summary>
public static class PostgresTenantPreferencePermissions
{
    private const string Drift = "tenant_preference_permissions_drift";
    private static readonly string[] Read = [nameof(User.Id), nameof(User.IsDeleted), nameof(User.LastActiveTenantId), nameof(User.UpdatedAt), nameof(User.UpdatedBy)];
    private static readonly string[] Update = [nameof(User.LastActiveTenantId), nameof(User.UpdatedAt), nameof(User.UpdatedBy)];

    internal static IReadOnlyList<string> GlobalReadColumns(ExploreDbContext context) =>
        Array.AsReadOnly(Read.Select(property => Column(context, property)).ToArray());

    internal static IReadOnlyList<string> GlobalUpdateColumns(ExploreDbContext context) =>
        Array.AsReadOnly(Update.Select(property => Column(context, property)).ToArray());

    // These proofs compose fixed declarations, not observed ACLs. The worker consumes only the two descriptors above.
    internal static string CompleteGlobalCataloguePredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        $"COALESCE((({Boundary(context, enrollment)}) AND ({Required(context, enrollment)}) AND (({FixedCatalogue(context, enrollment, preference: true)}) OR ({PostgresManagedTenantWorkerCreationPermissions.CompleteGlobalCataloguePredicate(context, enrollment)}))),false)";

    internal static string OptionalGlobalCataloguePredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        $"COALESCE((({Boundary(context, enrollment)}) AND (({Fresh(context, enrollment)}) OR ({CompleteGlobalCataloguePredicate(context, enrollment)}))),false)";

    private static string Fresh(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        FixedCatalogue(context, enrollment, preference: false);

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        var transaction = context.Database.CurrentTransaction!;
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
            if (context.ContextId != contextId || interrupted || !connection.Matches(context)
                || connection.State != ConnectionState.Open || connection.BackendProcessId != backend
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction)
                || !connection.Owns(transaction.GetDbTransaction()))
                throw new UnauthorizedAccessException("tenant_preference_installer_owner_changed");
        }
        connection.StateChange += Changed;
        string savepoint = "tenant_preference_" + Guid.CreateVersion7().ToString("N");
        try
        {
            RequireOriginal();
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            try
            {
                if (!await ScalarAsync(context, Identity(enrollment.MigratorLogin), cancellationToken))
                    throw new UnauthorizedAccessException("tenant_preference_installer_wrong_identity");
                RequireOriginal();
                if (!await ScalarAsync(context, OptionalGlobalCataloguePredicate(context, enrollment), cancellationToken))
                    throw new InvalidOperationException(Drift);
                RequireOriginal();
                if (!await ScalarAsync(context, CompleteGlobalCataloguePredicate(context, enrollment), cancellationToken))
                {
                    RequireOriginal();
                    var mapping = Mapping(context);
                    var names = context.GetService<ISqlGenerationHelper>();
                    string grantee = names.DelimitIdentifier(enrollment.GlobalLogin);
                    await ExecuteAsync(context, $"GRANT USAGE ON SCHEMA {names.DelimitIdentifier(mapping.Schema)} TO {grantee}; GRANT SELECT ({string.Join(',', GlobalReadColumns(context).Select(names.DelimitIdentifier))}), UPDATE ({string.Join(',', GlobalUpdateColumns(context).Select(names.DelimitIdentifier))}) ON {mapping.Table} TO {grantee}", cancellationToken);
                    RequireOriginal();
                }
                if (!await ScalarAsync(context, CompleteGlobalCataloguePredicate(context, enrollment), cancellationToken))
                    throw new InvalidOperationException(Drift);
                RequireOriginal();
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch
            {
                RequireOriginal();
                await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
                throw;
            }
            finally
            {
                RequireOriginal();
                await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None);
            }
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
        if (enrollment is null) throw new UnauthorizedAccessException("tenant_preference_enrollment_required");
        enrollment.Validate();
        context.RequireBoundTenantLease();
        var transaction = context.Database.CurrentTransaction;
        var connection = RelationalConnectionIdentity.Capture(context);
        var contextId = context.ContextId;
        int? backend = connection.State == ConnectionState.Open ? connection.BackendProcessId : null;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change)
        {
            if (change.CurrentState != ConnectionState.Open) interrupted = true;
        }
        void RequireOriginal()
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.RequireBoundTenantLease();
            if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
                || context.IsConfigurationInstanceImportOwner || context.ContextId != contextId
                || interrupted || !connection.Matches(context) || connection.State != ConnectionState.Open
                || connection.BackendProcessId != backend || !ReferenceEquals(transaction, context.Database.CurrentTransaction)
                || transaction is null || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
                || !connection.Owns(transaction.GetDbTransaction()))
                throw new UnauthorizedAccessException("tenant_preference_original_owner_required");
        }
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            if (!await ScalarAsync(context, $"({Identity(enrollment.GlobalLogin)}) AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NULL", cancellationToken))
                throw new UnauthorizedAccessException("tenant_preference_wrong_identity");
            RequireOriginal();
            bool ready = await ScalarAsync(context, CompleteGlobalCataloguePredicate(context, enrollment), cancellationToken);
            RequireOriginal();
            if (!ready) throw new UnauthorizedAccessException(Drift);
        }
        finally { connection.StateChange -= Changed; }
    }

    private static string Boundary(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var (schema, table) = Mapping(context);
        string relation = $"pg_catalog.to_regclass({Text(table)})";
        return $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace
                WHERE n.nspname={Text(schema)} AND p.proname IN ('human_tenant_preference_write','human_tenant_preference_image'))
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN
                ('event_tenant_preference_owner','event_tenant_preference_global','event_tenant_preference_migrator'))
            AND NOT pg_catalog.pg_has_role({Role(enrollment.RuntimeLogin)},{Role(enrollment.GlobalLogin)},'MEMBER')
            AND NOT pg_catalog.pg_has_role({Role(enrollment.GlobalLogin)},{Role(enrollment.RuntimeLogin)},'MEMBER')
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE pg_catalog.pg_has_role({Role(enrollment.RuntimeLogin)},r.oid,'MEMBER')
                AND (r.oid=(SELECT relowner FROM pg_catalog.pg_class WHERE oid={relation})
                    OR r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                    OR pg_catalog.has_table_privilege(r.oid,{relation},'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN
                        (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                        WHERE a.attrelid={relation} AND a.attnum<>0 AND NOT a.attisdropped
                            AND (pg_catalog.has_column_privilege(r.oid,a.attrelid,a.attnum,permission.privilege)
                                OR pg_catalog.has_column_privilege(r.oid,a.attrelid,a.attnum,permission.privilege || ' WITH GRANT OPTION')))))
            """;
    }

    private static string Required(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        string relation = $"pg_catalog.to_regclass({Text(Mapping(context).Table)})";
        return string.Join(" AND ", GlobalReadColumns(context).Select(column => (column, privilege: "SELECT"))
            .Concat(GlobalUpdateColumns(context).Select(column => (column, privilege: "UPDATE")))
            .Select(grant => $"""
                EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                    WHERE a.attrelid={relation} AND a.attname={Text(grant.column)} AND a.attnum>0
                        AND acl.grantee={Role(enrollment.GlobalLogin)} AND acl.grantor={Role(enrollment.MigratorLogin)}
                        AND acl.privilege_type={Text(grant.privilege)} AND NOT acl.is_grantable)
                """));
    }

    private static string FixedCatalogue(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, bool preference)
    {
        var (schema, table) = Mapping(context);
        var read = preference ? GlobalReadColumns(context) : PostgresUserTenantNavigationReadPermissions.GlobalReadColumns(context);
        var update = preference ? GlobalUpdateColumns(context) : [];
        string expected = string.Join(',', read.Select(column => (column, privilege: "SELECT"))
            .Concat(update.Select(column => (column, privilege: "UPDATE")))
            .Select(grant => $"({Text(grant.column)},{Text(grant.privilege)})"));
        var entity = context.Model.FindEntityType(typeof(User))!;
        string shape = string.Join(" AND ", Read.Select(propertyName =>
        {
            var property = entity.FindProperty(propertyName)!;
            return $"""
                EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=t.oid AND a.attname={Text(Column(context, propertyName))}
                    AND a.attnum>0 AND NOT a.attisdropped AND a.attidentity='' AND a.attgenerated=''
                    AND pg_catalog.format_type(a.atttypid,a.atttypmod)={Text(property.GetRelationalTypeMapping().StoreType)}
                    AND a.attnotnull={(property.IsNullable ? "false" : "true")})
                """;
        }));
        return $"""
            (WITH expected(column_name,privilege) AS (VALUES {expected})
            SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE oid={Role(enrollment.GlobalLogin)} AND rolcanlogin)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE roleid={Role(enrollment.GlobalLogin)})
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid=t.relnamespace
                    WHERE t.oid=pg_catalog.to_regclass({Text(table)}) AND n.nspname={Text(schema)}
                        AND t.relowner={Role(enrollment.MigratorLogin)} AND n.nspowner=t.relowner
                        AND t.relkind='r' AND t.relpersistence='p' AND NOT t.relispartition
                        AND NOT t.relrowsecurity AND NOT t.relforcerowsecurity AND ({shape})
                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits WHERE inhrelid=t.oid OR inhparent=t.oid)
                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite WHERE ev_class=t.oid)
                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE pg_catalog.pg_has_role({Role(enrollment.GlobalLogin)},r.oid,'MEMBER')
                            AND (r.oid=t.relowner OR r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                                OR pg_catalog.has_schema_privilege(r.oid,n.oid,'CREATE')
                                OR pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                                OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN
                                    (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                                    WHERE a.attrelid=t.oid AND a.attnum<>0 AND NOT a.attisdropped
                                        AND (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                                            OR (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege) AND NOT (
                                                r.oid={Role(enrollment.GlobalLogin)} AND a.attnum>0
                                                AND EXISTS (SELECT 1 FROM expected e WHERE e.column_name=a.attname AND e.privilege=permission.privilege)
                                                AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl WHERE acl.grantee=r.oid
                                                    AND acl.grantor=t.relowner AND acl.privilege_type=permission.privilege AND NOT acl.is_grantable)
                                                AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                                    WHERE acl.grantee=0 AND acl.privilege_type=permission.privilege)))))))))
            """;
    }

    private static (string Schema, string Table) Mapping(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(User)) ?? throw new InvalidOperationException(Drift);
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? entity.GetSchema() ?? "public";
        return (schema, context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, schema));
    }

    private static string Column(ExploreDbContext context, string property)
    {
        var entity = context.Model.FindEntityType(typeof(User))!;
        return entity.FindProperty(property)!.GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!;
    }
    private static string Identity(string login) => $"session_user={Text(login)} AND current_user=session_user AND pg_catalog.current_setting('role') IN ('none',session_user::text)";
    private static string Role(string login) => $"(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(login)})";
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static async Task<bool> ScalarAsync(ExploreDbContext context, string predicate, CancellationToken token)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT ({predicate})";
        return await command.ExecuteScalarAsync(token) is true;
    }
    private static async Task ExecuteAsync(ExploreDbContext context, string sql, CancellationToken token)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(token);
    }
}
