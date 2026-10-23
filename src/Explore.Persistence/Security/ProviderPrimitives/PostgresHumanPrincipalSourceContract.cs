using System.Security.Cryptography;
using System.Text;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Separate execute-only principal source contract. A migration-owned principal-write anchor
/// closes the PostgreSQL snapshot/phantom gap; it does not replace the policy-source-set fence.
/// All outputs are fixed, actor-bound facts. No runtime table grants or credentials are installed.
/// </summary>
public static partial class PostgresHumanPrincipalSourceContract
{
    public const string OwnerRole = "event_human_principal_source_owner";
    public const string MigratorRole = "event_human_principal_source_migrator";
    public const string RuntimeRole = "event_human_principal_source_runtime";
    public const string GlobalRole = "event_human_principal_source_global";
    private const string Observe = "human_principal_source_observe";
    private const string Acquire = "human_principal_source_acquire";
    private const string Write = "human_principal_source_write";
    private const string SettingWrite = "human_principal_source_setting_write";
    private const string Anchor = "human_principal_source_epoch";
    private const string ReadPolicy = "human_principal_source_read";
    private const string UpdatePolicy = "human_principal_source_update";
    private const string Signature = "uuid,integer,text,integer,uuid,text";
    private const string Arguments = "p_actor uuid,p_provider integer,p_account text,p_purpose integer,p_tenant uuid,p_stamp text";
    private const string Stamp = "human-principal-source:v1:";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, RuntimeRole, GlobalRole];
    private static readonly (Type Type, string[] Columns)[] Sources =
        [.. HumanPrincipalSourceSql.Sources, .. HumanPrincipalSourceSql.LocalSources];

    public static string ObserveFunctionSql(ExploreDbContext context) => new HumanPrincipalSourceSql(context).Name(Observe);
    public static string AcquireFunctionSql(ExploreDbContext context) => new HumanPrincipalSourceSql(context).Name(Acquire);

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Database.IsNpgsql()) return;
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
        $"DO $admission$ BEGIN {Guard(context, enrollment)} {ExactBodies(context, enrollment)} END $admission$;";

    internal static string OriginalSourceAssertionSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        Guard(context, enrollment) + "\n" + ExactBodies(context, enrollment);

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Native(string query) => query.Replace("@actor", "p_actor", StringComparison.Ordinal)
        .Replace("@provider", "p_provider", StringComparison.Ordinal).Replace("@account", "p_account", StringComparison.Ordinal)
        .Replace("@tenant", "p_tenant", StringComparison.Ordinal).Replace("@stamp", "p_stamp", StringComparison.Ordinal);

    private static string Touch(HumanPrincipalSourceSql sql) => $"""
        UPDATE {sql.Name(Anchor)} SET revision = NOT revision WHERE id = 1;
        IF NOT FOUND THEN RAISE EXCEPTION 'human_principal_anchor_missing' USING ERRCODE = '55000'; END IF;
        """;

    private static string Caller(PostgresNativeQueueEnrollment enrollment, bool acquire) => $"""
        {PostgresPrivilegeGuard.RestrictedCallerSql}
        IF current_user <> '{OwnerRole}'
            OR pg_catalog.current_setting('role') NOT IN ('none', session_user::text)
            OR pg_catalog.current_setting('session_replication_role') <> 'origin'
            {(acquire ? "OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'" : "")}
            OR NULLIF(pg_catalog.current_setting('app.current_tenant_id', true), '')::uuid IS DISTINCT FROM p_tenant
            OR NOT COALESCE((p_purpose = 1 AND session_user = {Text(enrollment.RuntimeLogin)} AND p_tenant IS NOT NULL
                OR p_purpose IN (2,3) AND session_user = {Text(enrollment.GlobalLogin)} AND p_tenant IS NULL), false)
            OR p_tenant = '00000000-0000-0000-0000-000000000000'::uuid
            OR p_actor IS NULL OR p_actor = '00000000-0000-0000-0000-000000000000'::uuid
            OR (p_provider IS NULL) <> (p_account IS NULL) OR p_account = ''
            OR (p_provider = {(int)AuthenticationProviderKind.Local}) IS DISTINCT FROM (p_stamp IS NOT NULL)
                AND p_provider IS NOT NULL
            OR p_stamp IS NOT NULL AND (p_provider IS NULL OR p_account IS DISTINCT FROM p_actor::text
                OR length(p_stamp) = 0 OR length(p_stamp) > 256)
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE (pg_catalog.pg_has_role(session_user, r.oid, 'USAGE')
                    OR pg_catalog.pg_has_role(session_user, r.oid, 'SET'))
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
        THEN RAISE EXCEPTION 'human_principal_caller_mismatch' USING ERRCODE = '42501'; END IF;
        """;

    private static string Body(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, bool acquire)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string[] queries = sql.Queries;
        string Aggregate(string query) =>
            $"(SELECT COALESCE(pg_catalog.jsonb_agg(pg_catalog.to_jsonb(facts_row)), '[]'::jsonb) FROM ({Native(query)}) facts_row)";
        string[] accountQueries = sql.AccountMembershipPolicyQueries;
        string accountLocks = acquire
            ? string.Join("\n", accountQueries.Select(query =>
                $"PERFORM 1 FROM ({Native(query)} FOR UPDATE) held;"))
            : "";
        string accountFacts = $"""
            result := result || pg_catalog.jsonb_build_object(
                'account_roles', {Aggregate(accountQueries[0])});
            result := result || pg_catalog.jsonb_build_object(
                'account_permissions', {Aggregate(accountQueries[1])});
            """;
        string lockSources = acquire
            ? string.Join("\n", queries.Take(10).Select(query => $"PERFORM 1 FROM ({Native(query)} FOR UPDATE) held;"))
            : "";
        string projections = string.Join("\n", queries.Take(10).Select((query, index) =>
            $"result := result || pg_catalog.jsonb_build_object('q{index}', {Aggregate(query)});"));
        string permissions = string.Join("\n", queries.Skip(11).Select((query, index) => $"""
            v_permission_rows := {Aggregate(query)};
            IF v_permission_rows = '[]'::jsonb AND NOT mappings_exist THEN v_permission_rows := {Aggregate(sql.FallbackQueries[index % 2])}; END IF;
            result := result || pg_catalog.jsonb_build_object('q{index + 11}', v_permission_rows);
            """));
        string localLocks = acquire
            ? string.Join("\n", sql.LocalQueries.Select(query => $"PERFORM 1 FROM ({Native(query)} FOR UPDATE) held;"))
            : "";
        string local = string.Join("\n", sql.LocalQueries.Select((query, index) =>
            $"result := result || pg_catalog.jsonb_build_object('l{index}', {Aggregate(query)});"));
        return $"""
            DECLARE result jsonb := pg_catalog.jsonb_build_object(); v_permission_rows jsonb; mappings_exist boolean;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, acquire)}
                {(acquire ? Touch(sql) : "")}
                {lockSources}
                {accountLocks}
                {projections}
                SELECT EXISTS ({Native(queries[10])}) INTO mappings_exist;
                result := result || pg_catalog.jsonb_build_object('PermissionMappingsExist', mappings_exist);
                {permissions}
                {accountFacts}
                IF p_stamp IS NOT NULL THEN
                    {localLocks}
                    {local}
                END IF;
                {Caller(enrollment, acquire)}
                {Guard(context, enrollment)}
                RETURN result;
            END;
            """;
    }

    private static string WriteBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, bool setting)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string table = sql.Table(typeof(SystemSetting));
        string key = sql.Column(typeof(SystemSetting), nameof(SystemSetting.SettingKey));
        string sourceTables = string.Join(",", Sources.Where(source => source.Type != typeof(RolePermission)
                && source.Type != typeof(SystemSetting)).Select(source => $"{Text(sql.Table(source.Type))}::regclass"));
        string target = setting ? $"""
            IF TG_WHEN <> 'AFTER' OR TG_LEVEL <> 'ROW' OR TG_RELID <> {Text(table)}::regclass
                OR TG_OP NOT IN ('INSERT','UPDATE','DELETE')
            THEN RAISE EXCEPTION 'human_principal_trigger_target' USING ERRCODE = '55000'; END IF;
            IF TG_OP = 'INSERT' THEN
                IF NEW.{key} <> '{GovernanceSettingKeys.Email.DeliveryEnabled}' THEN RETURN NEW; END IF;
            ELSIF TG_OP = 'DELETE' THEN
                IF OLD.{key} <> '{GovernanceSettingKeys.Email.DeliveryEnabled}' THEN RETURN OLD; END IF;
            ELSE
                IF OLD.{key} <> '{GovernanceSettingKeys.Email.DeliveryEnabled}'
                    AND NEW.{key} <> '{GovernanceSettingKeys.Email.DeliveryEnabled}' THEN RETURN NEW; END IF;
            END IF;
            """ : $"""
            IF TG_WHEN <> 'BEFORE' OR TG_LEVEL <> 'STATEMENT'
                OR NOT (TG_RELID IN ({sourceTables}) AND TG_OP IN ('INSERT','UPDATE','DELETE','TRUNCATE')
                    OR TG_RELID = {Text(table)}::regclass AND TG_OP = 'TRUNCATE')
            THEN RAISE EXCEPTION 'human_principal_trigger_target' USING ERRCODE = '55000'; END IF;
            """;
        return $"""
            BEGIN
                {target}
                {Guard(context, enrollment)}
                {Touch(sql)}
                {(setting ? "IF TG_OP = 'DELETE' THEN RETURN OLD; END IF; RETURN NEW;" : "RETURN NULL;")}
            END;
            """;
    }

    private static (string Name, string Signature, string Arguments, string Result, string Body)[] Functions(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
    [
        (Observe, Signature, Arguments, "jsonb", Body(context, enrollment, false)),
        (Acquire, Signature, Arguments, "jsonb", Body(context, enrollment, true)),
        (Session, SessionSignature, SessionArguments, "jsonb", SessionBody(context, enrollment)),
        (Write, "", "", "trigger", WriteBody(context, enrollment, false)),
        (SettingWrite, "", "", "trigger", WriteBody(context, enrollment, true))
    ];

    private static string ExactBodies(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return string.Join("\n", Functions(context, enrollment).Select(function => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(sql.Name(function.Name) + "(" + function.Signature + ")")})
                    AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(Hash(function.Body))}
                    AND pg_catalog.obj_description(f.oid,'pg_proc') = {Text(Stamp + Hash(function.Body))})
            THEN RAISE EXCEPTION 'human_principal_body_drift' USING ERRCODE = '55000'; END IF;
            """));
    }

    private static PostgresPrivilegeGuard.Contract Privileges(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        var sql = new HumanPrincipalSourceSql(context);
        return new(sql.Schema, Roles,
            [new(Observe, Signature, OwnerRole, [RuntimeRole, GlobalRole]),
                new(Acquire, Signature, OwnerRole, [RuntimeRole, GlobalRole]),
                new(Session, SessionSignature, OwnerRole, [RuntimeRole]),
                new(Write, "", OwnerRole, [OwnerRole]), new(SettingWrite, "", OwnerRole, [OwnerRole])],
            [.. Sources.Select(source => new PostgresPrivilegeGuard.Relation(
                context.Model.FindEntityType(source.Type)!.GetTableName()!, OwnerRole, [],
                source.Columns.Select((property, index) => new PostgresPrivilegeGuard.Column(
                    sql.ColumnName(source.Type, property), index == 0 ? ["SELECT", "UPDATE"] : ["SELECT"])).ToArray())),
                new(Anchor, OwnerRole, [], [new("id", ["SELECT"]), new("revision", ["SELECT", "UPDATE"])])],
            [new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(RuntimeRole, enrollment.RuntimeLogin, true, false, false),
                new(GlobalRole, enrollment.GlobalLogin, true, false, false)]);
    }
}
