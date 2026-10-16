using System.Data;
using System.Security.Cryptography;
using System.Text;
using Explore.Domain.Secrets;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Migration-owned boolean projection of the fixed localization binding. Enrollment grants
/// data capability only; application administrator admission remains outside this contract.
/// </summary>
public static class PostgresLocalizationSecretStatusContract
{
    public const string OwnerRole = "event_localization_secret_status_owner";
    public const string MigratorRole = "event_localization_secret_status_migrator";
    public const string RuntimeRole = "event_localization_secret_status_runtime";
    public const string GlobalRole = "event_localization_secret_status_global";
    private const string FunctionName = "localization_secret_status";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, RuntimeRole, GlobalRole];
    private static readonly string[] Fields =
        [nameof(SecretBinding.SettingKey), nameof(SecretBinding.Qualifier),
            nameof(SecretBinding.SettingScopeId), nameof(SecretBinding.ScopeId)];

    public static string FunctionSql(ExploreDbContext context) =>
        new HumanPrincipalSourceSql(context).Name(FunctionName);

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            throw new NotSupportedException("localization_secret_status_requires_postgresql");
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await using var command = Command(context);
        command.CommandText = BuildSql(context, enrollment);
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    internal static async Task<bool> ReadAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment, CancellationToken token)
    {
        var lease = new ExploreDbContextExecutionLease();
        lease.Bind(context, context.TenantFilterTenantId);
        var connection = RelationalConnectionIdentity.Capture(context);
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        bool openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await context.Database.OpenConnectionAsync(token);
        int backend = connection.PostgresProcessId;
        try
        {
            RequireOriginal();
            await RequireReadyAsync(context, enrollment, token);
            RequireOriginal();
            await using var command = Command(context);
            command.CommandText = $"SELECT {FunctionSql(context)}(@tenant)";
            command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid)
                { Value = (object?)lease.SelectedTenantId ?? DBNull.Value });
            bool result = (bool)(await command.ExecuteScalarAsync(token)
                ?? throw new UnauthorizedAccessException("Localization status is absent."));
            RequireOriginal();
            return result;
        }
        finally
        {
            if (openedHere) await context.Database.CloseConnectionAsync();
        }

        void RequireOriginal()
        {
            lease.RequireOriginalOwner(context);
            if (context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
                || !connection.Matches(context) || connection.State != ConnectionState.Open
                || connection.PostgresProcessId != backend
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction()))
                throw new UnauthorizedAccessException("Localization status requires its original ordinary database lease.");
        }
    }

    public static async Task RequireReadyAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql() || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new UnauthorizedAccessException("Localization readiness requires the original open PostgreSQL backend.");
        await using var command = Command(context);
        command.CommandText = $"DO $ready$ BEGIN {Guard(context, enrollment)} {ExactBody(context, enrollment)} END $ready$;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static PostgresPrivilegeGuard.Contract Privileges(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        if (new[] { enrollment.MigratorLogin, enrollment.GlobalLogin, enrollment.RuntimeLogin,
            enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin }.Any(Roles.Contains))
            throw new InvalidOperationException("localization_secret_status_enrollment_collision");
        var sql = new HumanPrincipalSourceSql(context);
        return new(sql.Schema, Roles,
            [new(FunctionName, "uuid", OwnerRole, [RuntimeRole, GlobalRole])],
            [new(context.Model.FindEntityType(typeof(SecretBinding))!.GetTableName()!, OwnerRole, [],
                Fields.Select(field => new PostgresPrivilegeGuard.Column(
                    sql.ColumnName(typeof(SecretBinding), field), ["SELECT"])).ToArray())],
            [
                new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(RuntimeRole, enrollment.RuntimeLogin, true, false, false),
                new(GlobalRole, enrollment.GlobalLogin, true, false, false)
            ]);
    }

    private static string SourceGuard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string table = Text(sql.Table(typeof(SecretBinding)));
        string columns = string.Join(" OR ", Fields.Select(field =>
        {
            var property = context.Model.FindEntityType(typeof(SecretBinding))!.FindProperty(field)!;
            return $"""
                NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({table})
                    AND a.attname = {Text(sql.ColumnName(typeof(SecretBinding), field))}
                    AND a.attnum > 0 AND NOT a.attisdropped AND a.attgenerated = '' AND a.attidentity = ''
                    AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(property.GetRelationalTypeMapping().StoreType)}
                    AND a.attnotnull = {(!property.IsNullable).ToString().ToLowerInvariant()})
                """;
        }));
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n WHERE n.nspname = {Text(sql.Schema)}
                AND n.nspowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({table})
                    AND t.relkind = 'r' AND t.relpersistence = 'p' AND NOT t.relrowsecurity AND NOT t.relforcerowsecurity
                    AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i
                    WHERE i.inhrelid = pg_catalog.to_regclass({table}) OR i.inhparent = pg_catalog.to_regclass({table}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({table}))
                OR {columns}
            THEN RAISE EXCEPTION 'localization_secret_status_source_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string routine = $"pg_catalog.to_regprocedure({Text(FunctionSql(context) + "(uuid)")})";
        string roles = string.Join(",", Roles.Select(Text));
        return $"""
            {PostgresPrivilegeGuard.BuildAssertSql(Privileges(context, enrollment))}
            {SourceGuard(context, enrollment)}
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f WHERE f.oid = {routine}
                AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof AND NOT f.proisstrict
                AND NOT f.proretset AND f.proargdefaults IS NULL AND f.prorettype = 'pg_catalog.bool'::regtype
                AND f.provariadic = 0 AND f.proallargtypes IS NULL AND f.proargmodes IS NULL
                AND f.proargnames = ARRAY['p_tenant']::text[]
                AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_shdepend d
                    WHERE d.refclassid = 'pg_catalog.pg_authid'::regclass AND d.deptype = 'o'
                        AND d.refobjid IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                        AND (d.refobjid <> pg_catalog.to_regrole('{OwnerRole}')::oid
                            OR d.classid <> 'pg_catalog.pg_proc'::regclass
                            OR d.dbid <> (SELECT oid FROM pg_catalog.pg_database WHERE datname = pg_catalog.current_database())
                            OR d.objid IS DISTINCT FROM {routine}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t
                    JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND n.nspname <> {Text(sql.Schema)}
                        AND n.nspname NOT IN ('pg_catalog','information_schema') AND n.nspname NOT LIKE 'pg_toast%'
                        AND t.relkind IN ('r','p','v','m','f')
                        AND (pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                            OR pg_catalog.has_any_column_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,REFERENCES')))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND t.relkind = 'S'
                        AND pg_catalog.has_sequence_privilege(r.oid,t.oid,'USAGE,SELECT,UPDATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_database d CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND pg_catalog.has_database_privilege(r.oid,d.oid,'CREATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f CROSS JOIN LATERAL pg_catalog.aclexplode(f.proacl) a
                    WHERE f.oid <> {routine}
                        AND a.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n
                    WHERE n.nspname = {Text(sql.Schema)}
                        AND (pg_catalog.has_schema_privilege({Text(enrollment.RuntimeLogin)},n.oid,'CREATE')
                            OR pg_catalog.has_schema_privilege({Text(enrollment.GlobalLogin)},n.oid,'CREATE')))
            THEN RAISE EXCEPTION 'localization_secret_status_authority_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    private static string Body(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string Column(string field) => sql.Column(typeof(SecretBinding), field);
        return $"""
            BEGIN
                {Guard(context, enrollment)}
                {PostgresPrivilegeGuard.RestrictedCallerSql}
                IF current_user <> '{OwnerRole}' OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                    OR pg_catalog.current_setting('session_replication_role') <> 'origin'
                    OR NOT COALESCE((session_user = {Text(enrollment.RuntimeLogin)} AND p_tenant IS NOT NULL
                        OR session_user = {Text(enrollment.GlobalLogin)} AND p_tenant IS NULL),false)
                    OR p_tenant = '00000000-0000-0000-0000-000000000000'::uuid
                    OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid IS DISTINCT FROM p_tenant
                    OR pg_catalog.pg_has_role(session_user,pg_catalog.to_regrole({Text(enrollment.MigratorLogin)}),'SET')
                    OR pg_catalog.pg_has_role(session_user,pg_catalog.to_regrole({Text(enrollment.MigratorLogin)}),'USAGE')
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                        WHERE (pg_catalog.pg_has_role(session_user,r.oid,'SET') OR pg_catalog.pg_has_role(session_user,r.oid,'USAGE'))
                            AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
                THEN RAISE EXCEPTION 'localization_secret_status_original_service' USING ERRCODE = '42501'; END IF;
                RETURN EXISTS (SELECT 1 FROM {sql.Table(typeof(SecretBinding))}
                    WHERE {Column(nameof(SecretBinding.SettingKey))} COLLATE "C" =
                            {Text(SecretDefinitionRegistry.Keys.Localization.TmsApiKey)} COLLATE "C"
                        AND {Column(nameof(SecretBinding.Qualifier))} COLLATE "C" = '' COLLATE "C"
                        AND ({Column(nameof(SecretBinding.SettingScopeId))} = 1
                                AND {Column(nameof(SecretBinding.ScopeId))} IS NULL
                            OR p_tenant IS NOT NULL AND {Column(nameof(SecretBinding.SettingScopeId))} = 2
                                AND {Column(nameof(SecretBinding.ScopeId))} = p_tenant));
            END;
            """;
    }

    private static string ExactBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) => $"""
        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
            WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context) + "(uuid)")})
                AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') =
                    {Text(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Body(context, enrollment)))))})
        THEN RAISE EXCEPTION 'localization_secret_status_body_drift' USING ERRCODE = '55000'; END IF;
        """;

    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        var contract = Privileges(context, enrollment);
        string roles = string.Join(",", Roles.Select(Text));
        string memberships = string.Join("\n", contract.Memberships.Select(edge => $"""
            EXECUTE pg_catalog.format('GRANT %I TO %I WITH INHERIT {edge.Inherit.ToString().ToUpperInvariant()},
                SET {edge.Set.ToString().ToUpperInvariant()}, ADMIN FALSE',{Text(edge.Parent)},{Text(edge.Member)});
            """));
        return $"""
            DO $bootstrap$
            DECLARE role_name text; installed integer;
            BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                THEN RAISE EXCEPTION 'localization_secret_status_original_migrator' USING ERRCODE = '42501'; END IF;
                {SourceGuard(context, enrollment)}
                SELECT count(*) INTO installed FROM pg_catalog.pg_roles WHERE rolname IN ({roles});
                IF installed = 0 THEN
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc f JOIN pg_catalog.pg_namespace n ON n.oid = f.pronamespace
                        WHERE n.nspname = {Text(sql.Schema)} AND f.proname = '{FunctionName}')
                    THEN RAISE EXCEPTION 'localization_secret_status_partial_installation' USING ERRCODE = '55000'; END IF;
                    FOREACH role_name IN ARRAY ARRAY[{roles}] LOOP
                        EXECUTE pg_catalog.format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS',role_name);
                    END LOOP;
                    {memberships}
                    FOREACH role_name IN ARRAY ARRAY[{roles}] LOOP
                        IF role_name <> '{MigratorRole}' AND EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members m
                            WHERE m.roleid = pg_catalog.to_regrole(role_name)::oid AND m.member = pg_catalog.to_regrole(session_user)::oid)
                        THEN EXECUTE pg_catalog.format('REVOKE %I FROM %I',role_name,session_user); END IF;
                    END LOOP;
                ELSE
                    IF installed <> {Roles.Length}
                    THEN RAISE EXCEPTION 'localization_secret_status_partial_roles' USING ERRCODE = '55000'; END IF;
                    {Guard(context, enrollment)}
                    {ExactBody(context, enrollment)}
                END IF;
            END $bootstrap$;
            GRANT USAGE ON SCHEMA {sql.Quote(sql.Schema)} TO {string.Join(",", Roles)};
            GRANT SELECT ({string.Join(",", Fields.Select(field => sql.Column(typeof(SecretBinding), field)))})
                ON {sql.Table(typeof(SecretBinding))} TO {OwnerRole};
            GRANT CREATE ON SCHEMA {sql.Quote(sql.Schema)} TO {OwnerRole};
            DO $install$
            DECLARE previous_role text := pg_catalog.current_setting('role');
            BEGIN
                PERFORM pg_catalog.set_config('role','{OwnerRole}',true);
                EXECUTE $ddl$
                    CREATE OR REPLACE FUNCTION {FunctionSql(context)}(p_tenant uuid) RETURNS boolean
                    LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                    AS $body${Body(context, enrollment)}$body$;
                    REVOKE ALL ON FUNCTION {FunctionSql(context)}(uuid) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION {FunctionSql(context)}(uuid) TO {RuntimeRole},{GlobalRole};
                $ddl$;
                PERFORM pg_catalog.set_config('role',previous_role,true);
            END $install$;
            REVOKE CREATE ON SCHEMA {sql.Quote(sql.Schema)} FROM {OwnerRole};
            DO $close$ BEGIN {Guard(context, enrollment)} {ExactBody(context, enrollment)} END $close$;
            """;
    }

    private static System.Data.Common.DbCommand Command(ExploreDbContext context)
    {
        var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        return command;
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
