using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Application.Responses;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresSupportAdministrationContract
{
    private static readonly (string Name, string Signature, string Arguments, string Result)[] Routines =
    [
        ("read", "jsonb", "p_request jsonb", "jsonb"),
        ("acquire", "jsonb,jsonb", "p_request jsonb,p_before jsonb", "jsonb"),
        ("compile", "jsonb,jsonb", "p_request jsonb,p_accepted jsonb", "jsonb"),
        ("apply", "jsonb", "p_request jsonb", "jsonb"),
        ("complete", "jsonb,jsonb", "p_request jsonb,p_disclosed jsonb", "jsonb"),
        ("changed", "", "", "trigger"),
        ("settlement", "", "", "trigger")
    ];

    private static string Caller(PostgresNativeQueueEnrollment enrollment, bool owner = true) => $"""
        {PostgresPrivilegeGuard.RestrictedCallerSql}
        IF current_user <> '{OwnerRole}' OR session_user <> {Text(enrollment.RuntimeLogin)}
            OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
            OR pg_catalog.current_setting('session_replication_role') <> 'origin'
            {(owner ? "OR pg_catalog.current_setting('transaction_isolation') <> 'serializable' OR p_request->>'Operation' IN ('Forwarded','Discover')" : "")}
            OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid
                IS DISTINCT FROM (p_request->>'TenantId')::uuid
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE (pg_catalog.pg_has_role(session_user,r.oid,'USAGE') OR pg_catalog.pg_has_role(session_user,r.oid,'SET'))
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
        THEN RAISE EXCEPTION 'support_administration_original_runtime_required' USING ERRCODE = '42501'; END IF;
        """;

    private static string RequestGuard => $"""
        IF p_request IS NULL OR pg_catalog.jsonb_typeof(p_request) IS DISTINCT FROM 'object'
            OR ARRAY(SELECT key FROM pg_catalog.jsonb_object_keys(p_request) key ORDER BY key COLLATE "C")
                IS DISTINCT FROM ARRAY(SELECT key FROM unnest(ARRAY['Operation','ActorId','TenantId','SessionId',
                    'TargetTenantId','TargetTenantUserId','ModeId','DurationMinutes','ReasonCode','ReasonText',
                    'TicketReference','EndReasonText','Limit','AtUtc']) key ORDER BY key COLLATE "C")
            OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_each(p_request) pair
                WHERE CASE WHEN pair.key IN ('ModeId','DurationMinutes')
                    THEN pg_catalog.jsonb_typeof(pair.value) NOT IN ('number','null')
                    WHEN pair.key = 'Limit' THEN pg_catalog.jsonb_typeof(pair.value) IS DISTINCT FROM 'number'
                    ELSE pg_catalog.jsonb_typeof(pair.value) NOT IN ('string','null') END)
            OR NOT COALESCE(p_request->>'Operation' IN ('Start','OwnStop','ForceStop','List','Audit','Forwarded','Discover'),false)
            OR NOT COALESCE((p_request->>'ActorId')::uuid <> '00000000-0000-0000-0000-000000000000'::uuid,false)
            OR NOT COALESCE((p_request->>'TenantId')::uuid <> '00000000-0000-0000-0000-000000000000'::uuid,false)
            OR p_request->>'Operation' IN ('OwnStop','ForceStop','Forwarded') AND NOT COALESCE(
                (p_request->>'SessionId')::uuid <> '00000000-0000-0000-0000-000000000000'::uuid,false)
            OR p_request->>'Operation' = 'Audit' AND p_request->>'SessionId' IS NULL
            OR p_request->>'Operation' IN ('Start','List','Discover') AND p_request->>'SessionId' IS NOT NULL
            OR p_request->>'Operation' = 'Discover' AND p_request->>'AtUtc' IS NULL
            OR p_request->>'Operation' <> 'Discover' AND p_request->>'AtUtc' IS NOT NULL
            OR p_request->>'Operation' IN ('Start','Forwarded') AND NOT COALESCE(
                (p_request->>'TargetTenantId')::uuid <> '00000000-0000-0000-0000-000000000000'::uuid,false)
            OR p_request->>'Operation' IN ('List','Audit') AND p_request->>'TargetTenantId' IS NULL
            OR p_request->>'Operation' IN ('OwnStop','ForceStop') AND p_request->>'TargetTenantId' IS NOT NULL
            OR p_request->>'Operation' = 'Forwarded' AND p_request->'TargetTenantId' IS DISTINCT FROM p_request->'TenantId'
            OR NOT COALESCE((p_request->>'Limit')::integer BETWEEN 1 AND {PaginatedResult<object>.MaxPageSize},false)
            OR p_request->>'Operation' NOT IN ('List','Audit') AND p_request->>'Limit' IS DISTINCT FROM '100'
            OR p_request->>'Operation' NOT IN ('OwnStop','ForceStop') AND p_request->>'EndReasonText' IS NOT NULL
            OR length(p_request->>'EndReasonText') > {SupportAccessSession.MaxEndReasonTextLength}
            OR p_request->>'Operation' <> 'Start' AND (
                p_request->>'ModeId' IS NOT NULL OR p_request->>'DurationMinutes' IS NOT NULL
                OR p_request->>'ReasonCode' IS NOT NULL OR p_request->>'ReasonText' IS NOT NULL
                OR p_request->>'TicketReference' IS NOT NULL OR p_request->>'TargetTenantUserId' IS NOT NULL)
            OR p_request->>'Operation' = 'Start' AND (
                NOT COALESCE((p_request->>'ModeId')::integer IN ({(int)SupportAccessModeEnum.ReadOnly},{(int)SupportAccessModeEnum.Write}),false)
                OR NOT COALESCE((p_request->>'DurationMinutes')::integer BETWEEN 1 AND
                    CASE WHEN (p_request->>'ModeId')::integer = {(int)SupportAccessModeEnum.Write} THEN 60 ELSE 240 END,false)
                OR p_request->>'TargetTenantUserId' = '00000000-0000-0000-0000-000000000000'
                OR NULLIF(btrim(p_request->>'ReasonCode'),'') IS NULL OR length(p_request->>'ReasonCode') > {SupportAccessSession.MaxReasonCodeLength}
                OR NULLIF(btrim(p_request->>'ReasonText'),'') IS NULL OR length(p_request->>'ReasonText') > {SupportAccessSession.MaxReasonTextLength}
                OR p_request->>'TicketReference' IS NULL OR length(p_request->>'TicketReference') > {SupportAccessSession.MaxTicketReferenceLength})
        THEN RAISE EXCEPTION 'support_administration_closed_request_required' USING ERRCODE = '42501'; END IF;
        """;

    private static string Projection(ExploreDbContext context, Type type, string alias)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return "pg_catalog.jsonb_build_object(" + string.Join(",", Inventory.Single(item => item.Type == type).Properties.Select(name =>
        {
            var property = context.Model.FindEntityType(type)!.FindProperty(name)!;
            string value = alias + "." + sql.Column(type, name);
            Type scalar = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            if (scalar == typeof(DateTime) || scalar == typeof(DateTimeOffset))
                value = $"pg_catalog.to_char({value} AT TIME ZONE 'UTC','YYYY-MM-DD\"T\"HH24:MI:SS.US') || '{(scalar == typeof(DateTimeOffset) ? "0+00:00" : "0Z")}'";
            else if (scalar == typeof(string) && property.GetColumnType() == "jsonb")
                value = $"({value})::text";
            return Text(name) + "," + value;
        })) + ")";
    }

    private static string ReadBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string T<T>() => sql.Table(typeof(T));
        string C<T>(string property) => sql.Column(typeof(T), property);
        string Data(Type type, string source, string order) => Text(Label(type))
            + $",(SELECT COALESCE(pg_catalog.jsonb_agg({Projection(context, type, "r")} ORDER BY {order}),'[]'::jsonb) FROM {source} r)";
        return $"""
            DECLARE result jsonb; generation bigint; planned_audit uuid;
            BEGIN
                {Guard(context, enrollment)}
                {RequestGuard}
                {Caller(enrollment, false)}
                IF EXISTS (SELECT 1 FROM {sql.Name(AttemptTable)}
                    WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid() AND completed)
                THEN RAISE EXCEPTION 'support_administration_already_completed' USING ERRCODE = '42501'; END IF;
                SELECT revision INTO STRICT generation FROM {sql.Name(SourceTable)} WHERE id = 1;
                SELECT (effect->'After'->>'Id')::uuid INTO planned_audit
                    FROM {sql.Name(AttemptTable)} a CROSS JOIN LATERAL pg_catalog.jsonb_array_elements(a.plan) effect
                    WHERE a.attempt = pg_catalog.txid_current() AND a.backend = pg_catalog.pg_backend_pid()
                        AND effect->>'Type' = 'Audits';
                WITH selected_sessions AS (
                    SELECT s.* FROM {T<SupportAccessSession>()} s
                    WHERE CASE p_request->>'Operation'
                        WHEN 'Start' THEN s.{C<SupportAccessSession>("ActorUserId")} = (p_request->>'ActorId')::uuid
                            AND s.{C<SupportAccessSession>("StatusId")} = {(int)SupportAccessSessionStatusEnum.Active}
                            AND s.{C<SupportAccessSession>("EndedAtUtc")} IS NULL
                        WHEN 'List' THEN s.{C<SupportAccessSession>("TargetTenantId")} = (p_request->>'TargetTenantId')::uuid
                        WHEN 'Discover' THEN s.{C<SupportAccessSession>("ActorUserId")} = (p_request->>'ActorId')::uuid
                            AND s.{C<SupportAccessSession>("StatusId")} = 1 AND s.{C<SupportAccessSession>("EndedAtUtc")} IS NULL
                            AND s.{C<SupportAccessSession>("ExpiresAtUtc")} > (p_request->>'AtUtc')::timestamptz
                        WHEN 'Forwarded' THEN s.{C<SupportAccessSession>("Id")} = (p_request->>'SessionId')::uuid
                            AND s.{C<SupportAccessSession>("ActorUserId")} = (p_request->>'ActorId')::uuid
                            AND s.{C<SupportAccessSession>("TargetTenantId")} = (p_request->>'TenantId')::uuid
                        ELSE s.{C<SupportAccessSession>("Id")} = (p_request->>'SessionId')::uuid END
                    ORDER BY s.{C<SupportAccessSession>("StartedAtUtc")} DESC,s.{C<SupportAccessSession>("Id")}
                    LIMIT CASE WHEN p_request->>'Operation' = 'List' THEN (p_request->>'Limit')::integer
                        WHEN p_request->>'Operation' = 'Discover' THEN 1 ELSE NULL END),
                selected_audits AS (
                    SELECT a.* FROM {T<SupportAccessAuditEvent>()} a
                    WHERE a.{C<SupportAccessAuditEvent>("Id")} = planned_audit
                        OR p_request->>'Operation' = 'Audit'
                            AND a.{C<SupportAccessAuditEvent>("SupportAccessSessionId")} = (p_request->>'SessionId')::uuid
                            AND EXISTS (SELECT 1 FROM selected_sessions s
                                WHERE s.{C<SupportAccessSession>("TargetTenantId")} = (p_request->>'TargetTenantId')::uuid)
                    ORDER BY a.{C<SupportAccessAuditEvent>("OccurredAtUtc")} DESC,a.{C<SupportAccessAuditEvent>("Id")}
                    LIMIT CASE WHEN p_request->>'Operation' = 'Audit' THEN (p_request->>'Limit')::integer ELSE NULL END),
                selected_targets AS (
                    SELECT t.* FROM {T<Tenant>()} t WHERE p_request->>'Operation' = 'Start'
                        AND t.{C<Tenant>("Id")} = (p_request->>'TargetTenantId')::uuid),
                selected_members AS (
                    SELECT m.* FROM {T<TenantUser>()} m WHERE p_request->>'Operation' = 'Start'
                        AND m.{C<TenantUser>("Id")} = (p_request->>'TargetTenantUserId')::uuid
                        AND m.{C<TenantUser>("TenantId")} = (p_request->>'TenantId')::uuid
                        AND NOT m.{C<TenantUser>("IsDeleted")})
                SELECT pg_catalog.jsonb_build_object(
                    {Data(typeof(SupportAccessSession), "selected_sessions", $"r.{C<SupportAccessSession>("StartedAtUtc")} DESC,r.{C<SupportAccessSession>("Id")}")},
                    {Data(typeof(SupportAccessAuditEvent), "selected_audits", $"r.{C<SupportAccessAuditEvent>("OccurredAtUtc")} DESC,r.{C<SupportAccessAuditEvent>("Id")}")},
                    {Data(typeof(Tenant), "selected_targets", $"r.{C<Tenant>("Id")}")},
                    {Data(typeof(TenantUser), "selected_members", $"r.{C<TenantUser>("Id")}")})
                INTO result;
                IF generation IS DISTINCT FROM (SELECT revision FROM {sql.Name(SourceTable)} WHERE id = 1)
                THEN RAISE EXCEPTION 'support_administration_read_changed' USING ERRCODE = '40001'; END IF;
                RETURN pg_catalog.jsonb_build_object('Request',p_request,'Revision',generation,'Data',result);
            END;
            """;
    }

    private static string AcquireBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            DECLARE observed jsonb;
            BEGIN
                {Guard(context, enrollment)}
                {RequestGuard}
                {Caller(enrollment)}
                UPDATE {sql.Name(SourceTable)} SET revision = revision WHERE id = 1;
                IF NOT FOUND THEN RAISE EXCEPTION 'support_administration_epoch_missing' USING ERRCODE = '55000'; END IF;
                observed := {FunctionSql(context, "read")}(p_request);
                IF p_before IS DISTINCT FROM observed
                THEN RAISE EXCEPTION 'support_administration_preimage_changed' USING ERRCODE = '40001'; END IF;
                INSERT INTO {sql.Name(AttemptTable)}
                    (attempt,backend,request,preimage,plan,postimage,result,applied,completed)
                    VALUES (pg_catalog.txid_current(),pg_catalog.pg_backend_pid(),p_request,observed,NULL,NULL,NULL,false,false);
                RETURN observed;
            END;
            """;
    }

    private static string LoadAttempt(ExploreDbContext context) => $"""
        SELECT * INTO admitted FROM {new HumanPrincipalSourceSql(context).Name(AttemptTable)}
            WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid() FOR UPDATE;
        IF NOT FOUND OR admitted.request IS DISTINCT FROM p_request OR admitted.completed
        THEN RAISE EXCEPTION 'support_administration_original_attempt_required' USING ERRCODE = '42501'; END IF;
        """;

    private static string ApplyBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string writes = string.Join("\n", Inventory.Where(source => Mutable(source.Type)).Select(source =>
        {
            string mapped = "pg_catalog.jsonb_build_object(" + string.Join(",", source.Properties.Select(property =>
                Text(sql.ColumnName(source.Type, property)) + ",effect->'After'->" + Text(property))) + ")";
            string predicate = "r." + sql.Column(source.Type, source.Key) + " = (effect->'After'->>"
                + Text(source.Key) + ")::uuid";
            string updates = string.Join(",", UpdateColumns(source.Type).Select(property =>
                sql.Column(source.Type, property) + " = n." + sql.Column(source.Type, property)));
            string update = updates.Length == 0
                ? "RAISE EXCEPTION 'support_administration_audit_append_only' USING ERRCODE = '42501';"
                : $"UPDATE {sql.Table(source.Type)} r SET {updates} FROM pg_catalog.jsonb_populate_record(NULL::{sql.Table(source.Type)},{mapped}) n WHERE {predicate};";
            return $"""
                WHEN {Text(Label(source.Type))} THEN
                    SELECT {Projection(context, source.Type, "r")} INTO stored FROM {sql.Table(source.Type)} r WHERE {predicate};
                    IF stored IS DISTINCT FROM NULLIF(effect->'Before','null'::jsonb)
                    THEN RAISE EXCEPTION 'support_administration_effect_preimage_changed' USING ERRCODE = '40001'; END IF;
                    IF stored IS NULL THEN
                        INSERT INTO {sql.Table(source.Type)} ({string.Join(",", source.Properties.Select(property => sql.Column(source.Type, property)))})
                            SELECT {string.Join(",", source.Properties.Select(property => "n." + sql.Column(source.Type, property)))}
                            FROM pg_catalog.jsonb_populate_record(NULL::{sql.Table(source.Type)},{mapped}) n;
                    ELSE
                        {update}
                    END IF;
                    GET DIAGNOSTICS affected = ROW_COUNT;
                    IF affected <> 1 THEN RAISE EXCEPTION 'support_administration_effect_cardinality' USING ERRCODE = '40001'; END IF;
                """;
        }));
        return $"""
            DECLARE admitted {sql.Name(AttemptTable)}%ROWTYPE; effect jsonb; stored jsonb; affected bigint; current_image jsonb;
            BEGIN
                {Guard(context, enrollment)}
                {RequestGuard}
                {Caller(enrollment)}
                {LoadAttempt(context)}
                IF admitted.plan IS NULL OR admitted.applied
                THEN RAISE EXCEPTION 'support_administration_plan_missing_or_replayed' USING ERRCODE = '42501'; END IF;
                current_image := {FunctionSql(context, "read")}(p_request);
                IF current_image IS DISTINCT FROM admitted.preimage
                THEN RAISE EXCEPTION 'support_administration_compiled_preimage_changed' USING ERRCODE = '40001'; END IF;
                FOR effect IN SELECT value FROM pg_catalog.jsonb_array_elements(admitted.plan) LOOP
                    CASE effect->>'Type'
                        {writes}
                        ELSE RAISE EXCEPTION 'support_administration_invalid_private_plan' USING ERRCODE = '55000';
                    END CASE;
                END LOOP;
                current_image := {FunctionSql(context, "read")}(p_request);
                IF current_image->'Data' IS DISTINCT FROM admitted.postimage
                    OR (current_image->>'Revision')::bigint IS DISTINCT FROM
                        (admitted.preimage->>'Revision')::bigint + pg_catalog.jsonb_array_length(admitted.plan)
                THEN RAISE EXCEPTION 'support_administration_group_effect_mismatch' USING ERRCODE = '40001'; END IF;
                UPDATE {sql.Name(AttemptTable)} SET applied = true WHERE attempt = admitted.attempt AND backend = admitted.backend;
                RETURN current_image;
            END;
            """;
    }

    private static string CompleteBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            DECLARE admitted {sql.Name(AttemptTable)}%ROWTYPE; observed jsonb;
            BEGIN
                {Guard(context, enrollment)}
                {RequestGuard}
                {Caller(enrollment)}
                {LoadAttempt(context)}
                IF NOT admitted.applied OR admitted.plan IS NULL
                THEN RAISE EXCEPTION 'support_administration_incomplete_group' USING ERRCODE = '42501'; END IF;
                observed := {FunctionSql(context, "read")}(p_request);
                IF observed IS DISTINCT FROM p_disclosed OR observed->'Data' IS DISTINCT FROM admitted.postimage
                    OR (observed->>'Revision')::bigint IS DISTINCT FROM
                        (admitted.preimage->>'Revision')::bigint + pg_catalog.jsonb_array_length(admitted.plan)
                THEN RAISE EXCEPTION 'support_administration_completion_changed' USING ERRCODE = '40001'; END IF;
                UPDATE {sql.Name(AttemptTable)} SET completed = true,request = pg_catalog.jsonb_build_object(),
                    preimage = pg_catalog.jsonb_build_object(),plan = '[]'::jsonb,postimage = pg_catalog.jsonb_build_object(),result = NULL
                    WHERE attempt = admitted.attempt AND backend = admitted.backend;
                RETURN observed;
            END;
            """;
    }

    private static string ChangedBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            BEGIN
                {Guard(context, enrollment)}
                IF current_user <> '{OwnerRole}' OR TG_WHEN <> 'BEFORE' OR TG_LEVEL <> 'STATEMENT'
                    OR TG_OP NOT IN ('INSERT','UPDATE','DELETE','TRUNCATE')
                    OR TG_RELID NOT IN ({string.Join(",", Inventory.Select(source => Text(sql.Table(source.Type)) + "::regclass"))})
                THEN RAISE EXCEPTION 'support_administration_writer_target' USING ERRCODE = '55000'; END IF;
                IF EXISTS (SELECT 1 FROM {sql.Name(AttemptTable)}
                    WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid() AND completed)
                THEN RAISE EXCEPTION 'support_administration_write_after_completion' USING ERRCODE = '42501'; END IF;
                UPDATE {sql.Name(SourceTable)} SET revision = revision + 1 WHERE id = 1;
                IF NOT FOUND THEN RAISE EXCEPTION 'support_administration_epoch_missing' USING ERRCODE = '55000'; END IF;
                RETURN NULL;
            END;
            """;
    }

    private static string SettlementBody(ExploreDbContext context) => $"""
        BEGIN
            IF current_user <> '{OwnerRole}' OR TG_RELID <> {Text(new HumanPrincipalSourceSql(context).Name(AttemptTable))}::regclass
                OR TG_WHEN <> 'AFTER' OR TG_LEVEL <> 'ROW' OR TG_OP <> 'INSERT'
                OR NEW.attempt <> pg_catalog.txid_current() OR NEW.backend <> pg_catalog.pg_backend_pid()
                OR NOT EXISTS (SELECT 1 FROM {new HumanPrincipalSourceSql(context).Name(AttemptTable)} a
                    WHERE a.attempt = NEW.attempt AND a.backend = NEW.backend AND a.completed AND a.applied
                        AND a.request = pg_catalog.jsonb_build_object() AND a.preimage = pg_catalog.jsonb_build_object()
                        AND a.plan = '[]'::jsonb AND a.postimage = pg_catalog.jsonb_build_object() AND a.result IS NULL)
            THEN RAISE EXCEPTION 'support_administration_private_settlement_missing' USING ERRCODE = '42501'; END IF;
            RETURN NULL;
        END;
        """;

    private static IEnumerable<(string Name, string Signature, string Arguments, string Result, string Body)> Functions(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        Routines.Select(routine => (routine.Name, routine.Signature, routine.Arguments, routine.Result, routine.Name switch
        {
            "read" => ReadBody(context, enrollment),
            "acquire" => AcquireBody(context, enrollment),
            "compile" => CompileBody(context, enrollment),
            "apply" => ApplyBody(context, enrollment),
            "complete" => CompleteBody(context, enrollment),
            "changed" => ChangedBody(context, enrollment),
            "settlement" => SettlementBody(context),
            _ => throw new InvalidOperationException("support_administration_routine_inventory_drift")
        }));
}
