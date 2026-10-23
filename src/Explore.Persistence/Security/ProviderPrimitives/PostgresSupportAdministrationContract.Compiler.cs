using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Persistence.Security;

public static partial class PostgresSupportAdministrationContract
{
    private const string UuidV7Pattern = "^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$";

    private static string Row(Type type, params (string Property, string Expression)[] values)
    {
        var source = Inventory.Single(item => item.Type == type);
        if (values.Length != source.Properties.Length
            || !source.Properties.Order(StringComparer.Ordinal).SequenceEqual(
                values.Select(item => item.Property).Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidOperationException("support_administration_compiler_inventory_drift");
        return "pg_catalog.jsonb_build_object(" + string.Join(",", values.Select(value =>
            Text(value.Property) + "," + value.Expression)) + ")";
    }

    private static string CompileBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string created = Row(typeof(SupportAccessSession),
            ("Id", "p_accepted->'SessionId'"), ("ActorUserId", "p_request->'ActorId'"),
            ("TargetTenantId", "p_request->'TargetTenantId'"), ("TargetTenantUserId", "p_request->'TargetTenantUserId'"),
            ("StatusId", $"{(int)SupportAccessSessionStatusEnum.Active}"), ("ModeId", "p_request->'ModeId'"),
            ("ReasonCode", "p_request->'ReasonCode'"), ("ReasonText", "p_request->'ReasonText'"),
            ("TicketReference", "p_request->'TicketReference'"), ("ApprovedByUserId", "NULL"),
            ("StartedAtUtc", "p_accepted->'TransitionAt'"), ("ExpiresAtUtc",
                "pg_catalog.to_char((transition_at + (p_request->>'DurationMinutes')::integer * interval '1 minute') AT TIME ZONE 'UTC','YYYY-MM-DD\"T\"HH24:MI:SS.US') || '0+00:00'"),
            ("EndedAtUtc", "NULL"), ("EndReasonId", "NULL"), ("EndReasonText", "NULL"),
            ("CreatedAt", "audit_time"), ("CreatedBy", "p_request->'ActorId'"),
            ("UpdatedAt", "NULL"), ("UpdatedBy", "NULL"), ("ConcurrencyStamp", "p_accepted->'ConcurrencyStamp'"));
        string audit = Row(typeof(SupportAccessAuditEvent),
            ("Id", "p_accepted->'AuditId'"), ("SupportAccessSessionId", "after_session->'Id'"),
            ("OccurredAtUtc", "p_accepted->'TransitionAt'"), ("EventTypeId", "event_type"),
            ("ActorUserId", "p_request->'ActorId'"), ("TargetTenantId", "after_session->'TargetTenantId'"),
            ("TargetTenantUserId", "after_session->'TargetTenantUserId'"), ("RouteName", "NULL"),
            ("RequestName", "NULL"), ("ResourceKind", "NULL"), ("ResourceId", "NULL"), ("Action", "NULL"),
            ("Outcome", "outcome"), ("HttpStatusCode", "NULL"), ("CorrelationId", "NULL"),
            ("TraceId", "NULL"), ("SanitizedMetadataJson", "NULL"));
        return $"""
            DECLARE admitted {sql.Name(AttemptTable)}%ROWTYPE; image jsonb; expected jsonb; original jsonb;
                after_session jsonb; after_audit jsonb; effects jsonb := '[]'::jsonb; transition_at timestamptz;
                audit_time text; terminal_status integer; end_reason integer; end_text text; event_type integer; outcome text;
            BEGIN
                {Guard(context, enrollment)}
                {RequestGuard}
                {Caller(enrollment)}
                {LoadAttempt(context)}
                IF admitted.plan IS NOT NULL OR admitted.applied OR p_accepted IS NULL
                    OR pg_catalog.jsonb_typeof(p_accepted) IS DISTINCT FROM 'object'
                    OR ARRAY(SELECT key FROM pg_catalog.jsonb_object_keys(p_accepted) key ORDER BY key COLLATE "C")
                        IS DISTINCT FROM ARRAY['AuditId','ConcurrencyStamp','SessionId','TransitionAt']::text[]
                    OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_each(p_accepted) pair
                        WHERE pg_catalog.jsonb_typeof(pair.value) NOT IN ('string','null'))
                THEN RAISE EXCEPTION 'support_administration_compile_once_required' USING ERRCODE = '42501'; END IF;
                IF {FunctionSql(context, "read")}(p_request) IS DISTINCT FROM admitted.preimage
                THEN RAISE EXCEPTION 'support_administration_compile_preimage_changed' USING ERRCODE = '40001'; END IF;
                image := admitted.preimage->'Data';
                expected := image;
                IF EXISTS (SELECT 1 FROM pg_catalog.jsonb_each(p_accepted) pair WHERE pair.value <> 'null'::jsonb) THEN
                    IF p_request->>'Operation' NOT IN ('Start','OwnStop','ForceStop')
                        OR p_accepted->>'TransitionAt' IS NULL
                        OR NOT COALESCE((p_accepted->>'SessionId') ~ {Text(UuidV7Pattern)},false)
                        OR NOT COALESCE((p_accepted->>'AuditId') ~ {Text(UuidV7Pattern)},false)
                        OR NOT COALESCE((p_accepted->>'ConcurrencyStamp') ~ {Text(UuidV7Pattern)},false)
                        OR p_request->>'Operation' <> 'Start' AND p_accepted->'SessionId' IS DISTINCT FROM p_request->'SessionId'
                    THEN RAISE EXCEPTION 'support_administration_invalid_derivation' USING ERRCODE = '42501'; END IF;
                    transition_at := (p_accepted->>'TransitionAt')::timestamptz;
                    IF p_accepted->>'TransitionAt' IS DISTINCT FROM
                            pg_catalog.to_char(transition_at AT TIME ZONE 'UTC','YYYY-MM-DD"T"HH24:MI:SS.US') || '0+00:00'
                    THEN RAISE EXCEPTION 'support_administration_invalid_transition_time' USING ERRCODE = '42501'; END IF;
                    audit_time := pg_catalog.to_char(transition_at AT TIME ZONE 'UTC','YYYY-MM-DD"T"HH24:MI:SS.US') || '0Z';
                    IF EXISTS (SELECT 1 FROM {sql.Table(typeof(SupportAccessAuditEvent))} a
                        WHERE a.{sql.Column(typeof(SupportAccessAuditEvent), "Id")} = (p_accepted->>'AuditId')::uuid)
                    THEN RAISE EXCEPTION 'support_administration_audit_collision' USING ERRCODE = '42501'; END IF;
                    IF p_request->>'Operation' = 'Start' THEN
                        IF pg_catalog.jsonb_array_length(image->'Targets') <> 1
                            OR p_request->>'TargetTenantUserId' IS NOT NULL AND (
                                pg_catalog.jsonb_array_length(image->'Members') <> 1
                                OR image->'Members'->0->'TenantId' IS DISTINCT FROM p_request->'TargetTenantId')
                            OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(image->'Sessions') s
                                WHERE (s->>'StartedAtUtc')::timestamptz <= transition_at
                                    AND transition_at < (s->>'ExpiresAtUtc')::timestamptz)
                            OR NULLIF(p_request->>'TicketReference','') IS NULL
                            OR EXISTS (SELECT 1 FROM {sql.Table(typeof(SupportAccessSession))} s
                                WHERE s.{sql.Column(typeof(SupportAccessSession), "Id")} = (p_accepted->>'SessionId')::uuid)
                        THEN RAISE EXCEPTION 'support_administration_start_target_changed' USING ERRCODE = '42501'; END IF;
                        after_session := {created};
                        event_type := {(int)SupportAccessAuditEventTypeEnum.Started};
                        outcome := 'started';
                    ELSE
                        SELECT s INTO original FROM pg_catalog.jsonb_array_elements(image->'Sessions') s
                            WHERE s->'Id' = p_request->'SessionId';
                        IF original IS NULL OR original->>'ActorUserId' IS NULL
                            OR original->>'StatusId' IS DISTINCT FROM '{(int)SupportAccessSessionStatusEnum.Active}'
                            OR original->'EndedAtUtc' IS DISTINCT FROM 'null'::jsonb
                            OR transition_at < (original->>'StartedAtUtc')::timestamptz
                            OR original->'ConcurrencyStamp' = p_accepted->'ConcurrencyStamp'
                            OR p_request->>'Operation' = 'OwnStop' AND original->'ActorUserId' IS DISTINCT FROM p_request->'ActorId'
                        THEN RAISE EXCEPTION 'support_administration_original_session_required' USING ERRCODE = '42501'; END IF;
                        end_text := p_request->>'EndReasonText';
                        IF p_request->>'Operation' = 'ForceStop' THEN
                            IF transition_at >= (original->>'ExpiresAtUtc')::timestamptz
                            THEN RAISE EXCEPTION 'support_administration_force_stop_inactive' USING ERRCODE = '42501'; END IF;
                            terminal_status := {(int)SupportAccessSessionStatusEnum.Revoked};
                            end_reason := {(int)SupportAccessEndReasonEnum.ForceStopped};
                            event_type := {(int)SupportAccessAuditEventTypeEnum.Revoked}; outcome := 'force_stopped';
                        ELSIF transition_at >= (original->>'ExpiresAtUtc')::timestamptz THEN
                            terminal_status := {(int)SupportAccessSessionStatusEnum.Expired};
                            end_reason := {(int)SupportAccessEndReasonEnum.Expired};
                            event_type := {(int)SupportAccessAuditEventTypeEnum.Expired}; outcome := 'expired';
                            end_text := 'Expired before stop request.';
                        ELSE
                            terminal_status := {(int)SupportAccessSessionStatusEnum.Stopped};
                            end_reason := {(int)SupportAccessEndReasonEnum.UserStopped};
                            event_type := {(int)SupportAccessAuditEventTypeEnum.Stopped}; outcome := 'stopped';
                        END IF;
                        after_session := original || pg_catalog.jsonb_build_object(
                            'StatusId',terminal_status,'EndReasonId',end_reason,'EndedAtUtc',p_accepted->'TransitionAt',
                            'EndReasonText',end_text,'ConcurrencyStamp',p_accepted->'ConcurrencyStamp',
                            'UpdatedAt',COALESCE(NULLIF(original->'UpdatedAt','null'::jsonb),pg_catalog.to_jsonb(audit_time)),
                            'UpdatedBy',p_request->'ActorId');
                    END IF;
                    after_audit := {audit};
                    effects := pg_catalog.jsonb_build_array(
                        pg_catalog.jsonb_build_object('Type','Sessions','Before',original,'After',after_session),
                        pg_catalog.jsonb_build_object('Type','Audits','Before',NULL,'After',after_audit));
                    expected := pg_catalog.jsonb_set(expected,ARRAY['Sessions'],
                        (SELECT COALESCE(pg_catalog.jsonb_agg(s ORDER BY s->>'StartedAtUtc' DESC,s->>'Id'),'[]'::jsonb)
                            FROM (
                                SELECT s FROM pg_catalog.jsonb_array_elements(image->'Sessions') s WHERE s->'Id' <> after_session->'Id'
                                UNION ALL SELECT after_session) rows(s)));
                    expected := pg_catalog.jsonb_set(expected,ARRAY['Audits'],pg_catalog.jsonb_build_array(after_audit));
                END IF;
                UPDATE {sql.Name(AttemptTable)} SET plan = effects,postimage = expected
                    WHERE attempt = admitted.attempt AND backend = admitted.backend;
                RETURN expected;
            END;
            """;
    }
}
