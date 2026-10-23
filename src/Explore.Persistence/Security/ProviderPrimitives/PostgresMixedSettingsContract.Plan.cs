using Explore.Application.Features.InstanceAdmin.Plans;
using Explore.Application.Features.Settings;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Persistence.Security;

public static partial class PostgresMixedSettingsContract
{
    internal static Task WritePlanAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        TypedSettingsOriginal original, string rows, CancellationToken token) =>
        ExecuteTypedAsync(context, enrollment, original, "plan_write", rows, token);

    private static string PlanData(ExploreDbContext context)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string rows = string.Join(",", MixedSettingsDataImage.PlanReadTypes.Select(type =>
            $"{Text(type.Name)},(SELECT COALESCE(pg_catalog.jsonb_agg({Projection(context, type, "r")} ORDER BY r.{sql.Column(type, "Id")}),"
            + $"'[]'::jsonb) FROM {sql.Table(type)} r WHERE {MixedSettingsDataImage.PlanPredicate(context, type, "(p_request->'PlanAssignment'->>'AssignmentId')::uuid")})"));
        return $"CASE WHEN p_request->>'Producer' = 'tenant-plan-assignment' THEN pg_catalog.jsonb_build_object({rows})"
            + " ELSE pg_catalog.jsonb_build_object() END";
    }

    private static string PlanCompile(ExploreDbContext context)
    {
        return $"""
            SELECT COALESCE(pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
                'Key',s->>'SettingKey','Value',s->>'JsonValue','Remove',false,
                'IsLocked',CASE WHEN s->>'SettingKey' IN ({string.Join(",", PublicationPolicySettingKeys.All.Select(Text))})
                    THEN 'null'::jsonb ELSE s->'IsLocked' END) ORDER BY s->>'SettingKey'),'[]'::jsonb)
            INTO original_effects
            FROM pg_catalog.jsonb_array_elements(({FunctionSql(context, "typed_data")}(p_tenant,p_request))->'TenantPlanVersionSetting') s
            WHERE EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(
                    ({FunctionSql(context, "typed_data")}(p_tenant,p_request))->'TenantPlanAssignment') a
                WHERE (a->>'TenantId')::uuid = p_tenant
                    AND (a->>'TenantPlanAssignmentStatusId')::integer = {(int)TenantPlanAssignmentStatusEnum.Active});
            IF original_effects IS DISTINCT FROM COALESCE(
                (SELECT pg_catalog.jsonb_agg(value ORDER BY value->>'Key') FROM pg_catalog.jsonb_array_elements(p_accepted)),'[]'::jsonb)
            THEN RAISE EXCEPTION 'typed_settings_plan_content_changed' USING ERRCODE = '42501'; END IF;
            """;
    }

    private static string PlanCompletion(ExploreDbContext context) => $"""
        IF admitted.request->>'Producer' = 'tenant-plan-assignment' AND p_expected->'Rejected' = 'false'::jsonb THEN
            IF (pg_catalog.jsonb_array_length(admitted.accepted) > 0 OR EXISTS (
                SELECT 1 FROM pg_catalog.jsonb_array_elements(p_expected->'Data'->'TenantPlanVersionQuota') q
                    WHERE q->>'QuotaKey' = {Text(TenantPlanQuotaKeys.StorageBytes)}))
                AND NOT (admitted.completed ? '$plan-audit')
            THEN RAISE EXCEPTION 'typed_settings_plan_audit_omitted' USING ERRCODE = '42501'; END IF;
            IF EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(admitted.effects) e
                WHERE e->>'Type' = 'TenantSetting'
                    AND e->'After'->>'SettingKey' IN ({string.Join(",", EmailDeliverySettingKeys.All.Select(Text))})
                    AND (e->'Before'->'Value' IS DISTINCT FROM e->'After'->'Value'
                        OR e->'Before'->'IsLocked' IS DISTINCT FROM e->'After'->'IsLocked'))
                AND NOT (admitted.completed ? '$plan-email-control')
            THEN RAISE EXCEPTION 'typed_settings_plan_email_revision_omitted' USING ERRCODE = '42501'; END IF;
        END IF;
        """;

    private static string PlanWriteBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string branches = string.Join("\n", new[] { typeof(TenantSetting), typeof(EmailDispatchTenantControl), typeof(TenantPlanAssignment) }.Select(type =>
        {
            var properties = context.Model.FindEntityType(type)!.GetProperties().Select(property => property.Name)
                .Order(StringComparer.Ordinal).ToArray();
            string table = sql.Table(type);
            string id = sql.Column(type, "Id");
            string mapped = "pg_catalog.jsonb_build_object(" + string.Join(",", properties.Select(property =>
                $"{Text(sql.ColumnName(type, property))},after_row->{Text(property)}")) + ")";
            string[] mutable = type == typeof(TenantSetting) ? ["Value", "IsLocked", "UpdatedAt", "UpdatedBy"]
                : type == typeof(TenantPlanAssignment) ? ["UpdatedAt", "UpdatedBy"]
                : ["DeliveryPolicyRevision", "OptionalSuppressedThroughRevision", "OptionalSuppressedThroughUtc", "UpdatedAt", "UpdatedBy"];
            string guard = type == typeof(TenantSetting) ? $"""
                key := after_row->>'SettingKey';
                SELECT value INTO effect FROM pg_catalog.jsonb_array_elements(admitted.accepted) WHERE value->>'Key' = key;
                IF NOT FOUND OR key IN ({string.Join(",", PublicationPolicySettingKeys.All.Select(Text))})
                    OR effect->'IsLocked' = 'null'::jsonb
                    OR after_row->>'Value' IS DISTINCT FROM effect->>'Value'
                    OR after_row->'IsLocked' IS DISTINCT FROM effect->'IsLocked'
                    OR EXISTS (SELECT 1 FROM {sql.Table(typeof(SystemSetting))} ceiling
                        WHERE ceiling.{sql.Column(typeof(SystemSetting), "SettingKey")} = key
                            AND ceiling.{sql.Column(typeof(SystemSetting), "IsLocked")})
                THEN RAISE EXCEPTION 'typed_settings_plan_effect_widened' USING ERRCODE = '42501'; END IF;
                """ : type == typeof(TenantPlanAssignment) ? $"""
                key := '$plan-audit';
                IF stored IS NULL OR (after_row->>'Id')::uuid IS DISTINCT FROM (admitted.request->'PlanAssignment'->>'AssignmentId')::uuid
                    OR (stored->>'TenantPlanAssignmentStatusId')::integer IS DISTINCT FROM {(int)TenantPlanAssignmentStatusEnum.Active}
                    OR NOT EXISTS (SELECT 1 FROM {sql.Table(typeof(TenantPlanVersion))} version
                        WHERE version.{sql.Column(typeof(TenantPlanVersion), "Id")} = (stored->>'TenantPlanVersionId')::uuid)
                    OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(admitted.accepted) e
                        WHERE NOT (admitted.completed ? (e->>'Key')))
                    OR pg_catalog.jsonb_array_length(admitted.accepted) = 0 AND NOT EXISTS (
                        SELECT 1 FROM {sql.Table(typeof(TenantPlanVersionQuota))} quota
                        WHERE quota.{sql.Column(typeof(TenantPlanVersionQuota), "TenantPlanVersionId")} = (stored->>'TenantPlanVersionId')::uuid
                            AND quota.{sql.Column(typeof(TenantPlanVersionQuota), "QuotaKey")} = {Text(TenantPlanQuotaKeys.StorageBytes)})
                THEN RAISE EXCEPTION 'typed_settings_plan_audit_widened' USING ERRCODE = '42501'; END IF;
                SELECT quota.{sql.Column(typeof(TenantPlanVersionQuota), "Limit")} INTO quota_limit
                    FROM {sql.Table(typeof(TenantPlanVersionQuota))} quota
                    WHERE quota.{sql.Column(typeof(TenantPlanVersionQuota), "TenantPlanVersionId")} = (stored->>'TenantPlanVersionId')::uuid
                        AND quota.{sql.Column(typeof(TenantPlanVersionQuota), "QuotaKey")} = {Text(TenantPlanQuotaKeys.StorageBytes)};
                SELECT setting.{sql.Column(typeof(SystemSetting), "Value")} INTO ceiling_text
                    FROM {sql.Table(typeof(SystemSetting))} setting
                    WHERE setting.{sql.Column(typeof(SystemSetting), "SettingKey")} = {Text(Explore.Domain.Constants.GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes)};
                BEGIN
                    ceiling_value := ceiling_text::bigint;
                EXCEPTION WHEN invalid_text_representation OR numeric_value_out_of_range THEN
                    BEGIN
                        ceiling_value := (ceiling_text::jsonb #>> ARRAY[]::text[])::bigint;
                    EXCEPTION WHEN invalid_text_representation OR numeric_value_out_of_range THEN ceiling_value := NULL;
                    END;
                END;
                IF quota_limit > ceiling_value
                THEN RAISE EXCEPTION 'typed_settings_plan_quota_ceiling_exceeded' USING ERRCODE = '42501'; END IF;
                """ : $"""
                key := '$plan-email-control';
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(admitted.effects) e
                        WHERE e->>'Type' = 'TenantSetting'
                            AND e->'After'->>'SettingKey' IN ({string.Join(",", EmailDeliverySettingKeys.All.Select(Text))})
                            AND (e->'Before'->'Value' IS DISTINCT FROM e->'After'->'Value'
                                OR e->'Before'->'IsLocked' IS DISTINCT FROM e->'After'->'IsLocked'))
                    OR (after_row->>'DeliveryPolicyRevision')::bigint IS DISTINCT FROM
                        COALESCE((stored->>'DeliveryPolicyRevision')::bigint,0) + 1
                    OR (after_row->>'OptionalSuppressedThroughRevision')::bigint > (after_row->>'DeliveryPolicyRevision')::bigint
                THEN RAISE EXCEPTION 'typed_settings_plan_email_control_widened' USING ERRCODE = '42501'; END IF;
                """;
            return $"""
                WHEN {Text(type.Name)} THEN
                    SELECT {Projection(context, type, "r")} INTO stored FROM {table} r WHERE r.{id} = (item->>'Id')::uuid;
                    IF stored IS DISTINCT FROM NULLIF(item->'Before','null'::jsonb)
                        OR pg_catalog.jsonb_typeof(after_row) IS DISTINCT FROM 'object'
                        OR (after_row->>'Id')::uuid IS DISTINCT FROM (item->>'Id')::uuid
                        OR (after_row->>'TenantId')::uuid IS DISTINCT FROM p_tenant
                        OR (SELECT count(*) FROM pg_catalog.jsonb_object_keys(after_row)) <> {properties.Length}
                        OR after_row - ARRAY[{string.Join(",", properties.Select(Text))}] <> pg_catalog.jsonb_build_object()
                        OR stored IS NOT NULL AND (stored - ARRAY[{string.Join(",", mutable.Select(Text))}])
                            IS DISTINCT FROM (after_row - ARRAY[{string.Join(",", mutable.Select(Text))}])
                        OR stored IS NULL AND (after_row->>'CreatedBy')::uuid IS DISTINCT FROM admitted.actor
                        OR stored IS DISTINCT FROM after_row AND (
                            (after_row->>'UpdatedBy')::uuid IS DISTINCT FROM admitted.actor
                            OR after_row->>'UpdatedAt' IS NULL)
                    THEN RAISE EXCEPTION 'typed_settings_plan_noncanonical_row' USING ERRCODE = '42501'; END IF;
                    {guard}
                    IF admitted.completed ? key
                    THEN RAISE EXCEPTION 'typed_settings_plan_replayed_writer' USING ERRCODE = '42501'; END IF;
                    {EmailProjectionGuard(context, type)}
                    IF stored IS NULL THEN
                        {EmailWitnessBegin(context, type, mapped, "item->>'Id'", "'INSERT'")}
                        {(type == typeof(TenantPlanAssignment) ? "RAISE EXCEPTION 'typed_settings_plan_assignment_absent' USING ERRCODE = '42501';"
                            : $"INSERT INTO {table} ({string.Join(",", properties.Select(property => sql.Column(type, property)))})"
                                + $" SELECT {string.Join(",", properties.Select(property => $"n.{sql.Column(type, property)}"))}"
                                + $" FROM pg_catalog.jsonb_populate_record(NULL::{table},{mapped}) n;")}
                        {EmailWitnessEnd(context, type, "item->>'Id'")}
                    ELSIF stored IS DISTINCT FROM after_row THEN
                        {EmailWitnessBegin(context, type, mapped, "item->>'Id'", "'UPDATE'")}
                        UPDATE {table} SET {string.Join(",", mutable.Select(property => $"{sql.Column(type, property)} = n.{sql.Column(type, property)}"))}
                            FROM pg_catalog.jsonb_populate_record(NULL::{table},{mapped}) n WHERE {table}.{id} = n.{id};
                        {EmailWitnessEnd(context, type, "item->>'Id'")}
                    END IF;
                """;
        }));
        return $"""
            DECLARE admitted {sql.Name(AttemptTable)}%ROWTYPE; item jsonb; after_row jsonb; stored jsonb; effect jsonb; key text;
                quota_limit bigint; ceiling_text text; ceiling_value bigint; email_image jsonb; email_baseline jsonb;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, true)}
                SELECT * INTO admitted FROM {sql.Name(AttemptTable)}
                    WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid()
                        AND tenant IS NOT DISTINCT FROM p_tenant FOR UPDATE;
                IF NOT FOUND OR admitted.request->>'Producer' IS DISTINCT FROM 'tenant-plan-assignment'
                    OR pg_catalog.jsonb_typeof(admitted.accepted) IS DISTINCT FROM 'array'
                    OR pg_catalog.jsonb_typeof(p_rows) IS DISTINCT FROM 'array' OR pg_catalog.jsonb_array_length(p_rows) = 0
                    OR admitted.completed ? '$plan-audit'
                THEN RAISE EXCEPTION 'typed_settings_original_plan_required' USING ERRCODE = '42501'; END IF;
                FOR item IN SELECT value FROM pg_catalog.jsonb_array_elements(p_rows) LOOP
                    IF (item->>'Id')::uuid IS NULL OR (item->>'Id')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
                    THEN RAISE EXCEPTION 'typed_settings_plan_identity_required' USING ERRCODE = '42501'; END IF;
                    after_row := item->'After';
                    CASE item->>'Type'
                        {branches}
                        ELSE RAISE EXCEPTION 'typed_settings_plan_foreign_effect' USING ERRCODE = '42501';
                    END CASE;
                    admitted.completed := admitted.completed || pg_catalog.jsonb_build_object(key,true);
                    admitted.effects := admitted.effects || pg_catalog.jsonb_build_array(item);
                END LOOP;
                UPDATE {sql.Name(AttemptTable)} SET completed = admitted.completed, effects = admitted.effects
                    WHERE attempt = admitted.attempt AND backend = admitted.backend;
            END;
            """;
    }
}
