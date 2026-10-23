using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Execute-only, selected-owner support facts with compulsory session/governance writers.</summary>
public static partial class PostgresHumanSupportAccessSourceContract
{
    public const string OwnerRole = "event_human_support_source_owner";
    public const string MigratorRole = "event_human_support_source_migrator";
    public const string RuntimeRole = "event_human_support_source_runtime";
    public const string GlobalRole = "event_human_support_source_global";
    private const string Observe = "human_support_access_source_observe";
    private const string Acquire = "human_support_access_source_acquire";
    private const string Write = "human_support_access_source_write";
    private const string WriterTrigger = "zz_human_support_access_source_write";
    private const string Anchor = "human_support_access_source_epoch";
    private const string ReadPolicy = "human_support_access_source_read";
    private const string UpdatePolicy = "human_support_access_source_update";
    private const string Signature = "uuid,integer,uuid,uuid,uuid";
    private const string Arguments = "p_actor uuid,p_purpose integer,p_tenant uuid,p_session uuid,p_resolved uuid";
    private const string Stamp = "human-support-access-source:v1:";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, RuntimeRole, GlobalRole];

    public static string ObserveFunctionSql(ExploreDbContext context) => Names(context).Name(Observe);
    public static string AcquireFunctionSql(ExploreDbContext context) => Names(context).Name(Acquire);
    private static HumanPrincipalSourceSql Names(ExploreDbContext context) => new(context);
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Native(string query) => query.Replace("@actor", "p_actor", StringComparison.Ordinal)
        .Replace("@tenant", "p_tenant", StringComparison.Ordinal).Replace("@session", "p_session", StringComparison.Ordinal)
        .Replace("@resolved", "p_resolved", StringComparison.Ordinal);

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

    private static string Touch(ExploreDbContext context) => $"""
        UPDATE {Names(context).Name(Anchor)} SET revision = NOT revision WHERE id = 1;
        IF NOT FOUND THEN RAISE EXCEPTION 'human_support_anchor_missing' USING ERRCODE = '55000'; END IF;
        """;

    private static string Caller(PostgresNativeQueueEnrollment enrollment, bool acquire) => $"""
        {PostgresPrivilegeGuard.RestrictedCallerSql}
        IF current_user <> '{OwnerRole}'
            OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
            OR pg_catalog.current_setting('session_replication_role') <> 'origin'
            {(acquire ? "OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'" : "")}
            OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid IS DISTINCT FROM p_tenant
            OR NOT COALESCE((p_purpose = 1 AND session_user = {Text(enrollment.RuntimeLogin)} AND p_tenant IS NOT NULL
                OR p_purpose IN (2,3) AND session_user = {Text(enrollment.GlobalLogin)} AND p_tenant IS NULL),false)
            OR p_actor IS NULL OR p_actor = '00000000-0000-0000-0000-000000000000'::uuid
            OR p_tenant = '00000000-0000-0000-0000-000000000000'::uuid
            OR p_session = '00000000-0000-0000-0000-000000000000'::uuid
            OR p_resolved = '00000000-0000-0000-0000-000000000000'::uuid
            OR p_tenant IS NOT NULL AND p_resolved IS DISTINCT FROM p_tenant
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE (pg_catalog.pg_has_role(session_user,r.oid,'USAGE') OR pg_catalog.pg_has_role(session_user,r.oid,'SET'))
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
        THEN RAISE EXCEPTION 'human_support_caller_mismatch' USING ERRCODE = '42501'; END IF;
        """;

    private static string Body(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, bool acquire)
    {
        string[] queries = new HumanSupportAccessSourceSql(context).Queries;
        string locks = acquire ? string.Join("\n", queries.Select(query =>
            $"PERFORM 1 FROM ({Native(query)} FOR UPDATE) held;")) : "";
        string rows = string.Join("\n", queries.Select((query, index) => $"""
            result := result || pg_catalog.jsonb_build_object('q{index}',
                (SELECT COALESCE(pg_catalog.jsonb_agg(pg_catalog.to_jsonb(facts_row)),'[]'::jsonb)
                    FROM ({Native(query)}) facts_row));
            """));
        return $"""
            DECLARE result jsonb := pg_catalog.jsonb_build_object();
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, acquire)}
                {(acquire ? Touch(context) : "")}
                {locks}
                {rows}
                {Caller(enrollment, acquire)}
                {Guard(context, enrollment)}
                RETURN result;
            END;
            """;
    }

    private static string WriteBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) => $"""
        BEGIN
            IF TG_WHEN <> 'BEFORE' OR TG_LEVEL <> 'STATEMENT'
                OR TG_OP NOT IN ('INSERT','UPDATE','DELETE','TRUNCATE')
                OR TG_RELID NOT IN ({string.Join(",", HumanSupportAccessSourceSql.Sources.Select(source =>
                    $"{Text(Names(context).Table(source.Type))}::regclass"))})
            THEN RAISE EXCEPTION 'human_support_trigger_target' USING ERRCODE = '55000'; END IF;
            {Guard(context, enrollment)}
            {Touch(context)}
            RETURN NULL;
        END;
        """;

    private static (string Name, string Signature, string Arguments, string Result, string Body)[] Functions(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
    [
        (Observe, Signature, Arguments, "jsonb", Body(context, enrollment, false)),
        (Acquire, Signature, Arguments, "jsonb", Body(context, enrollment, true)),
        (Write, "", "", "trigger", WriteBody(context, enrollment))
    ];

    private static string ExactBodies(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        string.Join("\n", Functions(context, enrollment).Select(function => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(Names(context).Name(function.Name) + "(" + function.Signature + ")")})
                    AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(Hash(function.Body))}
                    AND pg_catalog.obj_description(f.oid,'pg_proc') = {Text(Stamp + Hash(function.Body))})
            THEN RAISE EXCEPTION 'human_support_body_drift' USING ERRCODE = '55000'; END IF;
            """));

    private static PostgresPrivilegeGuard.Contract Privileges(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        var sql = Names(context);
        return new(sql.Schema, Roles,
            [new(Observe, Signature, OwnerRole, [RuntimeRole, GlobalRole]),
                new(Acquire, Signature, OwnerRole, [RuntimeRole, GlobalRole]), new(Write, "", OwnerRole, [OwnerRole])],
            [.. HumanSupportAccessSourceSql.Sources.Select(source => new PostgresPrivilegeGuard.Relation(
                context.Model.FindEntityType(source.Type)!.GetTableName()!, OwnerRole, [],
                source.Columns.Select((property, index) => new PostgresPrivilegeGuard.Column(sql.ColumnName(source.Type, property),
                    index == 0 ? ["SELECT", "UPDATE"] : ["SELECT"])).ToArray())),
                new(Anchor, OwnerRole, [], [new("id", ["SELECT"]), new("revision", ["SELECT", "UPDATE"])])],
            [new(OwnerRole, MigratorRole, false, true, false), new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(RuntimeRole, enrollment.RuntimeLogin, true, false, false), new(GlobalRole, enrollment.GlobalLogin, true, false, false)]);
    }
}
