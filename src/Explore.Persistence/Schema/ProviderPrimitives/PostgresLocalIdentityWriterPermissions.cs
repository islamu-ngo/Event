using System.Data;
using System.Data.Common;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Explore.Persistence.Schema;

/// <summary>Declared database identities only; never credentials or inferred catalogue authority.</summary>
internal sealed record PostgresLocalIdentityWriterEnrollment(
    string MigratorLogin, string RuntimeLogin, string WriterLogin, string? ApplicationGlobalLogin = null)
{
    internal void Validate()
    {
        string[] logins = ApplicationGlobalLogin is null
            ? [MigratorLogin, RuntimeLogin, WriterLogin] : [MigratorLogin, RuntimeLogin, WriterLogin, ApplicationGlobalLogin];
        if (logins.Any(login => string.IsNullOrWhiteSpace(login) || login != login.Trim()
                || login.Any(char.IsControl) || Encoding.UTF8.GetByteCount(login) > 63)
            || logins.Distinct(StringComparer.Ordinal).Count() != logins.Length)
            throw new InvalidOperationException("local_identity_writer_enrollment_invalid");
    }
}

/// <summary>
/// Fixed storage support for the separately selected Local lifecycle purpose.
/// C# admission and workflow completion remain outside this schema installer.
/// </summary>
internal static class PostgresLocalIdentityWriterPermissions
{
    private const string NotReady = "local_identity_writer_permissions_not_ready";
    private const string Drift = "local_identity_writer_permissions_drift";
    private const string ReadPolicy = "local_identity_writer_read";
    private const string ReadRestriction = "local_identity_writer_read_boundary";

    internal static async Task ApplyAsync(DbContext context, PostgresLocalIdentityWriterEnrollment enrollment,
        IdentityDatabaseTopology topology, LocalIdentityWriterInventory.Store store,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        RequireProfile(context, enrollment, topology, store);
        var sources = Sources(context, topology, store);
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        var transaction = context.Database.CurrentTransaction!;
        string savepoint = "local_writer_permissions_" + Guid.CreateVersion7().ToString("N");
        await transaction.CreateSavepointAsync(savepoint, cancellationToken);
        try
        {
            if (await ScalarAsync(context, $"SELECT {Session(enrollment.MigratorLogin)}", cancellationToken) is not true)
                throw new UnauthorizedAccessException("local_identity_writer_installer_wrong_identity");
            bool absent = await ScalarAsync(context, $"SELECT {Absent(sources, enrollment)}", cancellationToken) is true;
            if (await ScalarAsync(context, $"SELECT {Catalogue(context, sources, enrollment, topology, store, !absent)}", cancellationToken) is not true)
                throw new InvalidOperationException(Drift);
            if (absent)
            {
                var names = context.GetService<ISqlGenerationHelper>();
                foreach (var source in sources)
                {
                    await ExecuteAsync(context, $"GRANT USAGE ON SCHEMA {names.DelimitIdentifier(source.Schema)} TO {names.DelimitIdentifier(enrollment.WriterLogin)}", cancellationToken);
                    foreach (string privilege in new[] { "SELECT", "INSERT", "UPDATE" })
                    {
                        var columns = source.Grants.Where(grant => grant.Privilege == privilege).Select(grant => names.DelimitIdentifier(grant.Column)).ToArray();
                        if (columns.Length != 0)
                            await ExecuteAsync(context, $"GRANT {privilege} ({string.Join(',', columns)}) ON {source.Table} TO {names.DelimitIdentifier(enrollment.WriterLogin)}", cancellationToken);
                    }
                    if (source.Delete)
                        await ExecuteAsync(context, $"GRANT DELETE ON {source.Table} TO {names.DelimitIdentifier(enrollment.WriterLogin)}", cancellationToken);
                    if (source.Type == typeof(SystemSetting))
                    {
                        await PostgresLocalIdentitySystemSettingPolicies.InstallAsync(context, source.Table,
                            enrollment.MigratorLogin, cancellationToken);
                        await ExecuteAsync(context, $"ALTER TABLE {source.Table} ENABLE ROW LEVEL SECURITY; ALTER TABLE {source.Table} FORCE ROW LEVEL SECURITY", cancellationToken);
                    }
                    if (source.RowSecurity)
                        foreach (string policy in new[] { ReadPolicy, ReadRestriction })
                            await ExecuteAsync(context, $"CREATE POLICY {names.DelimitIdentifier(policy)} ON {source.Table} AS {(policy == ReadPolicy ? "PERMISSIVE" : "RESTRICTIVE")} FOR SELECT TO {names.DelimitIdentifier(enrollment.WriterLogin)} USING ({RowExpression(context, source, enrollment.WriterLogin, false)})", cancellationToken);
                }
                if (HasGlobalEligibility(topology, store))
                {
                    var eligibility = sources.Single(source => source.Type == typeof(LocalIdentityEligibility));
                    string global = names.DelimitIdentifier(enrollment.ApplicationGlobalLogin!);
                    await ExecuteAsync(context, $"GRANT USAGE ON SCHEMA {names.DelimitIdentifier(eligibility.Schema)} TO {global}", cancellationToken);
                    await ExecuteAsync(context, $"GRANT SELECT ({string.Join(',', eligibility.Columns.Values.Select(column => names.DelimitIdentifier(column.Name)))}) ON {eligibility.Table} TO {global}", cancellationToken);
                    await ExecuteAsync(context, $"GRANT UPDATE ({string.Join(',', GlobalEligibilityUpdates.Select(property => names.DelimitIdentifier(eligibility.Columns[property].Name)))}) ON {eligibility.Table} TO {global}", cancellationToken);
                }
                if (await ScalarAsync(context, $"SELECT {Catalogue(context, sources, enrollment, topology, store, true)}", cancellationToken) is not true)
                    throw new InvalidOperationException(Drift);
            }
        }
        catch
        {
            await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
            throw;
        }
        finally { await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None); }
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    internal static async Task RequireCurrentAsync(DbContext context, PostgresLocalIdentityWriterEnrollment enrollment,
        IdentityDatabaseTopology topology, LocalIdentityWriterInventory.Store store,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        RequireProfile(context, enrollment, topology, store);
        if (context.Database.GetDbConnection() is not NpgsqlConnection connection || connection.State != ConnectionState.Open)
            throw new UnauthorizedAccessException(NotReady);
        var contextId = context.ContextId;
        var application = context as ExploreDbContext;
        var tenant = application?.TenantContext;
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        int backend = connection.ProcessID;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs args) => interrupted = true;
        void RequireOriginal()
        {
            cancellationToken.ThrowIfCancellationRequested();
            application?.RequireBoundTenantLease();
            if (context.ContextId != contextId || interrupted || !ReferenceEquals(tenant, application?.TenantContext)
                || application?.IsTenantFilterBypassed is true || application?.IsConfigurationInstanceImportOwner is true
                || System.Transactions.Transaction.Current is not null
                || !ReferenceEquals(connection, context.Database.GetDbConnection()) || connection.State != ConnectionState.Open
                || connection.ProcessID != backend || !ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction())
                || transaction is not null && !ReferenceEquals(transaction.Connection, connection))
                throw new UnauthorizedAccessException(NotReady);
        }
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            await using var command = CreateReadinessCommand(connection, context, enrollment, topology, store);
            if (await command.ExecuteScalarAsync(cancellationToken) is not true)
                throw new UnauthorizedAccessException(NotReady);
            RequireOriginal();
            string? selectedTenant = application?.TenantFilterTenantId?.ToString();
            if (await ScalarAsync(context, $"SELECT NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NOT DISTINCT FROM {(selectedTenant is null ? "NULL::text" : Text(selectedTenant))}", cancellationToken) is not true)
                throw new UnauthorizedAccessException(NotReady);
            RequireOriginal();
        }
        finally { connection.StateChange -= Changed; }
    }

    internal static DbCommand CreateReadinessCommand(DbConnection connection, DbContext context,
        PostgresLocalIdentityWriterEnrollment enrollment, IdentityDatabaseTopology topology,
        LocalIdentityWriterInventory.Store store)
    {
        RequireProfile(context, enrollment, topology, store);
        var command = connection.CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? 30;
        // ConnectionOpened proves the purpose before its interceptor sets the original tenant GUC.
        command.CommandText = $"SELECT CASE WHEN ({Session(enrollment.WriterLogin)}) THEN ({Catalogue(context, Sources(context, topology, store), enrollment, topology, store, true)}) ELSE false END";
        return command;
    }

    internal static string PolicyException(DbContext context, PostgresLocalIdentityWriterEnrollment enrollment,
        IdentityDatabaseTopology topology, LocalIdentityWriterInventory.Store store)
    {
        RequireProfile(context, enrollment, topology, store);
        string policies = string.Join(" OR ", Sources(context, topology, store).Where(source => source.RowSecurity)
            .SelectMany(source => new[] { ReadPolicy, ReadRestriction }.Select(policy =>
                $"({PolicyPredicate(context, source, enrollment.WriterLogin, policy)})")));
        return policies.Length == 0 ? "FALSE" : "(" + policies + ")";
    }

    private static readonly string[] GlobalEligibilityUpdates =
        [nameof(LocalIdentityEligibility.State), nameof(LocalIdentityEligibility.Generation), nameof(LocalIdentityEligibility.UpdatedAt)];

    private static bool HasGlobalEligibility(IdentityDatabaseTopology topology, LocalIdentityWriterInventory.Store store) =>
        topology == IdentityDatabaseTopology.Colocated && store == LocalIdentityWriterInventory.Store.Application;

    private static string Catalogue(DbContext context, Source[] sources, PostgresLocalIdentityWriterEnrollment enrollment,
        IdentityDatabaseTopology topology, LocalIdentityWriterInventory.Store store, bool complete)
    {
        string login = Text(enrollment.WriterLogin), owner = Text(enrollment.MigratorLogin);
        string loginOid = RoleOid(enrollment.WriterLogin);
        string expected = string.Join(',', sources.SelectMany(source => source.Grants.Select(grant =>
            $"({Text(source.Table)},{Text(grant.Column)},{Text(grant.Privilege)})")));
        string tables = "ARRAY[" + string.Join(',', sources.Select(source => Text(source.Table))) + "]::text[]";
        string deletes = string.Join(',', sources.Where(source => source.Delete).Select(source => Text(source.Table)));
        string allowedDelete = deletes.Length == 0 ? "false" : $"t.oid=ANY(ARRAY[{deletes}]::regclass[])";
        string required = complete ? $"""
            AND NOT EXISTS (SELECT 1 FROM expected e WHERE NOT EXISTS (
                SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                WHERE a.attrelid=pg_catalog.to_regclass(e.relation) AND a.attname=e.column_name AND a.attnum>0
                    AND acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={login})
                    AND acl.grantor=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={owner})
                    AND acl.privilege_type=e.privilege AND NOT acl.is_grantable))
            {string.Concat(sources.Where(source => source.Delete).Select(source => $"""
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_class t CROSS JOIN LATERAL pg_catalog.aclexplode(t.relacl) acl
                    WHERE t.oid=pg_catalog.to_regclass({Text(source.Table)}) AND acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={login})
                        AND acl.grantor=t.relowner AND acl.privilege_type='DELETE' AND NOT acl.is_grantable)
                """))}
            """ : "";
        string global = HasGlobalEligibility(topology, store)
            ? GlobalEligibilityPredicate(sources.Single(source => source.Type == typeof(LocalIdentityEligibility)), enrollment, complete)
            : "true";
        return $"""
            (WITH expected(relation,column_name,privilege) AS (VALUES {expected})
            SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname={login} AND rolcanlogin
                    AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication)
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname={owner} AND rolcanlogin)
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname={Text(enrollment.RuntimeLogin)} AND rolcanlogin)
                {(enrollment.ApplicationGlobalLogin is null ? "" : $"AND EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname={Text(enrollment.ApplicationGlobalLogin)} AND rolcanlogin)")}
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members
                    WHERE member=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={login})
                        OR roleid=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={login}))
                AND NOT EXISTS (SELECT 1 FROM unnest(ARRAY[{string.Join(',', sources.Select(source => Text(source.Schema)).Distinct())}]::text[]) s(name)
                    WHERE pg_catalog.has_schema_privilege({loginOid},s.name,'CREATE'))
                {(complete ? $"AND NOT EXISTS (SELECT 1 FROM unnest(ARRAY[{string.Join(',', sources.Select(source => Text(source.Schema)).Distinct())}]::text[]) s(name) WHERE NOT pg_catalog.has_schema_privilege({loginOid},s.name,'USAGE'))" : "")}
                AND NOT EXISTS (SELECT 1 FROM unnest({tables}) source(relation)
                    JOIN pg_catalog.pg_class t ON t.oid=pg_catalog.to_regclass(source.relation)
                    WHERE t.relowner=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={login})
                        OR EXISTS (SELECT 1 FROM (VALUES ('SELECT'),('INSERT'),('UPDATE'),('DELETE'),('TRUNCATE'),('REFERENCES'),('TRIGGER'),('MAINTAIN')) permission(privilege)
                            WHERE (CASE WHEN permission.privilege='MAINTAIN'
                                THEN {PostgresPrivilegeGuard.MaintainPrivilegeSql(loginOid, "t.oid", false)}
                                ELSE pg_catalog.has_table_privilege({loginOid},t.oid,permission.privilege) END)
                                AND NOT (permission.privilege='DELETE' AND ({allowedDelete})
                                    AND NOT pg_catalog.has_table_privilege({loginOid},t.oid,'DELETE WITH GRANT OPTION')
                                    AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(t.relacl) acl
                                        WHERE acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={login})
                                            AND acl.grantor=t.relowner AND acl.privilege_type='DELETE' AND NOT acl.is_grantable)
                                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(t.relacl) acl WHERE acl.grantee=0 AND acl.privilege_type='DELETE')))
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                            CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                            WHERE a.attrelid=t.oid AND a.attnum<>0 AND NOT a.attisdropped
                                AND (pg_catalog.has_column_privilege({loginOid},t.oid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                                    OR (pg_catalog.has_column_privilege({loginOid},t.oid,a.attnum,permission.privilege) AND NOT (
                                        a.attnum>0 AND EXISTS (SELECT 1 FROM expected e WHERE e.relation=source.relation
                                            AND e.column_name=a.attname AND e.privilege=permission.privilege)
                                        AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl WHERE acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={login})
                                            AND acl.grantor=t.relowner AND acl.privilege_type=permission.privilege AND NOT acl.is_grantable)
                                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl WHERE acl.grantee=0 AND acl.privilege_type=permission.privilege))))))
                AND ({string.Join(" AND ", sources.Select(source => SourcePredicate(context, source, enrollment, complete)))})
                AND ({global})
                {required})
            """;
    }

    private static string Absent(Source[] sources, PostgresLocalIdentityWriterEnrollment enrollment) =>
        string.Join(" AND ", sources.Select(source => $"""
            NOT (pg_catalog.has_table_privilege({RoleOid(enrollment.WriterLogin)},{Text(source.Table)},'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                OR {PostgresPrivilegeGuard.MaintainPrivilegeSql(RoleOid(enrollment.WriterLogin), Text(source.Table), false)})
            AND NOT pg_catalog.has_any_column_privilege({RoleOid(enrollment.WriterLogin)},{Text(source.Table)},'SELECT,INSERT,UPDATE,REFERENCES')
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(source.Table)})
                AND p.polname IN ({Text(ReadPolicy)},{Text(ReadRestriction)}))
            """));

    private static string SourcePredicate(DbContext context, Source source, PostgresLocalIdentityWriterEnrollment enrollment, bool complete)
    {
        var entity = context.Model.FindEntityType(source.Type)!;
        string keys = string.Join(" AND ", entity.GetKeys().Select(key => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_index i WHERE i.indrelid=pg_catalog.to_regclass({Text(source.Table)})
                AND i.indisunique AND i.indisvalid AND i.indisready AND i.indimmediate
                AND i.indexprs IS NULL AND i.indpred IS NULL
                AND ARRAY(SELECT a.attname FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(id,ord)
                    JOIN pg_catalog.pg_attribute a ON a.attrelid=i.indrelid AND a.attnum=k.id
                    WHERE k.ord<=i.indnkeyatts ORDER BY k.ord)
                    =ARRAY[{string.Join(',', key.Properties.Select(property => Text(source.Columns[property.Name].Name)))}]::name[])
            """));
        string shape = string.Join(" AND ", source.Columns.Values.Select(column => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)})
                AND a.attname={Text(column.Name)} AND a.attnum>0 AND NOT a.attisdropped
                AND pg_catalog.format_type(a.atttypid,a.atttypmod)={Text(column.StoreType)}
                AND a.attnotnull={(column.Required ? "true" : "false")} AND a.attgenerated='')
            """));
        string rowSecurity = source.Type == typeof(SystemSetting)
            ? complete ? "c.relrowsecurity AND c.relforcerowsecurity" : "NOT c.relrowsecurity AND NOT c.relforcerowsecurity"
            : source.RowSecurity ? "c.relrowsecurity AND c.relforcerowsecurity" : "NOT c.relrowsecurity AND NOT c.relforcerowsecurity";
        string policies = source.RowSecurity && complete
            ? string.Join(" AND ", new[] { ReadPolicy, ReadRestriction }.Select(policy =>
                $"EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE {PolicyPredicate(context, source, enrollment.WriterLogin, policy)})"))
            : "true";
        string tenantPolicy = TenantPolicyPredicate(context, source);
        string existingTenant = source.Type != typeof(SystemSetting) && source.RowSecurity
            ? $"AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE {tenantPolicy})" : "";
        return $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_class c WHERE c.oid=pg_catalog.to_regclass({Text(source.Table)})
                AND c.relkind='r' AND c.relpersistence='p' AND NOT c.relispartition
                AND c.relowner=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(enrollment.MigratorLogin)})
                AND ({rowSecurity}))
            AND ({shape}) AND ({policies}) AND ({(keys.Length == 0 ? "true" : keys)}) {existingTenant}
            {(complete && source.Type == typeof(SystemSetting) ? "AND (" + PostgresLocalIdentitySystemSettingPolicies.Complete(source.Table, enrollment.MigratorLogin) + ")" : "")}
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(source.Table)})
                AND (p.polname IN ({Text(ReadPolicy)},{Text(ReadRestriction)})
                    OR (SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(enrollment.WriterLogin)})=ANY(p.polroles)
                    OR 0::oid=ANY(p.polroles))
                AND NOT (({PolicyPredicate(context, source, enrollment.WriterLogin, ReadPolicy)})
                    OR ({PolicyPredicate(context, source, enrollment.WriterLogin, ReadRestriction)}) OR ({tenantPolicy})))
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits WHERE inhrelid=pg_catalog.to_regclass({Text(source.Table)})
                OR inhparent=pg_catalog.to_regclass({Text(source.Table)}))
            {(source.Lifecycle ? $"AND (SELECT count(*)={source.Columns.Count} FROM pg_catalog.pg_attribute WHERE attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND attnum>0 AND NOT attisdropped)" : "")}
            """;
    }

    private static string TenantPolicyPredicate(DbContext context, Source source)
    {
        if (!source.RowSecurity || source.Type == typeof(SystemSetting)) return "false";
        var entity = context.Model.FindEntityType(source.Type)!;
        var tenant = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
            .Single(table => table.TableName == entity.GetTableName() && (table.Schema ?? source.Schema) == source.Schema);
        return $"""
            p.polrelid=pg_catalog.to_regclass({Text(source.Table)})
            AND p.polname='tenant_isolation' AND p.polcmd='*' AND p.polpermissive AND p.polroles=ARRAY[0::oid]
            AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
            AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenant, source.Schema))}
                || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text)
            """;
    }

    private static string GlobalEligibilityPredicate(Source source, PostgresLocalIdentityWriterEnrollment enrollment, bool complete)
    {
        string login = Text(enrollment.ApplicationGlobalLogin!);
        string loginOid = RoleOid(enrollment.ApplicationGlobalLogin!);
        if (!complete)
            return $"""
                ((NOT (pg_catalog.has_table_privilege({loginOid},{Text(source.Table)},'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                    OR {PostgresPrivilegeGuard.MaintainPrivilegeSql(loginOid, Text(source.Table), false)})
                    AND NOT pg_catalog.has_any_column_privilege({loginOid},{Text(source.Table)},'SELECT,INSERT,UPDATE,REFERENCES'))
                    OR ({GlobalEligibilityPredicate(source, enrollment, true)}))
                """;
        var expected = source.Columns.Select(column => (Column: column.Value.Name, Privilege: "SELECT"))
            .Concat(GlobalEligibilityUpdates.Select(property => (Column: source.Columns[property].Name, Privilege: "UPDATE"))).ToArray();
        string expectedSql = string.Join(',', expected.Select(grant => $"({Text(grant.Column)},{Text(grant.Privilege)})"));
        return $"""
            (WITH expected(column_name,privilege) AS (VALUES {expectedSql}),
            members AS (SELECT oid,rolname FROM pg_catalog.pg_roles WHERE pg_catalog.pg_has_role({loginOid},oid,'MEMBER'))
            SELECT NOT EXISTS (SELECT 1 FROM members r WHERE
                (pg_catalog.has_table_privilege(r.oid,{Text(source.Table)},'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                    OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", Text(source.Table), false)})
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                    CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                    WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND a.attnum<>0 AND NOT a.attisdropped
                        AND (pg_catalog.has_column_privilege(r.oid,a.attrelid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                            OR (pg_catalog.has_column_privilege(r.oid,a.attrelid,a.attnum,permission.privilege) AND NOT (
                                r.rolname={login} AND a.attnum>0
                                AND EXISTS (SELECT 1 FROM expected e WHERE e.column_name=a.attname AND e.privilege=permission.privilege)
                                AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                    WHERE acl.grantee=r.oid AND acl.grantor=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(enrollment.MigratorLogin)})
                                        AND acl.privilege_type=permission.privilege AND NOT acl.is_grantable)
                                AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl WHERE acl.grantee=0 AND acl.privilege_type=permission.privilege))))))
                {(complete ? $"""
                    AND NOT EXISTS (SELECT 1 FROM expected e WHERE NOT EXISTS (
                        SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                        WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND a.attname=e.column_name
                            AND acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={login})
                            AND acl.grantor=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(enrollment.MigratorLogin)})
                            AND acl.privilege_type=e.privilege AND NOT acl.is_grantable))
                    """ : "")})
            """;
    }

    private static string PolicyPredicate(DbContext context, Source source, string login, string policy) => $"""
        p.polrelid=pg_catalog.to_regclass({Text(source.Table)}) AND p.polname={Text(policy)} AND p.polcmd='r'
        AND p.polpermissive={(policy == ReadPolicy ? "true" : "false")}
        AND p.polroles=ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(login)})]
        AND p.polwithcheck IS NULL AND pg_catalog.pg_get_expr(p.polqual,p.polrelid)={Text(RowExpression(context, source, login, true))}
        """;

    private static string RowExpression(DbContext context, Source source, string login, bool deparsed)
    {
        string identity = deparsed
            ? $"(SESSION_USER = CURRENT_USER) AND (CURRENT_USER = {StandardText(login)}::name)"
            : $"SESSION_USER=CURRENT_USER AND CURRENT_USER={Text(login)}::name";
        if (source.Type != typeof(SystemSetting) && source.Type != typeof(TenantSetting))
            return deparsed ? $"({identity})" : identity;
        var names = context.GetService<ISqlGenerationHelper>();
        string keyName = source.Columns[nameof(SystemSetting.SettingKey)].Name;
        string key = deparsed ? DeparsedIdentifier(keyName) : names.DelimitIdentifier(keyName);
        string[] keys = source.Type == typeof(SystemSetting)
            ? [.. HumanAuthorizationRouteFacts.Keys, GovernanceSettingKeys.Email.DeliveryEnabled]
            : [.. HumanAuthorizationRouteFacts.Keys];
        string keyExpression = deparsed
            ? $"(({key})::text = ANY (ARRAY[{string.Join(", ", keys.Select(value => StandardText(value) + "::text"))}]))"
            : $"{key}::text=ANY(ARRAY[{string.Join(',', keys.Select(Text))}]::text[])";
        if (source.Type == typeof(SystemSetting)) return $"({identity} AND {keyExpression})";
        string tenantName = source.Columns[nameof(TenantSetting.TenantId)].Name;
        string tenant = deparsed ? DeparsedIdentifier(tenantName) : names.DelimitIdentifier(tenantName);
        string selected = deparsed
            ? $"({tenant} = (NULLIF(current_setting('app.current_tenant_id'::text, true), ''::text))::uuid)"
            : $"{tenant}=NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid";
        return $"({identity} AND {keyExpression} AND {selected})";
    }

    private static Source[] Sources(DbContext context, IdentityDatabaseTopology topology, LocalIdentityWriterInventory.Store store)
    {
        var selected = LocalIdentityWriterInventory.Sources
            .Where(source => topology == IdentityDatabaseTopology.Colocated || source.Database == store).ToArray();
        foreach (var source in selected) LocalIdentityWriterInventory.RequireModel(context, source);
        var grants = selected.Select(source => (source.EntityType, Read: source.Select.ToArray(), Insert: source.Insert.ToArray(), Update: source.Update.ToArray(), Delete: source.AllowDelete)).ToList();
        if (store == LocalIdentityWriterInventory.Store.Application)
        {
            grants.AddRange(HumanPrincipalSourceSql.Sources.Select(source => (source.Type, source.Columns, Array.Empty<string>(), Array.Empty<string>(), false)));
            grants.Add((typeof(SystemSetting), ["SettingKey", "Value", "SettingValueTypeId", "IsLocked", "Description", "Category", "AllowedValues"], [], [], false));
            grants.Add((typeof(TenantSetting), ["TenantId", "SettingKey", "Value", "IsLocked"], [], [], false));
        }
        var tenantTables = context is ExploreDbContext ? PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model) : [];
        return grants.GroupBy(source => source.EntityType).Select(group =>
        {
            var entity = context.Model.FindEntityType(group.Key) ?? throw new InvalidOperationException("local_identity_writer_model_missing");
            string name = entity.GetTableName() ?? throw new InvalidOperationException("local_identity_writer_table_missing");
            string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema ?? entity.GetSchema() ?? "public";
            var mapped = StoreObjectIdentifier.Table(name, entity.GetSchema());
            var columns = entity.GetProperties().ToDictionary(property => property.Name,
                property => new Column(property.GetColumnName(mapped) ?? throw new InvalidOperationException("local_identity_writer_column_missing"),
                    property.GetRelationalTypeMapping().StoreType, !property.IsNullable), StringComparer.Ordinal);
            Grant[] Grants(IEnumerable<string> properties, string privilege) => properties.Distinct(StringComparer.Ordinal)
                .Select(property => new Grant(columns.TryGetValue(property, out var column) ? column.Name
                    : throw new InvalidOperationException("local_identity_writer_property_missing"), privilege)).ToArray();
            return new Source(group.Key, schema, context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name, schema), columns,
                [.. Grants(group.SelectMany(value => value.Read), "SELECT"), .. Grants(group.SelectMany(value => value.Insert), "INSERT"),
                    .. Grants(group.SelectMany(value => value.Update), "UPDATE")], group.Any(value => value.Delete),
                selected.Any(value => value.EntityType == group.Key),
                group.Key == typeof(SystemSetting) || tenantTables.Any(table => table.TableName == name && (table.Schema ?? schema) == schema));
        }).ToArray();
    }

    private sealed record Grant(string Column, string Privilege);
    private sealed record Column(string Name, string StoreType, bool Required);
    private sealed record Source(Type Type, string Schema, string Table, Dictionary<string, Column> Columns,
        Grant[] Grants, bool Delete, bool Lifecycle, bool RowSecurity);

    private static string Session(string login) => $"session_user=current_user AND current_user={Text(login)}::name AND pg_catalog.current_setting('role') IN ('none',session_user::text) AND pg_catalog.current_setting('session_replication_role')='origin'";
    private static string StandardText(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    private static string DeparsedIdentifier(string value) =>
        value.Length > 0 && (value[0] is >= 'a' and <= 'z' or '_')
            && value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')
            ? value : "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static string RoleOid(string login) =>
        $"(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(login)})";
    private static DbCommand Command(DbContext context, string sql)
    {
        var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? 30;
        command.CommandText = sql;
        return command;
    }
    private static async Task<object?> ScalarAsync(DbContext context, string sql, CancellationToken token)
    {
        await using var command = Command(context, sql);
        return await command.ExecuteScalarAsync(token);
    }
    private static async Task ExecuteAsync(DbContext context, string sql, CancellationToken token)
    {
        await using var command = Command(context, sql);
        await command.ExecuteNonQueryAsync(token);
    }

    private static void RequireProfile(DbContext context, PostgresLocalIdentityWriterEnrollment enrollment,
        IdentityDatabaseTopology topology, LocalIdentityWriterInventory.Store store)
    {
        if (!Enum.IsDefined(topology)) throw new ArgumentOutOfRangeException(nameof(topology));
        if (!Enum.IsDefined(store)) throw new ArgumentOutOfRangeException(nameof(store));
        ArgumentNullException.ThrowIfNull(enrollment);
        enrollment.Validate();
        bool application = store == LocalIdentityWriterInventory.Store.Application;
        if (application && context is not ExploreDbContext
            || !application && (topology != IdentityDatabaseTopology.External || context is not ExternalIdentityDbContext)
            || application != (enrollment.ApplicationGlobalLogin is not null))
            throw new InvalidOperationException("local_identity_writer_store_profile_invalid");
    }
}
