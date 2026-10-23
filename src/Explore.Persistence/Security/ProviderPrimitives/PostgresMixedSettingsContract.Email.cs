using System.Globalization;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;

namespace Explore.Persistence.Security;

public static partial class PostgresMixedSettingsContract
{
    internal const string EmailConsumeSignature = "oid,text,jsonb,jsonb";
    private static string EmailClaimLock =>
        RelationalNamedLock.ComputeStableKey(EmailDispatchOutboxRepository.ClaimAdvisoryLockName)
            .ToString(CultureInfo.InvariantCulture);

    private static string EmailInitialize(ExploreDbContext context, bool typed)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            PERFORM pg_catalog.pg_advisory_xact_lock({EmailClaimLock});
            email_time := pg_catalog.clock_timestamp();
            UPDATE {sql.Name(AttemptTable)} SET email_state = pg_catalog.jsonb_build_object(
                'Pending',NULL,'Captured','[]'::jsonb,'Projected','[]'::jsonb,
                'At',pg_catalog.to_char(email_time AT TIME ZONE 'UTC','YYYY-MM-DD"T"HH24:MI:SS.US') || '0Z',
                'Data',{FunctionSql(context, typed ? "typed_data" : "data")}(p_tenant,p_request)
                    || pg_catalog.jsonb_build_object('Tenant',(
                        SELECT COALESCE(pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object('Id',t.{sql.Column(typeof(Tenant), "Id")})),
                            '[]'::jsonb) FROM {sql.Table(typeof(Tenant))} t
                            WHERE p_tenant IS NULL OR t.{sql.Column(typeof(Tenant), "Id")} = p_tenant)))
                WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid();
            RETURN email_time;
            """;
    }

    private static string EmailWitnessBegin(ExploreDbContext context, Type type, string mapped, string identity,
        string operation)
    {
        if (type != typeof(SystemSetting) && type != typeof(TenantSetting)) return "";
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            IF COALESCE(after_row,stored)->>'SettingKey' IN ({string.Join(",", EmailDeliverySettingKeys.All.Select(Text))}) THEN
                IF EXISTS (SELECT 1 FROM {sql.Name(AttemptTable)}
                    WHERE attempt = admitted.attempt AND backend = admitted.backend
                        AND email_state->'Pending' IS DISTINCT FROM 'null'::jsonb)
                THEN RAISE EXCEPTION 'mixed_email_pending_effect' USING ERRCODE = '42501'; END IF;
                UPDATE {sql.Name(AttemptTable)} SET email_state = pg_catalog.jsonb_set(email_state,ARRAY['Pending'],
                    pg_catalog.jsonb_build_object(
                        'Relation',{Text(sql.Table(type))}::regclass::oid,'Operation',{operation},
                        'Actor',admitted.actor,'Tenant',admitted.tenant,
                        'Producer',admitted.request->>'Producer',
                        'Ordinal',pg_catalog.jsonb_array_length(email_state->'Captured') + 1,
                        'Before',(SELECT pg_catalog.to_jsonb(r) FROM {sql.Table(type)} r
                            WHERE r.{sql.Column(type, "Id")} = ({identity})::uuid),
                        'After',CASE WHEN after_row IS NULL THEN NULL ELSE
                            (SELECT pg_catalog.to_jsonb(n) FROM pg_catalog.jsonb_populate_record(
                                NULL::{sql.Table(type)},{mapped}) n) END,
                        'Changed',stored IS NULL OR after_row IS NULL
                            OR stored->'Value' IS DISTINCT FROM after_row->'Value'
                            OR stored->'IsLocked' IS DISTINCT FROM after_row->'IsLocked',
                        'Consumed',false))
                    WHERE attempt = admitted.attempt AND backend = admitted.backend;
            END IF;
            """;
    }

    private static string EmailWitnessEnd(ExploreDbContext context, Type type, string identity)
    {
        if (type != typeof(SystemSetting) && type != typeof(TenantSetting)) return "";
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            IF COALESCE(after_row,stored)->>'SettingKey' IN ({string.Join(",", EmailDeliverySettingKeys.All.Select(Text))}) THEN
                IF NOT EXISTS (SELECT 1 FROM {sql.Name(AttemptTable)} a
                    WHERE a.attempt = admitted.attempt AND a.backend = admitted.backend
                        AND a.email_state->'Pending'->'Consumed' = 'true'::jsonb
                        AND a.email_state->'Pending'->'After' IS NOT DISTINCT FROM COALESCE(
                            (SELECT pg_catalog.to_jsonb(r) FROM {sql.Table(type)} r
                                WHERE r.{sql.Column(type, "Id")} = ({identity})::uuid),'null'::jsonb))
                THEN RAISE EXCEPTION 'mixed_email_effect_not_consumed' USING ERRCODE = '42501'; END IF;
                UPDATE {sql.Name(AttemptTable)} SET email_state = pg_catalog.jsonb_set(
                    pg_catalog.jsonb_set(email_state,ARRAY['Captured'],
                        (email_state->'Captured') || pg_catalog.jsonb_build_array(email_state->'Pending')),
                    ARRAY['Pending'],'null'::jsonb)
                    WHERE attempt = admitted.attempt AND backend = admitted.backend;
            END IF;
            """;
    }

    private static string EmailConsumeBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            DECLARE admitted {sql.Name(AttemptTable)}%ROWTYPE; p_tenant uuid; witness jsonb;
            BEGIN
                SELECT * INTO admitted FROM {sql.Name(AttemptTable)}
                    WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid() FOR UPDATE;
                IF NOT FOUND THEN RAISE EXCEPTION 'mixed_email_original_effect_required' USING ERRCODE = '42501'; END IF;
                p_tenant := admitted.tenant;
                {Guard(context, enrollment)}
                {Caller(enrollment, true)}
                witness := admitted.email_state->'Pending';
                IF pg_catalog.pg_trigger_depth() <> 1 OR admitted.accepted IS NULL
                    OR witness IS NULL OR witness = 'null'::jsonb
                    OR witness->'Consumed' IS DISTINCT FROM 'false'::jsonb
                    OR (witness->>'Relation')::oid IS DISTINCT FROM p_relation
                    OR witness->>'Operation' IS DISTINCT FROM p_operation
                    OR witness->'Before' IS DISTINCT FROM COALESCE(p_before,'null'::jsonb)
                    OR witness->'After' IS DISTINCT FROM COALESCE(p_after,'null'::jsonb)
                    OR (witness->>'Actor')::uuid IS DISTINCT FROM admitted.actor
                    OR (witness->>'Tenant')::uuid IS DISTINCT FROM p_tenant
                    OR witness->>'Producer' IS DISTINCT FROM admitted.request->>'Producer'
                THEN RAISE EXCEPTION 'mixed_email_effect_mismatch_or_replay' USING ERRCODE = '42501'; END IF;
                UPDATE {sql.Name(AttemptTable)} SET email_state = pg_catalog.jsonb_set(
                    email_state,ARRAY['Pending','Consumed'],'true'::jsonb)
                    WHERE attempt = admitted.attempt AND backend = admitted.backend;
            END;
            """;
    }

    private static string EmailProjectionGuard(ExploreDbContext context, Type type)
    {
        if (type != typeof(EmailDispatchProcessorState) && type != typeof(EmailDispatchTenantControl)) return "";
        var sql = new HumanPrincipalSourceSql(context);
        bool processor = type == typeof(EmailDispatchProcessorState);
        string target = processor ? "'smtp'" : "after_row->>'TenantId'";
        string baseline = processor
            ? $"r->>'ProcessorCode' = {Text(EmailDispatchOutboxRepository.SmtpProcessorCode)}"
            : "r->>'TenantId' = after_row->>'TenantId'";
        string operatorFields = processor
            ? "ARRAY['IsPaused','PauseReason','PausedAt','PausedBy','GlobalSmtpRateLimitPerMinuteOverride','OptionalRemindersDeferred','SmtpAvailableTokens','SmtpRefillAt']"
            : "ARRAY['IsPaused']";
        string targetGuard = processor
            ? $"p_tenant IS NOT NULL OR after_row->>'ProcessorCode' IS DISTINCT FROM {Text(EmailDispatchOutboxRepository.SmtpProcessorCode)}"
            : """
                p_tenant IS NOT NULL AND (after_row->>'TenantId')::uuid IS DISTINCT FROM p_tenant
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(email_image->'Data'->'Tenant') t
                    WHERE t->>'Id' = after_row->>'TenantId')
                """;
        string initialGuard = processor ? """
            OR after_row->'OptionalRemindersDeferred' IS DISTINCT FROM 'false'::jsonb
            OR EXISTS (SELECT 1 FROM unnest(ARRAY['PauseReason','PausedAt','PausedBy',
                'GlobalSmtpRateLimitPerMinuteOverride','SmtpAvailableTokens','SmtpRefillAt']) f
                WHERE after_row->f IS DISTINCT FROM 'null'::jsonb)
            """ : "";
        return $"""
            SELECT email_state INTO email_image FROM {sql.Name(AttemptTable)}
                WHERE attempt = admitted.attempt AND backend = admitted.backend;
            SELECT r INTO email_baseline FROM pg_catalog.jsonb_array_elements(
                email_image->'Data'->{Text(type.Name)}) r WHERE {baseline};
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(email_image->'Captured') c
                    WHERE c->'Consumed' = 'true'::jsonb AND c->'Changed' = 'true'::jsonb)
                OR {targetGuard}
                OR email_image->'Projected' ? ({Text(type.Name + ":")} || ({target}))
                OR COALESCE((email_baseline->>'DeliveryPolicyRevision')::bigint,0) = 9223372036854775807
                OR (after_row->>'DeliveryPolicyRevision')::bigint IS DISTINCT FROM
                    COALESCE((email_baseline->>'DeliveryPolicyRevision')::bigint,0) + 1
                OR (after_row->>'UpdatedBy')::uuid IS DISTINCT FROM admitted.actor
                OR after_row->>'UpdatedAt' IS DISTINCT FROM email_image->>'At'
                OR stored IS NOT NULL AND EXISTS (SELECT 1 FROM unnest({operatorFields}) f
                    WHERE stored->f IS DISTINCT FROM after_row->f)
                OR stored IS NULL AND (after_row->'IsPaused' IS DISTINCT FROM 'false'::jsonb
                    {initialGuard})
                OR (
                    after_row->'OptionalSuppressedThroughRevision' IS NOT DISTINCT FROM
                        COALESCE(email_baseline->'OptionalSuppressedThroughRevision','null'::jsonb)
                    AND after_row->'OptionalSuppressedThroughUtc' IS NOT DISTINCT FROM
                        COALESCE(email_baseline->'OptionalSuppressedThroughUtc','null'::jsonb)
                    OR (after_row->>'OptionalSuppressedThroughRevision')::bigint IN (
                            COALESCE((email_baseline->>'DeliveryPolicyRevision')::bigint,0),
                            COALESCE((email_baseline->>'DeliveryPolicyRevision')::bigint,0) + 1)
                        AND (after_row->>'OptionalSuppressedThroughUtc')::timestamptz IS NOT DISTINCT FROM
                            greatest((email_baseline->>'OptionalSuppressedThroughUtc')::timestamptz,
                                (email_image->>'At')::timestamptz)) IS NOT TRUE
            THEN RAISE EXCEPTION 'mixed_email_projection_receipt_mismatch' USING ERRCODE = '42501'; END IF;
            UPDATE {sql.Name(AttemptTable)} SET email_state = pg_catalog.jsonb_set(email_state,ARRAY['Projected'],
                (email_state->'Projected') || pg_catalog.jsonb_build_array({Text(type.Name + ":")} || ({target})))
                WHERE attempt = admitted.attempt AND backend = admitted.backend;
            """;
    }

    private static string EmailCompletion => """
        IF admitted.email_state->'Pending' IS DISTINCT FROM 'null'::jsonb
            OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(admitted.email_state->'Captured') c
                WHERE c->'Consumed' IS DISTINCT FROM 'true'::jsonb)
            OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(admitted.email_state->'Captured') c
                WHERE c->'Changed' = 'true'::jsonb)
                AND NOT (admitted.email_state->'Projected' ? CASE WHEN p_tenant IS NULL
                    THEN 'EmailDispatchProcessorState:smtp' ELSE 'EmailDispatchTenantControl:' || p_tenant::text END)
        THEN RAISE EXCEPTION 'mixed_email_unsettled_effect' USING ERRCODE = '42501'; END IF;
        """;
}
