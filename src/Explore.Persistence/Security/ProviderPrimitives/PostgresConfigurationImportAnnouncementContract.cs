using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Fixed-key storage on the selected import connection. The Global login cannot perform
/// direct EF DML; these primitives supply exclusion and exact-row CAS, not edition policy.
/// </summary>
public static class PostgresConfigurationImportAnnouncementContract
{
    public const string OwnerRole = "event_configuration_import_announcement_owner";
    private const string Prefix = "configuration_import_announcement_";
    private const string Stamp = "configuration-import-announcement:v1:";

    internal static string FunctionSql(ExploreDbContext context, string operation) =>
        new HumanPrincipalSourceSql(context).Name(Prefix + operation);

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            throw new NotSupportedException("announcement_import_requires_postgresql");
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = BuildSql(context, enrollment);
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (owned is not null)
            await owned.CommitAsync(cancellationToken);
    }

    internal static async Task<JsonObject> ReadAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Guid? tenant, Guid[] targets, CancellationToken token)
    {
        RequireTransaction(context, tenant);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = Attest(context, enrollment);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = $"SELECT {FunctionSql(context, "read")}(@tenant,@targets)::text";
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = (object?)tenant ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("targets", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = targets });
        return JsonNode.Parse((string)(await command.ExecuteScalarAsync(token)
            ?? throw new UnauthorizedAccessException("The original announcement import image is absent.")))!.AsObject();
    }

    internal static async Task WriteAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Guid? tenant, JsonObject? before, JsonObject after, CancellationToken token)
    {
        RequireTransaction(context, tenant);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = Attest(context, enrollment);
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = $"SELECT {FunctionSql(context, "write")}(@tenant,@before,@after)::text";
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = (object?)tenant ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("before", NpgsqlDbType.Jsonb) { Value = before?.ToJsonString() ?? "null" });
        command.Parameters.Add(new NpgsqlParameter("after", NpgsqlDbType.Jsonb) { Value = after.ToJsonString() });
        var actual = JsonNode.Parse((string)(await command.ExecuteScalarAsync(token)
            ?? throw new UnauthorizedAccessException("The announcement import write has no storage result.")));
        if (!JsonNode.DeepEquals(actual, after))
            throw new UnauthorizedAccessException("Announcement import storage substituted its canonical row.");
        RequireTransaction(context, tenant);
    }

    private static void RequireTransaction(ExploreDbContext context, Guid? tenant)
    {
        context.ValidateTenantLease();
        if (!context.Database.IsNpgsql() || tenant == Guid.Empty
            || context.Database.CurrentTransaction?.GetDbTransaction() is not { } transaction
            || transaction.IsolationLevel != IsolationLevel.Serializable
            || !ReferenceEquals(transaction.Connection, context.Database.GetDbConnection())
            || context.IsTenantFilterBypassed
            || !context.IsConfigurationInstanceImportOwner && context.TenantFilterTenantId != tenant)
            throw new UnauthorizedAccessException("Announcement import storage requires its original selected transaction.");
    }

    private static string Caller(PostgresNativeQueueEnrollment enrollment) => $"""
        {PostgresPrivilegeGuard.RestrictedCallerSql}
        IF current_user <> '{OwnerRole}'
            OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
            OR pg_catalog.current_setting('session_replication_role') <> 'origin'
            OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'
            OR p_tenant = '00000000-0000-0000-0000-000000000000'::uuid
            OR NOT COALESCE((
                session_user = {Text(enrollment.GlobalLogin)}
                    AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NULL
                OR session_user = {Text(enrollment.RuntimeLogin)} AND p_tenant IS NOT NULL
                    AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid = p_tenant),false)
        THEN RAISE EXCEPTION 'announcement_import_original_storage_owner' USING ERRCODE = '42501'; END IF;
        """;

    private static string Projection(ExploreDbContext context, Type type, string alias)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return "pg_catalog.jsonb_build_object(" + string.Join(",", context.Model.FindEntityType(type)!.GetProperties()
            .OrderBy(property => property.Name, StringComparer.Ordinal).Select(property =>
            {
                string value = $"{alias}.{sql.Column(type, property.Name)}";
                if (property.Name is "CreatedAt" or "UpdatedAt")
                    value = $"pg_catalog.to_char({value} AT TIME ZONE 'UTC','YYYY-MM-DD\"T\"HH24:MI:SS.US') || '0Z'";
                return $"{Text(property.Name)},{value}";
            })) + ")";
    }

    private static string ReadBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string key = Text(GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication);
        return $"""
            BEGIN
                {Caller(enrollment)}
                IF p_targets IS NULL OR EXISTS (SELECT 1 FROM unnest(p_targets) target
                        WHERE target IS NULL OR target='00000000-0000-0000-0000-000000000000'::uuid
                            OR session_user={Text(enrollment.RuntimeLogin)} AND target IS DISTINCT FROM p_tenant)
                    OR cardinality(p_targets) <> (SELECT count(DISTINCT target) FROM unnest(p_targets) target)
                THEN RAISE EXCEPTION 'announcement_import_storage_targets' USING ERRCODE = '42501'; END IF;
                LOCK TABLE {sql.Table(typeof(SystemSetting))},{sql.Table(typeof(TenantSetting))}
                    IN SHARE ROW EXCLUSIVE MODE NOWAIT;
                RETURN pg_catalog.jsonb_build_object(
                    'System',(SELECT {Projection(context, typeof(SystemSetting), "r")} FROM {sql.Table(typeof(SystemSetting))} r
                        WHERE r.{sql.Column(typeof(SystemSetting), "SettingKey")} = {key} COLLATE "C"),
                    'Tenants',(SELECT COALESCE(pg_catalog.jsonb_agg({Projection(context, typeof(TenantSetting), "r")}
                            ORDER BY r.{sql.Column(typeof(TenantSetting), "TenantId")}), '[]'::jsonb)
                        FROM {sql.Table(typeof(TenantSetting))} r
                        WHERE r.{sql.Column(typeof(TenantSetting), "SettingKey")} = {key} COLLATE "C"
                            AND r.{sql.Column(typeof(TenantSetting), "TenantId")} = ANY(p_targets)));
            END;
            """;
    }

    private static string WriteBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string branches = string.Join("\n", new[] { typeof(SystemSetting), typeof(TenantSetting) }.Select(type =>
        {
            var properties = context.Model.FindEntityType(type)!.GetProperties().OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
            string table = sql.Table(type);
            string mapped = "pg_catalog.jsonb_build_object(" + string.Join(",", properties.Select(property =>
                $"{Text(sql.ColumnName(type, property.Name))},p_after->{Text(property.Name)}")) + ")";
            string columns = string.Join(",", properties.Select(property => sql.Column(type, property.Name)));
            string values = string.Join(",", properties.Select(property => "n." + sql.Column(type, property.Name)));
            string updates = string.Join(",", new[] { "Value", "IsLocked", "UpdatedAt", "UpdatedBy" }
                .Select(property => $"{sql.Column(type, property)}=n.{sql.Column(type, property)}"));
            return $"""
                WHEN '{type.Name}' THEN
                    IF (SELECT count(*) FROM pg_catalog.jsonb_object_keys(p_after)) <> {properties.Length}
                        {(type == typeof(TenantSetting) ? "OR (p_after->>'TenantId')::uuid IS DISTINCT FROM p_tenant" : "")}
                    THEN RAISE EXCEPTION 'announcement_import_row_shape' USING ERRCODE = '42501'; END IF;
                    IF p_before = 'null'::jsonb THEN
                        INSERT INTO {table} ({columns})
                            SELECT {values} FROM pg_catalog.jsonb_populate_record(NULL::{table},{mapped}) n;
                    ELSE
                        UPDATE {table} SET {updates} FROM pg_catalog.jsonb_populate_record(NULL::{table},{mapped}) n
                            WHERE {table}.{sql.Column(type, "Id")}=(p_before->>'Id')::uuid;
                    END IF;
                    GET DIAGNOSTICS affected=ROW_COUNT;
                    IF affected <> 1 THEN RAISE EXCEPTION 'announcement_import_row_changed' USING ERRCODE = '40001'; END IF;
                    SELECT {Projection(context, type, "r")} INTO actual FROM {table} r
                        WHERE r.{sql.Column(type, "Id")}=(p_after->>'Id')::uuid;
                """;
        }));
        return $"""
            DECLARE image jsonb; stored jsonb; actual jsonb; source_type text; affected integer;
            BEGIN
                {Caller(enrollment)}
                image := {FunctionSql(context, "read")}(p_tenant,
                    CASE WHEN p_tenant IS NULL THEN ARRAY[]::uuid[] ELSE ARRAY[p_tenant] END);
                source_type := CASE WHEN p_tenant IS NULL THEN 'SystemSetting' ELSE 'TenantSetting' END;
                stored := CASE WHEN p_tenant IS NULL THEN image->'System'
                    ELSE COALESCE((SELECT value FROM pg_catalog.jsonb_array_elements(image->'Tenants') item(value)
                        WHERE (value->>'TenantId')::uuid=p_tenant),'null'::jsonb) END;
                IF p_before IS NULL OR p_after IS NULL OR pg_catalog.jsonb_typeof(p_after) <> 'object'
                    OR stored IS DISTINCT FROM p_before
                    OR p_after->>'SettingKey' IS DISTINCT FROM {Text(GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication)}
                    OR pg_catalog.jsonb_typeof(p_after->'Value') IS DISTINCT FROM 'string'
                    OR pg_catalog.jsonb_typeof(p_after->'IsLocked') IS DISTINCT FROM 'boolean'
                    OR (p_after->>'Id')::uuid IS NULL
                    OR (p_after->>'Id')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
                    OR p_before <> 'null'::jsonb
                        AND p_before - ARRAY['Value','IsLocked','UpdatedAt','UpdatedBy']
                            IS DISTINCT FROM p_after - ARRAY['Value','IsLocked','UpdatedAt','UpdatedBy']
                THEN RAISE EXCEPTION 'announcement_import_original_row' USING ERRCODE = '40001'; END IF;
                IF p_before = p_after THEN RETURN p_before; END IF;
                CASE source_type {branches}
                    ELSE RAISE EXCEPTION 'announcement_import_source_type' USING ERRCODE = '42501';
                END CASE;
                IF actual IS DISTINCT FROM p_after
                THEN RAISE EXCEPTION 'announcement_import_after_image' USING ERRCODE = '42501'; END IF;
                RETURN actual;
            END;
            """;
    }

    private static string Attest(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string checks = string.Join("\n", new[]
        {
            ("read", "uuid,uuid[]", ReadBody(context, enrollment)),
            ("write", "uuid,jsonb,jsonb", WriteBody(context, enrollment))
        }.Select(routine => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid=pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Item1) + "(" + routine.Item2 + ")")})
                    AND f.proowner=pg_catalog.to_regrole('{OwnerRole}') AND f.prosecdef
                    AND f.provolatile='v' AND f.proparallel='u' AND NOT f.proleakproof AND NOT f.proisstrict
                    AND f.prorettype='pg_catalog.jsonb'::regtype
                    AND f.prolang=(SELECT oid FROM pg_catalog.pg_language WHERE lanname='plpgsql')
                    AND f.proconfig=ARRAY['search_path=pg_catalog']
                    AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex')={Text(Hash(routine.Item3))})
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                    CROSS JOIN LATERAL pg_catalog.aclexplode(f.proacl) acl
                    WHERE f.oid=pg_catalog.to_regprocedure({Text(FunctionSql(context, routine.Item1) + "(" + routine.Item2 + ")")})
                        AND (acl.privilege_type <> 'EXECUTE' OR acl.is_grantable
                            OR acl.grantee NOT IN (pg_catalog.to_regrole('{OwnerRole}'),
                                pg_catalog.to_regrole({Text(enrollment.GlobalLogin)}),pg_catalog.to_regrole({Text(enrollment.RuntimeLogin)}))))
            THEN RAISE EXCEPTION 'announcement_import_storage_contract_changed' USING ERRCODE = '55000'; END IF;
            """));
        return $"""
            DO $attest$ BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname='{OwnerRole}'
                    AND NOT (rolcanlogin OR rolsuper OR rolcreatedb OR rolcreaterole OR rolinherit OR rolreplication OR rolbypassrls)
                    AND pg_catalog.shobj_description(oid,'pg_authid')='{Stamp}role')
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members m
                        WHERE m.member=pg_catalog.to_regrole('{OwnerRole}')
                            OR m.roleid=pg_catalog.to_regrole('{OwnerRole}')
                                AND (m.member <> pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})
                                    OR m.inherit_option OR NOT m.set_option))
                    OR pg_catalog.has_schema_privilege('{OwnerRole}',{Text(sql.Schema)},'CREATE')
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t
                        WHERE t.oid={Text(sql.Table(typeof(SystemSetting)))}::regclass
                            AND {PostgresLocalIdentitySystemSettingPolicies.Compatible(OwnerRole, "t")})
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                        WHERE p.polrelid={Text(sql.Table(typeof(TenantSetting)))}::regclass
                            AND p.polname='{Prefix}owner' AND p.polcmd='*' AND p.polpermissive
                            AND p.polroles=ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                            AND pg_catalog.pg_get_expr(p.polqual,p.polrelid)='true'
                            AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid)='true')
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid=t.relnamespace
                        WHERE n.nspname NOT IN ('pg_catalog','information_schema') AND n.nspname NOT LIKE 'pg_toast%'
                            AND t.relkind IN ('r','p','v','m','f')
                            AND (t.oid NOT IN ({Text(sql.Table(typeof(SystemSetting)))}::regclass,{Text(sql.Table(typeof(TenantSetting)))}::regclass)
                                AND pg_catalog.has_table_privilege('{OwnerRole}',t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                                OR pg_catalog.has_table_privilege('{OwnerRole}',t.oid,'DELETE,TRUNCATE,REFERENCES,TRIGGER')))
                THEN RAISE EXCEPTION 'announcement_import_storage_authority_changed' USING ERRCODE = '55000'; END IF;
                {checks}
            END $attest$;
            """;
    }

    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string ddl = string.Join("\n", new[]
        {
            ("read", "p_tenant uuid,p_targets uuid[]", "uuid,uuid[]", ReadBody(context, enrollment)),
            ("write", "p_tenant uuid,p_before jsonb,p_after jsonb", "uuid,jsonb,jsonb", WriteBody(context, enrollment))
        }.Select(routine => $"""
            CREATE OR REPLACE FUNCTION {FunctionSql(context, routine.Item1)}({routine.Item2}) RETURNS jsonb
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog AS $body${routine.Item4}$body$;
            REVOKE ALL ON FUNCTION {FunctionSql(context, routine.Item1)}({routine.Item3}) FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION {FunctionSql(context, routine.Item1)}({routine.Item3})
                TO {sql.Quote(enrollment.RuntimeLogin)},{sql.Quote(enrollment.GlobalLogin)};
            """));
        return $"""
            DO $owner$
            BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                THEN RAISE EXCEPTION 'announcement_import_migration_owner' USING ERRCODE = '42501'; END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname='{OwnerRole}') THEN
                    CREATE ROLE {OwnerRole} NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS;
                    COMMENT ON ROLE {OwnerRole} IS '{Stamp}role';
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_class
                        WHERE oid={Text(sql.Table(typeof(SystemSetting)))}::regclass AND relrowsecurity) THEN
                        EXECUTE {Text(PostgresLocalIdentitySystemSettingPolicies.CreateOwnerPoliciesSql(
                            context, sql.Table(typeof(SystemSetting)), OwnerRole))};
                    END IF;
                ELSE
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname='{OwnerRole}'
                        AND (rolcanlogin OR rolsuper OR rolcreatedb OR rolcreaterole OR rolinherit OR rolreplication OR rolbypassrls
                            OR pg_catalog.shobj_description(oid,'pg_authid') IS DISTINCT FROM '{Stamp}role'))
                    THEN RAISE EXCEPTION 'announcement_import_role_changed' USING ERRCODE = '55000'; END IF;
                    EXECUTE {Text(Attest(context, enrollment))};
                END IF;
            END $owner$;
            GRANT {OwnerRole} TO {sql.Quote(enrollment.MigratorLogin)} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
            GRANT USAGE,CREATE ON SCHEMA {sql.Quote(sql.Schema)} TO {OwnerRole};
            GRANT SELECT,INSERT,UPDATE ON {sql.Table(typeof(SystemSetting))},{sql.Table(typeof(TenantSetting))} TO {OwnerRole};
            DO $policy$ BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy
                    WHERE polrelid={Text(sql.Table(typeof(TenantSetting)))}::regclass AND polname='{Prefix}owner') THEN
                    CREATE POLICY {Prefix}owner ON {sql.Table(typeof(TenantSetting))}
                        FOR ALL TO {OwnerRole} USING (true) WITH CHECK (true);
                END IF;
            END $policy$;
            DO $install$
            DECLARE previous_role text := pg_catalog.current_setting('role');
            BEGIN
                PERFORM pg_catalog.set_config('role','{OwnerRole}',true);
                EXECUTE $ddl${ddl}$ddl$;
                PERFORM pg_catalog.set_config('role',previous_role,true);
            END $install$;
            REVOKE CREATE ON SCHEMA {sql.Quote(sql.Schema)} FROM {OwnerRole};
            {Attest(context, enrollment)}
            """;
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal) + "'";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
