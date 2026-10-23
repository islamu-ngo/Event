using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Fixed execute-only assignment facts. The dedicated epoch covers every contributor write,
/// including empty ranges, independently of the principal and policy-source-set epochs.
/// Installation is explicit migrator work; no runtime IAM promotion or repair is performed.
/// </summary>
public static partial class PostgresHumanEventAssignmentSourceContract
{
    public const string OwnerRole = "event_human_assignment_source_owner";
    public const string MigratorRole = "event_human_assignment_source_migrator";
    public const string RuntimeRole = "event_human_assignment_source_runtime";
    public const string GlobalRole = "event_human_assignment_source_global";
    private const string Observe = "human_event_assignment_source_observe";
    private const string Acquire = "human_event_assignment_source_acquire";
    private const string Write = "human_event_assignment_source_write";
    private const string WriterTrigger = "zz_human_event_assignment_source_write";
    private const string Anchor = "human_event_assignment_source_epoch";
    private const string ReadPolicy = "human_event_assignment_source_read";
    private const string UpdatePolicy = "human_event_assignment_source_update";
    private const string Signature = "uuid,integer,uuid,uuid[]";
    private const string Arguments = "p_actor uuid,p_purpose integer,p_tenant uuid,p_events uuid[]";
    private const string Stamp = "human-event-assignment-source:v1:";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, RuntimeRole, GlobalRole];

    public static string ObserveFunctionSql(ExploreDbContext context) => Names(context).Name(Observe);
    public static string AcquireFunctionSql(ExploreDbContext context) => Names(context).Name(Acquire);
    private static HumanPrincipalSourceSql Names(ExploreDbContext context) => new(context);
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Native(string value) => value.Replace("@actor", "p_actor", StringComparison.Ordinal)
        .Replace("@tenant", "p_tenant", StringComparison.Ordinal).Replace("@events", "p_events", StringComparison.Ordinal);

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
        await PostgresHumanAiModerationProposalTargetContract.ApplyAsync(context, enrollment, cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    internal static string AdmissionSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        $"DO $admission$ BEGIN {Guard(context, enrollment)} {ExactBodies(context, enrollment)} END $admission$;";

    internal static string AssignmentSourceAttestationSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        $"{Guard(context, enrollment)} {ExactBodies(context, enrollment)}";

    private static string Touch(ExploreDbContext context) => $"""
        UPDATE {Names(context).Name(Anchor)} SET revision = NOT revision WHERE id = 1;
        IF NOT FOUND THEN RAISE EXCEPTION 'human_assignment_anchor_missing' USING ERRCODE = '55000'; END IF;
        """;

    private static string Caller(PostgresNativeQueueEnrollment enrollment, bool acquire) => $"""
        {PostgresPrivilegeGuard.RestrictedCallerSql}
        IF current_user <> '{OwnerRole}'
            OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
            OR pg_catalog.current_setting('session_replication_role') <> 'origin'
            {(acquire ? "OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'" : "")}
            OR NOT COALESCE((
                p_purpose = 1 AND session_user = {Text(enrollment.RuntimeLogin)}
                    AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid = p_tenant
                OR p_purpose IN (2,3) AND session_user = {Text(enrollment.GlobalLogin)}
                    AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NULL),false)
            OR p_actor IS NULL OR p_actor = '00000000-0000-0000-0000-000000000000'::uuid
            OR p_tenant IS NULL OR p_tenant = '00000000-0000-0000-0000-000000000000'::uuid
            OR p_events IS NULL OR pg_catalog.cardinality(p_events) > {HumanEventAssignmentSourceFacts.MaximumEvents}
            OR COALESCE(pg_catalog.array_ndims(p_events),1) <> 1
            OR EXISTS (SELECT 1 FROM pg_catalog.unnest(p_events) e
                WHERE e IS NULL OR e = '00000000-0000-0000-0000-000000000000'::uuid)
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE (pg_catalog.pg_has_role(session_user,r.oid,'USAGE') OR pg_catalog.pg_has_role(session_user,r.oid,'SET'))
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
        THEN RAISE EXCEPTION 'human_assignment_caller_mismatch' USING ERRCODE = '42501'; END IF;
        """;

    private static string Body(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, bool acquire)
    {
        var vocabulary = new HumanEventAssignmentSourceSql(context, 0);
        var queries = vocabulary.Queries.Select((query, index) => (Name: $"q{index}", Query: query))
            .Concat(vocabulary.TeamQueries).ToArray();
        string locks = acquire ? string.Join("\n", queries.Select(query =>
            $"PERFORM 1 FROM ({Native(query.Query)} FOR UPDATE) held;")) : "";
        string rows = string.Join("\n", queries.Select(query => $"""
            result := result || pg_catalog.jsonb_build_object('{query.Name}',
                (SELECT COALESCE(pg_catalog.jsonb_agg(pg_catalog.to_jsonb(facts_row)), '[]'::jsonb)
                    FROM ({Native(query.Query)}) facts_row));
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

    private static string WriteBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = Names(context);
        string tables = string.Join(",", HumanEventAssignmentSourceSql.Sources.Select(source => $"{Text(sql.Table(source.Type))}::regclass"));
        return $"""
            BEGIN
                IF TG_WHEN <> 'BEFORE' OR TG_LEVEL <> 'STATEMENT'
                    OR TG_OP NOT IN ('INSERT','UPDATE','DELETE','TRUNCATE') OR TG_RELID NOT IN ({tables})
                THEN RAISE EXCEPTION 'human_assignment_trigger_target' USING ERRCODE = '55000'; END IF;
                {Guard(context, enrollment)}
                {Touch(context)}
                RETURN NULL;
            END;
            """;
    }

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
            THEN RAISE EXCEPTION 'human_assignment_body_drift' USING ERRCODE = '55000'; END IF;
            """));

    private static PostgresPrivilegeGuard.Contract Privileges(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        var sql = Names(context);
        return new(sql.Schema, Roles,
            [new(Observe, Signature, OwnerRole, [RuntimeRole, GlobalRole]),
                new(Acquire, Signature, OwnerRole, [RuntimeRole, GlobalRole]), new(Write, "", OwnerRole, [OwnerRole])],
            [.. HumanEventAssignmentSourceSql.Sources.Select(source => new PostgresPrivilegeGuard.Relation(
                context.Model.FindEntityType(source.Type)!.GetTableName()!, OwnerRole, [],
                source.Columns.Select((column, index) => new PostgresPrivilegeGuard.Column(sql.ColumnName(source.Type, column),
                    index == 0 ? ["SELECT", "UPDATE"] : ["SELECT"])).ToArray())),
                new(Anchor, OwnerRole, [], [new("id", ["SELECT"]), new("revision", ["SELECT", "UPDATE"])])],
            [new(OwnerRole, MigratorRole, false, true, false), new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(RuntimeRole, enrollment.RuntimeLogin, true, false, false), new(GlobalRole, enrollment.GlobalLogin, true, false, false)]);
    }
}
