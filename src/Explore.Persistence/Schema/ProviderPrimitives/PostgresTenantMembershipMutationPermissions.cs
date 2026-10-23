using System.Data;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Explore.Persistence.Schema;

/// <summary>Fixed storage rights for tenant membership removal and instance tenant-status transitions.</summary>
public static partial class PostgresTenantMembershipMutationPermissions
{
    private const string Drift = "tenant_membership_mutation_permissions_drift";
    private const string NotReady = "tenant_membership_mutation_permissions_not_ready";
    internal static string RuntimeDeletePredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        string roleOid, string relationOid) => $"""
        ({roleOid}={Role(enrollment.RuntimeLogin)} AND {relationOid}=pg_catalog.to_regclass({Text(Table(context, typeof(TenantUserProfile)))})
            AND NOT pg_catalog.has_table_privilege({roleOid},{relationOid},'DELETE WITH GRANT OPTION')
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                CROSS JOIN LATERAL pg_catalog.aclexplode(relation.relacl) acl
                WHERE relation.oid={relationOid} AND acl.grantee={roleOid} AND acl.grantor=relation.relowner
                    AND acl.privilege_type='DELETE' AND NOT acl.is_grantable)
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                CROSS JOIN LATERAL pg_catalog.aclexplode(relation.relacl) acl
                WHERE relation.oid={relationOid} AND acl.grantee=0 AND acl.privilege_type='DELETE'))
        """;

    internal static string OptionalCataloguePredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        $"(({Catalogue(context, enrollment, false)}) AND (({Absent(context, enrollment)}) OR ({Catalogue(context, enrollment, true)})))";

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        var transaction = context.Database.CurrentTransaction!;
        var physical = transaction.GetDbTransaction();
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var contextId = context.ContextId;
        int backend = connection.ProcessID;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change) => interrupted = true;
        void RequireOriginal()
        {
            if (interrupted || context.ContextId != contextId || connection.State != ConnectionState.Open
                || connection.ProcessID != backend || !ReferenceEquals(context.Database.GetDbConnection(), connection)
                || !ReferenceEquals(context.Database.CurrentTransaction, transaction)
                || !ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), physical))
                throw new InvalidOperationException(Drift);
        }
        async Task<bool> Read(string predicate)
        {
            RequireOriginal();
            bool result = await IsTrueAsync(context, predicate, cancellationToken);
            RequireOriginal();
            return result;
        }
        async Task Run(string sql)
        {
            RequireOriginal();
            await ExecuteAsync(context, sql, cancellationToken);
            RequireOriginal();
        }
        string savepoint = "membership_mutation_" + Guid.CreateVersion7().ToString("N");
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            try
            {
                if (!await Read(Identity(enrollment.MigratorLogin)))
                    throw new UnauthorizedAccessException("tenant_membership_mutation_installer_wrong_identity");
                if (!await Read(OptionalCataloguePredicate(context, enrollment)))
                    throw new InvalidOperationException(Drift);
                if (await Read(Absent(context, enrollment)))
                {
                    var names = context.GetService<ISqlGenerationHelper>();
                    foreach (var source in Sources(context, enrollment))
                    {
                        await Run($"GRANT USAGE ON SCHEMA {names.DelimitIdentifier(source.Schema)} TO {names.DelimitIdentifier(source.Login)}");
                        foreach (string privilege in new[] { "SELECT", "UPDATE" })
                        {
                            var columns = source.Own.Where(grant => grant.Privilege == privilege)
                                .Select(grant => names.DelimitIdentifier(grant.Column)).ToArray();
                            if (columns.Length != 0)
                                await Run($"GRANT {privilege} ({string.Join(',', columns)}) ON {source.Table} TO {names.DelimitIdentifier(source.Login)}");
                        }
                        if (source.Delete)
                            await Run($"GRANT DELETE ON {source.Table} TO {names.DelimitIdentifier(source.Login)}");
                    }
                }
                if (!await Read(Catalogue(context, enrollment, true)))
                    throw new InvalidOperationException(Drift);
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

    /// <summary>Rechecks the existing selected Runtime or tenantless Global connector without replacing its transaction.</summary>
    public static async Task RequireCurrentAsync(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        if (enrollment is null) throw new UnauthorizedAccessException(NotReady);
        enrollment.Validate();
        context.RequireBoundTenantLease();
        if (context.Database.GetDbConnection() is not NpgsqlConnection connection || connection.State != ConnectionState.Open
            || context.IsTenantFilterBypassed || System.Transactions.Transaction.Current is not null)
            throw new UnauthorizedAccessException(NotReady);
        var contextId = context.ContextId;
        var tenant = context.TenantContext;
        var selected = context.TenantFilterTenantId;
        var transaction = context.Database.CurrentTransaction;
        var physical = transaction?.GetDbTransaction();
        int backend = connection.ProcessID;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change) => interrupted = true;
        void RequireOriginal()
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.RequireBoundTenantLease();
            if (interrupted || context.ContextId != contextId || !ReferenceEquals(context.TenantContext, tenant)
                || context.TenantFilterTenantId != selected || context.IsTenantFilterBypassed
                || !ReferenceEquals(context.Database.GetDbConnection(), connection)
                || connection.State != ConnectionState.Open || connection.ProcessID != backend
                || !ReferenceEquals(context.Database.CurrentTransaction, transaction)
                || !ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), physical))
                throw new UnauthorizedAccessException(NotReady);
        }
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            string login = selected is null ? enrollment.GlobalLogin : enrollment.RuntimeLogin;
            string selectedSql = selected is null ? "NULL::text" : Text(selected.Value.ToString("D"));
            if (!await IsTrueAsync(context, $"({Identity(login)}) AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NOT DISTINCT FROM {selectedSql}", cancellationToken))
                throw new UnauthorizedAccessException(NotReady);
            RequireOriginal();
            if (!await IsTrueAsync(context, Catalogue(context, enrollment, true), cancellationToken))
                throw new UnauthorizedAccessException(NotReady);
            RequireOriginal();
        }
        finally { connection.StateChange -= Changed; }
    }

    private static string Absent(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        string.Join(" AND ", Sources(context, enrollment).Select(source => $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND acl.grantee={Role(source.Login)}
                    AND ({string.Join(" OR ", source.Own.Select(grant => $"(a.attname={Text(grant.Column)} AND acl.privilege_type={Text(grant.Privilege)})"))}))
            {(source.Delete ? $"AND NOT pg_catalog.has_table_privilege({Role(source.Login)},pg_catalog.to_regclass({Text(source.Table)}),'DELETE')" : "")}
            """));

    private static string Catalogue(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, bool complete) => $"""
        EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE oid={Role(enrollment.MigratorLogin)} AND rolcanlogin)
        AND NOT pg_catalog.pg_has_role({Role(enrollment.RuntimeLogin)},{Role(enrollment.GlobalLogin)},'MEMBER')
        AND NOT pg_catalog.pg_has_role({Role(enrollment.GlobalLogin)},{Role(enrollment.RuntimeLogin)},'MEMBER')
        AND {string.Join(" AND ", Sources(context, enrollment).Select(source => SourcePredicate(context, source, enrollment, complete)))}
        """;

    private static string SourcePredicate(ExploreDbContext context, Source source, PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        string expected = string.Join(',', source.Allowed.Select(grant => $"({Text(grant.Column)},{Text(grant.Privilege)})"));
        string required = complete ? string.Join(" AND ", source.Own.Select(grant => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND a.attname={Text(grant.Column)}
                    AND acl.grantee={Role(source.Login)} AND acl.grantor={Role(enrollment.MigratorLogin)}
                    AND acl.privilege_type={Text(grant.Privilege)} AND NOT acl.is_grantable)
            """)) : "true";
        string security = source.Type == typeof(Tenant) ? "NOT t.relrowsecurity AND NOT t.relforcerowsecurity" : TenantPolicy(context, source);
        string delete = source.Delete ? RuntimeDeletePredicate(context, enrollment, "r.oid", "t.oid") : "false";
        var entity = context.Model.FindEntityType(source.Type)!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string shape = string.Join(" AND ", source.Allowed.Select(grant => grant.Column).Distinct().Select(column =>
        {
            var property = entity.GetProperties().Single(property => property.GetColumnName(store) == column);
            return $"""
                EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=t.oid AND a.attname={Text(column)}
                    AND a.attnum>0 AND NOT a.attisdropped AND a.attgenerated='' AND a.attidentity=''
                    AND pg_catalog.format_type(a.atttypid,a.atttypmod)={Text(property.GetRelationalTypeMapping().StoreType)}
                    AND a.attnotnull={(property.IsNullable ? "false" : "true")})
                """;
        }));
        return $"""
            (WITH expected(column_name,privilege) AS (VALUES {expected}),
            members AS (SELECT r.* FROM pg_catalog.pg_roles r WHERE pg_catalog.pg_has_role({Role(source.Login)},r.oid,'MEMBER'))
            SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE oid={Role(source.Login)} AND rolcanlogin)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE roleid={Role(source.Login)})
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid=pg_catalog.to_regclass({Text(source.Table)})
                    AND t.relowner={Role(enrollment.MigratorLogin)} AND t.relkind='r' AND t.relpersistence='p' AND NOT t.relispartition
                    AND ({security}) AND ({shape})
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits WHERE inhrelid=t.oid OR inhparent=t.oid)
                    AND NOT EXISTS (SELECT 1 FROM members r WHERE r.oid=t.relowner OR r.rolsuper OR r.rolbypassrls
                        OR r.rolcreaterole OR r.rolcreatedb OR r.rolreplication
                        OR pg_catalog.has_schema_privilege(r.oid,pg_catalog.to_regnamespace({Text(source.Schema)}),'CREATE')
                        OR pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                        OR (pg_catalog.has_table_privilege(r.oid,t.oid,'DELETE') AND NOT ({delete}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                            CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                            WHERE a.attrelid=t.oid AND a.attnum<>0 AND NOT a.attisdropped
                                AND (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                                    OR (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege) AND NOT (
                                        r.oid={Role(source.Login)} AND a.attnum>0
                                        AND EXISTS (SELECT 1 FROM expected e WHERE e.column_name=a.attname AND e.privilege=permission.privilege)
                                        AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                            WHERE acl.grantee=r.oid AND acl.grantor=t.relowner AND acl.privilege_type=permission.privilege AND NOT acl.is_grantable)
                                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                            WHERE acl.grantee=0 AND acl.privilege_type=permission.privilege))))))
                    {(complete ? $"AND pg_catalog.has_schema_privilege({Role(source.Login)},pg_catalog.to_regnamespace({Text(source.Schema)}),'USAGE')" : "")}
                    {(complete && source.Delete ? $"AND ({RuntimeDeletePredicate(context, enrollment, Role(source.Login), "t.oid")})" : "")})
                AND ({required}))
            """;
    }

    private static string TenantPolicy(ExploreDbContext context, Source source)
    {
        var metadata = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
            .Single(table => table.TableName == source.Name && (table.Schema ?? source.Schema) == source.Schema);
        if (metadata.AllowsNullTenant || metadata.ParentJoin is not null)
            throw new InvalidOperationException("tenant_membership_mutation_model_drift");
        string column = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(Column(context, source.Type, "TenantId"));
        string condition = $"({column} = (NULLIF(current_setting('app.current_tenant_id'::text, true), ''::text))::uuid)";
        return $"""
            t.relrowsecurity AND t.relforcerowsecurity
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=t.oid AND p.polname='tenant_isolation'
                AND p.polcmd='*' AND p.polpermissive AND p.polroles=ARRAY[0::oid]
                AND pg_catalog.pg_get_expr(p.polqual,p.polrelid)={Text(condition)}
                AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid)={Text(condition)}
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
                    WHERE pg_catalog.pg_has_role({Role(source.Login)},role_id,'MEMBER'))))
            """;
    }

    private static Source[] Sources(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sources = Runtime.Select(item =>
        {
            TryGetRuntimeColumns(context, item.Type, out var read, out var update, out bool delete);
            Grant[] own = [.. read.Select(column => new Grant(column, "SELECT")), .. update.Select(column => new Grant(column, "UPDATE"))];
            PostgresActorOwnershipReadPermissions.TryGetRuntimeReadColumns(context, item.Type, out var ownershipRead);
            Grant[] allowed = [.. own.Concat(ownershipRead.Select(column => new Grant(column, "SELECT"))).Distinct()];
            return Map(context, item.Type, enrollment.RuntimeLogin, own, allowed, delete);
        }).ToList();
        PostgresUserTenantMembershipReadPermissions.TryGetGlobalReadColumns(context, typeof(Tenant), out var membership);
        PostgresTenantCreationPermissions.TryGetGlobalColumns(context, typeof(Tenant), out var readTenant, out var insertTenant);
        Grant[] status = GlobalUpdateColumns(context, typeof(Tenant)).Select(column => new Grant(column, "UPDATE")).ToArray();
        Grant[] allowed = [.. membership.Concat(readTenant).Distinct().Select(column => new Grant(column, "SELECT")),
            .. insertTenant.Select(column => new Grant(column, "INSERT")), .. status];
        sources.Add(Map(context, typeof(Tenant), enrollment.GlobalLogin, status, allowed, false));
        return sources.ToArray();
    }

    private static Source Map(ExploreDbContext context, Type type, string login, Grant[] own, Grant[] allowed, bool delete)
    {
        var entity = context.Model.FindEntityType(type)!;
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? entity.GetSchema() ?? "public";
        return new(type, schema, entity.GetTableName()!, Table(context, type), login, own, allowed, delete);
    }
    private static string Table(ExploreDbContext context, Type type)
    {
        var entity = context.Model.FindEntityType(type)!;
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? entity.GetSchema() ?? "public";
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, schema);
    }
    private static string Role(string login) => $"(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(login)})";
    private static string Identity(string login) =>
        $"session_user={Text(login)} AND current_user=session_user AND pg_catalog.current_setting('role') IN ('none',session_user::text)";
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static async Task<bool> IsTrueAsync(ExploreDbContext context, string predicate, CancellationToken token)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT " + predicate;
        return await command.ExecuteScalarAsync(token) is true;
    }
    private static async Task ExecuteAsync(ExploreDbContext context, string sql, CancellationToken token)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(token);
    }
    private sealed record Grant(string Column, string Privilege);
    private sealed record Source(Type Type, string Schema, string Name, string Table, string Login, Grant[] Own, Grant[] Allowed, bool Delete);
}
