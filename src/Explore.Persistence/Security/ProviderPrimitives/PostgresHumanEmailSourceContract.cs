using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Dedicated email source facts, never human authentication or action permission. The original
/// source contracts remain separately attested and retain their exact ordinary caller guards.
/// </summary>
public static partial class PostgresHumanEmailSourceContract
{
    public const string OwnerRole = "event_email_human_source_owner";
    public const string MigratorRole = "event_email_human_source_migrator";
    public const string InstanceRole = "event_email_human_source_instance";
    public const string TenantRole = "event_email_human_source_tenant";
    private const string Observe = "human_email_source_observe";
    private const string Acquire = "human_email_source_acquire";
    private const string Signature = "uuid,integer,text,integer,uuid,text,uuid,uuid,boolean";
    private const string Arguments = "p_actor uuid,p_provider integer,p_account text,p_purpose integer,"
        + "p_tenant uuid,p_stamp text,p_session uuid,p_resolved uuid,p_route boolean";
    private const string ReadPolicy = "human_email_source_read";
    private const string LockPolicy = "human_email_source_lock";
    private const string Stamp = "human-email-source:v1:";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, InstanceRole, TenantRole];
    private static readonly string[] Anchors =
        ["policy_source_set", "human_principal_source_epoch", "human_support_access_source_epoch"];

    public static string ObserveFunctionSql(ExploreDbContext context) => new HumanPrincipalSourceSql(context).Name(Observe);
    public static string AcquireFunctionSql(ExploreDbContext context) => new HumanPrincipalSourceSql(context).Name(Acquire);

    /// <summary>Explicit migration composition; no portable-provider success or runtime installation.</summary>
    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Database.IsNpgsql())
            throw new NotSupportedException("human_email_dedicated_source_native_contract_not_supported");
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = BuildSql(context, enrollment);
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    internal static string AdmissionSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        $"DO $email_admission$ BEGIN {Guard(context, enrollment)} {ExactBodies(context, enrollment)} END $email_admission$;";

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Native(string query) => query.Replace("@actor", "p_actor", StringComparison.Ordinal)
        .Replace("@provider", "p_provider", StringComparison.Ordinal).Replace("@account", "p_account", StringComparison.Ordinal)
        .Replace("@tenant", "p_tenant", StringComparison.Ordinal).Replace("@stamp", "p_stamp", StringComparison.Ordinal)
        .Replace("@session", "p_session", StringComparison.Ordinal).Replace("@resolved", "p_resolved", StringComparison.Ordinal);

    private static string OriginalSources(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        PostgresPolicySourceSetContract.OriginalSourceAssertionSql(context, enrollment) + "\n"
        + PostgresHumanPrincipalSourceContract.OriginalSourceAssertionSql(context, enrollment) + "\n"
        + PostgresHumanAuthorizationRouteContract.OriginalSourceAssertionSql(context, enrollment) + "\n"
        + PostgresHumanSupportAccessSourceContract.OriginalSourceAssertionSql(context, enrollment);

    private static string AnchorRows(ExploreDbContext context) => string.Join("\n", Anchors.Select(anchor => $"""
        IF (SELECT count(*) FROM {new HumanPrincipalSourceSql(context).Name(anchor)} WHERE id = 1) <> 1
        THEN RAISE EXCEPTION 'human_email_source_anchor_missing' USING ERRCODE = '55000'; END IF;
        """));

    private static string Caller(PostgresNativeQueueEnrollment enrollment, bool acquire) => $"""
        {PostgresPrivilegeGuard.RestrictedCallerSql}
        IF current_user <> '{OwnerRole}'
            OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
            OR pg_catalog.current_setting('session_replication_role') <> 'origin'
            {(acquire ? "OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'" : "")}
            OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid IS DISTINCT FROM p_tenant
            OR NOT COALESCE((p_purpose = 1 AND session_user = {Text(enrollment.EmailInstanceMutationLogin)} AND p_tenant IS NULL
                OR p_purpose = 2 AND session_user = {Text(enrollment.EmailTenantMutationLogin)} AND p_tenant IS NOT NULL),false)
            OR p_actor IS NULL OR p_actor = '00000000-0000-0000-0000-000000000000'::uuid
            OR p_tenant = '00000000-0000-0000-0000-000000000000'::uuid
            OR p_session = '00000000-0000-0000-0000-000000000000'::uuid
            OR p_resolved = '00000000-0000-0000-0000-000000000000'::uuid
            OR p_tenant IS NOT NULL AND p_resolved IS DISTINCT FROM p_tenant
            OR p_route IS NULL OR p_provider IS NULL OR p_account IS NULL OR p_account = ''
            OR p_provider NOT IN ({string.Join(",", Enum.GetValues<AuthenticationProviderKind>().Select(value => (int)value))})
            OR (p_provider = {(int)AuthenticationProviderKind.Local}) IS DISTINCT FROM (p_stamp IS NOT NULL)
            OR p_stamp IS NOT NULL AND (p_account IS DISTINCT FROM p_actor::text OR length(p_stamp) = 0 OR length(p_stamp) > 256)
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE (pg_catalog.pg_has_role(session_user,r.oid,'USAGE') OR pg_catalog.pg_has_role(session_user,r.oid,'SET'))
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
        THEN RAISE EXCEPTION 'human_email_caller_mismatch' USING ERRCODE = '42501'; END IF;
        """;

    private static string Body(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, bool acquire)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string Aggregate(string query) =>
            $"(SELECT COALESCE(pg_catalog.jsonb_agg(pg_catalog.to_jsonb(r)),'[]'::jsonb) FROM ({Native(query)}) r)";
        string Touch(string anchor) => $"""
            UPDATE {sql.Name(anchor)} SET revision = NOT revision WHERE id = 1;
            IF NOT FOUND THEN RAISE EXCEPTION 'human_email_source_anchor_missing' USING ERRCODE = '55000'; END IF;
            """;
        string Locks(IEnumerable<string> queries) => !acquire ? "" : string.Join("\n", queries.Select(query =>
            $"PERFORM 1 FROM ({Native(query)} FOR UPDATE) held;"));
        string[] principal = sql.Queries;
        string principalRows = string.Join("\n", principal.Take(10).Select((query, index) =>
            $"p := p || pg_catalog.jsonb_build_object('q{index}',{Aggregate(query)});"));
        string permissions = string.Join("\n", principal.Skip(11).Select((query, index) => $"""
            value := {Aggregate(query)};
            IF value = '[]'::jsonb AND NOT mappings_exist THEN value := {Aggregate(sql.FallbackQueries[index % 2])}; END IF;
            p := p || pg_catalog.jsonb_build_object('q{index + 11}',value);
            """));
        string local = string.Join("\n", sql.LocalQueries.Select((query, index) =>
            $"p := p || pg_catalog.jsonb_build_object('l{index}',{Aggregate(query)});"));
        string[] support = new HumanSupportAccessSourceSql(context).Queries;
        string supportRows = string.Join("\n", support.Select((query, index) =>
            $"h := h || pg_catalog.jsonb_build_object('q{index}',{Aggregate(query)});"));
        var route = new HumanAuthorizationRouteSql(context);
        string Route(bool tenant)
        {
            string query = route.Query(tenant);
            for (int index = 0; index < HumanAuthorizationRouteFacts.Keys.Length; index++)
                query = query.Replace($"@key{index}", Text(HumanAuthorizationRouteFacts.Keys[index]), StringComparison.Ordinal);
            return Aggregate(query);
        }
        return $"""
            DECLARE p jsonb := pg_catalog.jsonb_build_object(); h jsonb := pg_catalog.jsonb_build_object(); route jsonb := NULL;
                value jsonb; mappings_exist boolean;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, acquire)}
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
                IF p_route THEN
                    route := pg_catalog.jsonb_build_object('System',{Route(false)},'Tenant',
                        CASE WHEN p_tenant IS NULL THEN '[]'::jsonb ELSE {Route(true)} END);
                END IF;
                {(acquire ? Touch(Anchors[2]) : "")}
                {Locks(support)}
                {supportRows}
                {Caller(enrollment, acquire)}
                {Guard(context, enrollment)}
                RETURN pg_catalog.jsonb_build_object('Principal',p,'Support',h,'Route',route);
            END;
            """;
    }

    private static string ExactBodies(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return string.Join("\n", new[] { (Observe, false), (Acquire, true) }.Select(function => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(sql.Name(function.Item1) + "(" + Signature + ")")})
                    AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') =
                        {Text(Hash(Body(context, enrollment, function.Item2)))}
                    AND pg_catalog.obj_description(f.oid,'pg_proc') = {Text(Stamp + Hash(Body(context, enrollment, function.Item2)))})
            THEN RAISE EXCEPTION 'human_email_exact_body_drift' USING ERRCODE = '55000'; END IF;
            """));
    }
}
