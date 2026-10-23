using System.Globalization;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;

namespace Explore.Persistence.Security;

public static partial class PostgresOperatorIdentityEditorContract
{
    private static string Caller(PostgresNativeQueueEnrollment enrollment, bool transaction) => $"""
        {PostgresPrivilegeGuard.RestrictedCallerSql}
        IF current_user <> '{OwnerRole}' OR session_user <> {Text(enrollment.GlobalLogin)}
            OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
            OR pg_catalog.current_setting('session_replication_role') <> 'origin'
            OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NOT NULL
            {(transaction ? "OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'" : "")}
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE (pg_catalog.pg_has_role(session_user,r.oid,'USAGE') OR pg_catalog.pg_has_role(session_user,r.oid,'SET'))
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
        THEN RAISE EXCEPTION 'operator_editor_caller_mismatch' USING ERRCODE = '42501'; END IF;
        """;

    private static string Projection(HumanPrincipalSourceSql sql, Type type, string alias, string[] properties) =>
        "pg_catalog.jsonb_build_object(" + string.Join(",", properties.Select(property =>
        {
            string name = type == typeof(SystemSetting) && property == "SettingValueTypeId" ? "ValueType" : property;
            string value = $"{alias}.{sql.Column(type, property)}";
            if (property is "AllowedValues" or "Payload") value += "::text";
            if (property is "CreatedAt" or "UpdatedAt" or "ProcessedAt" or "NextRetryAt" or "DeadLetteredAt"
                or "SupersededAt" or "CompletedAt")
                value = $"pg_catalog.to_char({value} AT TIME ZONE 'UTC','YYYY-MM-DD\"T\"HH24:MI:SS.US\"Z\"')";
            return $"{Text(name)},{value}";
        })) + ")";

    private static string SettingProjection(HumanPrincipalSourceSql sql, string alias) =>
        Projection(sql, typeof(SystemSetting), alias, SettingColumns.Where(name => name != "SettingKey").ToArray());

    private static string ReadBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) => $"""
        DECLARE result jsonb;
        BEGIN
            {Guard(context, enrollment)}
            {Caller(enrollment, false)}
            SELECT {SettingProjection(new(context), "r")} INTO result
                FROM {new HumanPrincipalSourceSql(context).Table(typeof(SystemSetting))} r
                WHERE r.{new HumanPrincipalSourceSql(context).Column(typeof(SystemSetting), "SettingKey")}
                    COLLATE "C" = {Text(InstanceOperatorIdentitySettingKeys.OperatorIdentity)} COLLATE "C";
            RETURN result;
        END;
        """;

    private static string PendingBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string B(string property) => sql.Column(typeof(InstanceBootstrapState), property);
        return $"""
            DECLARE selected {sql.Table(typeof(InstanceBootstrapState))}%ROWTYPE; found_row boolean;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, false)}
                IF p_acquire IS NULL THEN RAISE EXCEPTION 'operator_editor_pending_mode' USING ERRCODE = '22023'; END IF;
                IF p_acquire THEN
                    {Caller(enrollment, true)}
                    PERFORM pg_catalog.pg_advisory_xact_lock({RelationalNamedLock.ComputeStableKey("explore:instance-onboarding").ToString(CultureInfo.InvariantCulture)});
                    SELECT * INTO selected FROM {sql.Table(typeof(InstanceBootstrapState))} r
                        ORDER BY r.{B("Generation")} DESC,r.{B("CreatedAt")} DESC,r.{B("Id")} DESC LIMIT 1 FOR UPDATE;
                ELSE
                    SELECT * INTO selected FROM {sql.Table(typeof(InstanceBootstrapState))} r
                        ORDER BY r.{B("Generation")} DESC,r.{B("CreatedAt")} DESC,r.{B("Id")} DESC LIMIT 1;
                END IF;
                found_row := FOUND;
                IF NOT found_row THEN RETURN NULL; END IF;
                IF selected.{B("Status")} <> {(int)InstanceBootstrapStatus.Pending}
                THEN RAISE EXCEPTION 'operator_editor_setup_not_pending' USING ERRCODE = '42501'; END IF;
                RETURN {Projection(sql, typeof(InstanceBootstrapState), "selected", BootstrapColumns)};
            END;
            """;
    }

    private static string AuditBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string A(string property) => sql.Column(typeof(OutboxMessage), property);
        return $"""
            DECLARE selected {sql.Table(typeof(OutboxMessage))}%ROWTYPE;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, false)}
                IF p_id IS NULL OR p_id = '00000000-0000-0000-0000-000000000000'::uuid
                THEN RAISE EXCEPTION 'operator_editor_audit_id' USING ERRCODE = '22023'; END IF;
                SELECT * INTO selected FROM {sql.Table(typeof(OutboxMessage))} r WHERE r.{A("Id")} = p_id;
                IF NOT FOUND THEN RETURN NULL; END IF;
                IF selected.{A("AggregateType")} COLLATE "C" <> {Text(OperatorIdentityImportAudit.AggregateType)} COLLATE "C"
                    OR selected.{A("EventType")} COLLATE "C" <> {Text(OperatorIdentityImportAudit.EventType)} COLLATE "C"
                THEN RAISE EXCEPTION 'operator_editor_audit_collision' USING ERRCODE = '42501'; END IF;
                RETURN {Projection(sql, typeof(OutboxMessage), "selected", AuditColumns)};
            END;
            """;
    }

    private static string SnapshotShape(string name)
    {
        const string instant = "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}([.][0-9]{1,6})?Z$";
        string[] properties = ["Id", "Value", "ValueType", "IsLocked", "AllowedValues", "Description",
            "Category", "DisplayOrder", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"];
        string shape = string.Join(" AND ", properties.Select(property =>
        {
            string kind = property is "ValueType" or "DisplayOrder" ? "number" : property == "IsLocked" ? "boolean" : "string";
            bool nullable = property is "AllowedValues" or "Description" or "Category" or "CreatedBy" or "UpdatedAt" or "UpdatedBy";
            return $"(pg_catalog.jsonb_typeof({name}->{Text(property)}) = '{kind}'"
                + (nullable ? $" OR {name}->{Text(property)} = 'null'::jsonb)" : ")");
        }));
        return $"""
            IF pg_catalog.jsonb_typeof({name}) IS DISTINCT FROM 'object'
            THEN RAISE EXCEPTION 'operator_editor_snapshot_shape' USING ERRCODE = '22023'; END IF;
            IF ((SELECT count(*) FROM pg_catalog.jsonb_object_keys({name})) = {properties.Length}
                AND {name} ?& ARRAY[{string.Join(",", properties.Select(Text))}] AND {shape}
                AND {name}->>'CreatedAt' ~ {Text(instant)}
                AND ({name}->'UpdatedAt' = 'null'::jsonb OR {name}->>'UpdatedAt' ~ {Text(instant)})) IS DISTINCT FROM true
            THEN RAISE EXCEPTION 'operator_editor_snapshot_shape' USING ERRCODE = '22023'; END IF;
            """;
    }

    private static string WriteBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string S(string property) => sql.Column(typeof(SystemSetting), property);
        string A(string property) => sql.Column(typeof(OutboxMessage), property);
        string key = Text(InstanceOperatorIdentitySettingKeys.OperatorIdentity);
        string Empty = "'00000000-0000-0000-0000-000000000000'::uuid";
        const string uuid7 = "^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$";
        return $"""
            DECLARE old_row {sql.Table(typeof(SystemSetting))}%ROWTYPE; old_snapshot jsonb;
                old_document jsonb; document jsonb; audit jsonb; present boolean; affected integer;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, true)}
                {SnapshotShape("p_after")}
                IF p_before IS NOT NULL THEN {SnapshotShape("p_before")} END IF;
                IF p_import IS NULL OR p_audit IS NULL OR p_audit = {Empty}
                    OR p_actor = {Empty} OR p_occurred IS NULL OR NOT pg_catalog.isfinite(p_occurred)
                    OR p_occurred < '0001-01-01T00:00:00Z'::timestamptz
                    OR p_occurred >= '10000-01-01T00:00:00Z'::timestamptz
                    OR (p_after->>'Id')::uuid IS NULL OR (p_after->>'Id')::uuid = {Empty}
                    OR (p_after->>'ValueType')::integer <> {(int)SettingValueType.Json}
                THEN RAISE EXCEPTION 'operator_editor_effect_invalid' USING ERRCODE = '22023'; END IF;
                document := (p_after->>'Value')::jsonb;
                IF (pg_catalog.jsonb_typeof(document) = 'object'
                    AND pg_catalog.jsonb_typeof(document->'operatorId') = 'string'
                    AND pg_catalog.jsonb_typeof(document->'revision') = 'string'
                    AND pg_catalog.jsonb_typeof(document->'isOfficialInstance') = 'boolean'
                    AND (document->>'operatorId')::uuid <> {Empty}
                    AND (document->>'revision')::uuid <> {Empty}) IS DISTINCT FROM true
                THEN RAISE EXCEPTION 'operator_editor_document_invalid' USING ERRCODE = '22023'; END IF;
                SELECT * INTO old_row FROM {sql.Table(typeof(SystemSetting))} r
                    WHERE r.{S("SettingKey")} COLLATE "C" = {key} COLLATE "C" FOR UPDATE;
                present := FOUND;
                IF present IS DISTINCT FROM (p_before IS NOT NULL)
                THEN RAISE EXCEPTION 'operator_editor_source_changed' USING ERRCODE = '40001'; END IF;
                IF present THEN
                    old_snapshot := {SettingProjection(sql, "old_row")};
                    IF old_snapshot - 'CreatedAt' - 'UpdatedAt' IS DISTINCT FROM p_before - 'CreatedAt' - 'UpdatedAt'
                        OR old_row.{S("CreatedAt")} IS DISTINCT FROM (p_before->>'CreatedAt')::timestamptz
                        OR old_row.{S("UpdatedAt")} IS DISTINCT FROM (p_before->>'UpdatedAt')::timestamptz
                    THEN RAISE EXCEPTION 'operator_editor_source_changed' USING ERRCODE = '40001'; END IF;
                    old_document := old_row.{S("Value")}::jsonb;
                    IF p_after - ARRAY['Value','ValueType','UpdatedAt','UpdatedBy']::text[]
                            IS DISTINCT FROM p_before - ARRAY['Value','ValueType','UpdatedAt','UpdatedBy']::text[]
                        OR (p_after->>'UpdatedBy')::uuid IS DISTINCT FROM p_actor
                        OR (p_after->>'UpdatedAt')::timestamptz IS DISTINCT FROM p_occurred
                        OR document->>'operatorId' IS DISTINCT FROM COALESCE(old_document->>'operatorId',document->>'operatorId')
                        OR document->'isOfficialInstance' IS DISTINCT FROM COALESCE(old_document->'isOfficialInstance','false'::jsonb)
                        OR document->>'revision' IS NOT DISTINCT FROM old_document->>'revision'
                    THEN RAISE EXCEPTION 'operator_editor_fixed_effect_changed' USING ERRCODE = '42501'; END IF;
                    UPDATE {sql.Table(typeof(SystemSetting))} SET {S("Value")} = p_after->>'Value',
                        {S("SettingValueTypeId")} = {(int)SettingValueType.Json},
                        {S("UpdatedAt")} = p_occurred, {S("UpdatedBy")} = p_actor
                        WHERE {S("Id")} = old_row.{S("Id")} AND {S("SettingKey")} COLLATE "C" = {key} COLLATE "C";
                ELSE
                    IF p_after->>'Id' !~ {Text(uuid7)}
                        OR (p_after->>'IsLocked')::boolean IS DISTINCT FROM false
                        OR p_after->'AllowedValues' IS DISTINCT FROM 'null'::jsonb
                        OR p_after->'Description' IS DISTINCT FROM 'null'::jsonb
                        OR p_after->'Category' IS DISTINCT FROM 'null'::jsonb
                        OR (p_after->>'DisplayOrder')::integer IS DISTINCT FROM 0
                        OR (p_after->>'CreatedBy')::uuid IS DISTINCT FROM p_actor
                        OR (p_after->>'CreatedAt')::timestamptz IS DISTINCT FROM p_occurred
                        OR p_after->'UpdatedAt' IS DISTINCT FROM 'null'::jsonb
                        OR p_after->'UpdatedBy' IS DISTINCT FROM 'null'::jsonb
                        OR document->'isOfficialInstance' IS DISTINCT FROM 'false'::jsonb
                    THEN RAISE EXCEPTION 'operator_editor_insert_metadata' USING ERRCODE = '42501'; END IF;
                    INSERT INTO {sql.Table(typeof(SystemSetting))} ({string.Join(",", SettingColumns.Select(S))})
                    VALUES ((p_after->>'Id')::uuid,{key},p_after->>'Value',{(int)SettingValueType.Json},
                        false,NULL,NULL,NULL,0,p_occurred,p_actor,NULL,NULL);
                END IF;
                GET DIAGNOSTICS affected = ROW_COUNT;
                IF affected <> 1 THEN RAISE EXCEPTION 'operator_editor_write_conflict' USING ERRCODE = '40001'; END IF;
                IF p_import THEN
                    IF p_actor IS NULL OR p_payload IS NULL OR document->'isOfficialInstance' <> 'false'::jsonb
                    THEN RAISE EXCEPTION 'operator_editor_import_effect' USING ERRCODE = '42501'; END IF;
                    audit := p_payload::jsonb;
                    IF pg_catalog.jsonb_typeof(audit) IS DISTINCT FROM 'object'
                    THEN RAISE EXCEPTION 'operator_editor_audit_shape' USING ERRCODE = '22023'; END IF;
                    IF ((SELECT count(*) FROM pg_catalog.jsonb_object_keys(audit)) = 4
                        AND audit ?& ARRAY['ActorUserId','ContentDigest','ExpectedRevisionHash','CommittedRevision']
                        AND pg_catalog.jsonb_typeof(audit->'ActorUserId') = 'string'
                        AND pg_catalog.jsonb_typeof(audit->'CommittedRevision') = 'string'
                        AND pg_catalog.jsonb_typeof(audit->'ContentDigest') = 'string'
                        AND pg_catalog.jsonb_typeof(audit->'ExpectedRevisionHash') = 'string'
                        AND length(audit->>'ContentDigest') > 0 AND length(audit->>'ExpectedRevisionHash') > 0
                        AND (audit->>'ActorUserId')::uuid = p_actor
                        AND (audit->>'CommittedRevision')::uuid = (document->>'revision')::uuid
                        AND audit::text COLLATE "C" = p_payload COLLATE "C") IS DISTINCT FROM true
                    THEN RAISE EXCEPTION 'operator_editor_audit_effect' USING ERRCODE = '42501'; END IF;
                    INSERT INTO {sql.Table(typeof(OutboxMessage))} ({string.Join(",", AuditColumns.Select(A))})
                    VALUES (p_audit,{Text(OperatorIdentityImportAudit.AggregateType)},(document->>'operatorId')::uuid,
                        {Text(OperatorIdentityImportAudit.EventType)},audit,{(int)OutboxMessageStatus.Pending},p_occurred,
                        NULL,0,NULL,NULL,5,NULL);
                ELSIF p_payload IS NOT NULL THEN
                    RAISE EXCEPTION 'operator_editor_unexpected_audit' USING ERRCODE = '42501';
                END IF;
                {Guard(context, enrollment)}
                {Caller(enrollment, true)}
            END;
            """;
    }

    private static (string Operation, string Signature, string Arguments, string Result, string Body)[] Functions(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
    [
        ("read", "", "", "jsonb", ReadBody(context, enrollment)),
        ("pending", "boolean", "p_acquire boolean", "jsonb", PendingBody(context, enrollment)),
        ("audit", "uuid", "p_id uuid", "jsonb", AuditBody(context, enrollment)),
        ("write", WriteSignature,
            "p_actor uuid,p_before jsonb,p_after jsonb,p_audit uuid,p_payload text,p_occurred timestamptz,p_import boolean",
            "void", WriteBody(context, enrollment))
    ];
}
