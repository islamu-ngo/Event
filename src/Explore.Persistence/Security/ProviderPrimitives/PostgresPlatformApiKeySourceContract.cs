using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Dedicated platform-key source facts. Neither SQL login possession nor actor parameters prove
/// an HTTP credential or action. The managed original owner retains that separate authority.
/// </summary>
public static partial class PostgresPlatformApiKeySourceContract
{
    public const string OwnerRole = "event_platform_key_source_owner";
    public const string MigratorRole = "event_platform_key_source_migrator";
    private const string Observe = "platform_key_source_observe";
    private const string Acquire = "platform_key_source_acquire";
    private const string Arguments = "p_actor uuid,p_provider integer,p_account text,p_purpose integer,p_stamp text,p_session uuid,p_resolved uuid";
    private const string Signature = "uuid,integer,text,integer,text,uuid,uuid";
    private const string ReadPolicy = "platform_key_source_read";
    private const string LockPolicy = "platform_key_source_lock";
    private const string Stamp = "platform-key-source:v1:";
    private static readonly string[] Roles = [OwnerRole, MigratorRole];
    private static readonly string[] Executors = [PostgresInstanceApiKeyManagementContract.ReadExecutor,
        PostgresInstanceApiKeyManagementContract.WriteExecutor, PostgresPlatformApiKeyUsageReportContract.ExecutorRole];
    private static readonly string[] Anchors =
        ["policy_source_set", "human_principal_source_epoch", "human_support_access_source_epoch"];

    internal static string FunctionSql(ExploreDbContext context, bool acquire) =>
        new HumanPrincipalSourceSql(context).Name(acquire ? Acquire : Observe);

    internal static IReadOnlyList<InstanceApiKeySqlFunction> Functions(
        ExploreDbContext context, PlatformApiKeySourceEnrollment enrollment, string executor)
    {
        if (!Executors.Contains(executor, StringComparer.Ordinal))
            throw new InvalidOperationException("platform_key_source_executor_invalid");
        return [new(Observe, Arguments, "jsonb", Body(context, enrollment, false), OwnerRole, executor),
            new(Acquire, Arguments, "jsonb", Body(context, enrollment, true), OwnerRole, executor)];
    }

    public static async Task ApplyAsync(ExploreDbContext context, PlatformApiKeySourceEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) throw new NotSupportedException("platform_key_native_source_requires_postgresql");
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = BuildSql(context, enrollment);
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    internal static string AdmissionSql(ExploreDbContext context, PlatformApiKeySourceEnrollment enrollment) =>
        $"DO $key_source_admission$ BEGIN {Guard(context, enrollment)} {ExactBodies(context, enrollment)} END $key_source_admission$;";

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Native(string query) => query.Replace("@actor", "p_actor", StringComparison.Ordinal)
        .Replace("@provider", "p_provider", StringComparison.Ordinal).Replace("@account", "p_account", StringComparison.Ordinal)
        .Replace("@tenant", "p_resolved", StringComparison.Ordinal).Replace("@stamp", "p_stamp", StringComparison.Ordinal)
        .Replace("@session", "p_session", StringComparison.Ordinal).Replace("@resolved", "p_resolved", StringComparison.Ordinal);

    private static string OriginalSources(ExploreDbContext context, PlatformApiKeySourceEnrollment enrollment) =>
        PostgresPolicySourceSetContract.OriginalSourceAssertionSql(context, enrollment.Original) + "\n"
        + PostgresHumanPrincipalSourceContract.OriginalSourceAssertionSql(context, enrollment.Original) + "\n"
        + PostgresHumanAuthorizationRouteContract.OriginalSourceAssertionSql(context, enrollment.Original) + "\n"
        + PostgresHumanSupportAccessSourceContract.OriginalSourceAssertionSql(context, enrollment.Original);

    private static string AnchorRows(ExploreDbContext context) => string.Join("\n", Anchors.Select(anchor => $"""
        IF (SELECT count(*) FROM {new HumanPrincipalSourceSql(context).Name(anchor)} WHERE id = 1) <> 1
        THEN RAISE EXCEPTION 'platform_key_source_anchor_missing' USING ERRCODE = '55000'; END IF;
        """));

    private static string Caller(ExploreDbContext context, PlatformApiKeySourceEnrollment enrollment, bool acquire)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string allowed = string.Join(" OR ", Executors.Select((executor, index) =>
        {
            var effects = executor == PostgresPlatformApiKeyUsageReportContract.ExecutorRole
                ? new[] { PostgresPlatformApiKeyUsageReportContract.Function(context) }
                : PostgresInstanceApiKeyManagementContract.Functions(context).Where(function => function.Executor == executor).ToArray();
            string signatures = string.Join(",", effects.Select(function =>
                    $"pg_catalog.to_regprocedure({Text(sql.Name(function.Name) + "(" + function.ArgumentTypes + ")")})")
                .Concat(new[] { Observe, Acquire }.Select(name =>
                    $"pg_catalog.to_regprocedure({Text(sql.Name(name) + "(" + Signature + ")")})")));
            return $"(p_purpose = {index + 1} AND f.oid IN ({signatures}))";
        }));
        return $"""
        {PostgresPrivilegeGuard.RestrictedCallerSql}
        IF current_user <> '{OwnerRole}' OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
            OR pg_catalog.current_setting('session_replication_role') <> 'origin'
            {(acquire ? "OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'" : "")}
            OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NOT NULL
            OR NOT COALESCE((p_purpose = 1 AND session_user = {Text(enrollment.ReadLogin)}
                OR p_purpose = 2 AND session_user = {Text(enrollment.WriteLogin)}
                OR p_purpose = 3 AND session_user = {Text(enrollment.ReportLogin)}),false)
            OR p_actor IS NULL OR p_actor = '00000000-0000-0000-0000-000000000000'::uuid
            OR p_session = '00000000-0000-0000-0000-000000000000'::uuid
            OR p_resolved = '00000000-0000-0000-0000-000000000000'::uuid
            OR p_provider IS NULL OR p_account IS NULL OR p_account = ''
            OR p_provider NOT IN ({string.Join(",", Enum.GetValues<AuthenticationProviderKind>().Select(value => (int)value))})
            OR (p_provider = {(int)AuthenticationProviderKind.Local}) IS DISTINCT FROM (p_stamp IS NOT NULL)
            OR p_stamp IS NOT NULL AND (p_account IS DISTINCT FROM p_actor::text OR length(p_stamp) = 0 OR length(p_stamp) > 256)
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE (pg_catalog.pg_has_role(session_user,r.oid,'USAGE') OR pg_catalog.pg_has_role(session_user,r.oid,'SET'))
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
            OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members m
                JOIN pg_catalog.pg_roles r ON r.oid = m.roleid
                WHERE m.member = pg_catalog.to_regrole(session_user)::oid
                    AND r.rolname = CASE p_purpose WHEN 1 THEN '{Executors[0]}'
                        WHEN 2 THEN '{Executors[1]}' WHEN 3 THEN '{Executors[2]}' END
                    AND m.inherit_option AND NOT m.set_option AND NOT m.admin_option
                    AND NOT r.rolcanlogin AND NOT r.rolinherit
                    AND NOT r.rolsuper AND NOT r.rolbypassrls AND NOT r.rolcreatedb
                    AND NOT r.rolcreaterole AND NOT r.rolreplication)
            OR EXISTS (WITH RECURSIVE inherited(roleid) AS (
                SELECT roleid FROM pg_catalog.pg_auth_members WHERE member = pg_catalog.to_regrole(session_user)::oid
                UNION SELECT m.roleid FROM pg_catalog.pg_auth_members m JOIN inherited i ON m.member = i.roleid)
                SELECT 1 FROM inherited i JOIN pg_catalog.pg_roles r ON r.oid = i.roleid
                WHERE r.rolname <> CASE p_purpose WHEN 1 THEN '{Executors[0]}'
                    WHEN 2 THEN '{Executors[1]}' WHEN 3 THEN '{Executors[2]}' END)
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n WHERE pg_catalog.has_schema_privilege(session_user,n.oid,'CREATE'))
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace
                WHERE n.nspname NOT IN ('pg_catalog','information_schema') AND n.nspname NOT LIKE 'pg_toast%'
                    AND t.relkind IN ('r','p','v','m','f')
                    AND (pg_catalog.has_table_privilege(session_user,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                        OR pg_catalog.has_any_column_privilege(session_user,t.oid,'SELECT,INSERT,UPDATE,REFERENCES')))
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.relkind = 'S'
                AND pg_catalog.has_sequence_privilege(session_user,t.oid,'USAGE,SELECT,UPDATE'))
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f JOIN pg_catalog.pg_namespace n ON n.oid = f.pronamespace
                WHERE n.nspname NOT IN ('pg_catalog','information_schema') AND n.nspname NOT LIKE 'pg_toast%'
                    AND f.prosecdef AND pg_catalog.has_function_privilege(session_user,f.oid,'EXECUTE')
                    AND NOT ({allowed}))
        THEN RAISE EXCEPTION 'platform_key_source_caller_mismatch' USING ERRCODE = '42501'; END IF;
        """;
    }

    private static string Body(ExploreDbContext context, PlatformApiKeySourceEnrollment enrollment, bool acquire)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string Aggregate(string query) =>
            $"(SELECT COALESCE(pg_catalog.jsonb_agg(pg_catalog.to_jsonb(r)),'[]'::jsonb) FROM ({Native(query)}) r)";
        string Touch(string anchor) => $"""
            UPDATE {sql.Name(anchor)} SET revision = NOT revision WHERE id = 1;
            IF NOT FOUND THEN RAISE EXCEPTION 'platform_key_source_anchor_missing' USING ERRCODE = '55000'; END IF;
            """;
        string Locks(IEnumerable<string> queries) => !acquire ? "" : string.Join("\n", queries.Select(query =>
            $"PERFORM 1 FROM ({Native(query)} FOR UPDATE) held;"));
        string[] principal = sql.Queries;
        string principalRows = string.Join("\n", principal.Take(10).Select((query, index) =>
            $"p := p || pg_catalog.jsonb_build_object('q{index}',{Aggregate(query)});"));
        string permissions = string.Join("\n", principal.Skip(11).Select((query, index) => $"""
            permission_rows := {Aggregate(query)};
            IF permission_rows = '[]'::jsonb AND NOT mappings_exist THEN permission_rows := {Aggregate(sql.FallbackQueries[index % 2])}; END IF;
            p := p || pg_catalog.jsonb_build_object('q{index + 11}',permission_rows);
            """));
        string local = string.Join("\n", sql.LocalQueries.Select((query, index) =>
            $"p := p || pg_catalog.jsonb_build_object('l{index}',{Aggregate(query)});"));
        string[] support = new HumanSupportAccessSourceSql(context).Queries;
        string supportRows = string.Join("\n", support.Select((query, index) =>
            $"h := h || pg_catalog.jsonb_build_object('q{index}',{Aggregate(query)});"));
        string Route(bool tenant)
        {
            string query = new HumanAuthorizationRouteSql(context).Query(tenant);
            for (int index = 0; index < HumanAuthorizationRouteFacts.Keys.Length; index++)
                query = query.Replace($"@key{index}", Text(HumanAuthorizationRouteFacts.Keys[index]), StringComparison.Ordinal);
            return Aggregate(query);
        }
        return $"""
            DECLARE p jsonb := pg_catalog.jsonb_build_object(); h jsonb := pg_catalog.jsonb_build_object();
                route jsonb; permission_rows jsonb; mappings_exist boolean;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(context, enrollment, acquire)}
                {AnchorRows(context)}
                {(acquire ? Touch(Anchors[0]) + "\n" + Touch(Anchors[1]) : "")}
                {Locks(principal.Take(10))}
                {principalRows}
                SELECT EXISTS ({Native(principal[10])}) INTO mappings_exist;
                p := p || pg_catalog.jsonb_build_object('PermissionMappingsExist',mappings_exist);
                {permissions}
                IF p_stamp IS NOT NULL THEN
                    {Locks(sql.LocalQueries)}
                    {local}
                END IF;
                route := pg_catalog.jsonb_build_object('System',{Route(false)},'Tenant',
                    CASE WHEN p_resolved IS NULL THEN '[]'::jsonb ELSE {Route(true)} END);
                {(acquire ? Touch(Anchors[2]) : "")}
                {Locks(support)}
                {supportRows}
                {Caller(context, enrollment, acquire)}
                {Guard(context, enrollment)}
                RETURN pg_catalog.jsonb_build_object('Principal',p,'Support',h,'Route',route);
            END
            """;
    }

    private static string ExactBodies(ExploreDbContext context, PlatformApiKeySourceEnrollment enrollment) =>
        string.Join("\n", Functions(context, enrollment, Executors[0]).Select(function => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(new HumanPrincipalSourceSql(context).Name(function.Name) + "(" + Signature + ")")})
                    AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(Hash(function.Source))}
                    AND pg_catalog.obj_description(f.oid,'pg_proc') = {Text(Stamp + Hash(function.Source))})
            THEN RAISE EXCEPTION 'platform_key_source_exact_body_drift' USING ERRCODE = '55000'; END IF;
            """));
}
