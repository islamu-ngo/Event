using Explore.Application.Features.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Modules;
using Explore.Domain.Settings.Documents;

namespace Explore.Persistence.Security;

public static partial class PostgresMixedSettingsContract
{
    internal static Task WriteGovernanceAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        TypedSettingsOriginal original, string rows, CancellationToken token) =>
        ExecuteTypedAsync(context, enrollment, original, "governance_write", rows, token);

    private static string GovernanceData(ExploreDbContext context)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string rows = string.Join(",", MixedSettingsDataImage.GovernanceReadTypes.Select(type =>
            $"{Text(type.Name)},(SELECT COALESCE(pg_catalog.jsonb_agg({Projection(context, type, "r")} ORDER BY r.{sql.Column(type, "Id")}),"
            + $"'[]'::jsonb) FROM {sql.Table(type)} r WHERE {MixedSettingsDataImage.GovernancePredicate(context, type)})"));
        return $"CASE WHEN p_request ? 'Governance' THEN pg_catalog.jsonb_build_object({rows}) ELSE pg_catalog.jsonb_build_object() END";
    }

    private static string GovernanceSingleTenant(ExploreDbContext context)
    {
        var sql = new HumanPrincipalSourceSql(context);
        var type = typeof(InstanceBootstrapState);
        return $"COALESCE((SELECT b.{sql.Column(type, nameof(InstanceBootstrapState.Status))} <> {(int)InstanceBootstrapStatus.Completed}"
            + $" OR b.{sql.Column(type, nameof(InstanceBootstrapState.DeploymentMode))} = {(int)DeploymentMode.SingleTenant}"
            + $" FROM {sql.Table(type)} b ORDER BY b.{sql.Column(type, nameof(InstanceBootstrapState.Generation))} DESC,"
            + $" b.{sql.Column(type, nameof(InstanceBootstrapState.CreatedAt))} DESC,b.{sql.Column(type, "Id")} DESC LIMIT 1),true)";
    }

    private static string GovernanceEmailTenantAffected(ExploreDbContext context, string tenant)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(admitted.effects) smtp
                WHERE smtp->>'Type' = 'SystemSetting'
                    AND smtp->'After'->>'SettingKey' = {Text(GovernanceSettingKeys.TenantDelegation.LockSmtp)}
                    AND (smtp->'Before'->'Value' IS DISTINCT FROM smtp->'After'->'Value'
                        OR smtp->'Before'->'IsLocked' IS DISTINCT FROM smtp->'After'->'IsLocked')
                    AND (pg_catalog.lower(pg_catalog.btrim(COALESCE(smtp->'Before'->>'Value','true'),E' "\\t\\r\\n')) <> 'false'
                        OR pg_catalog.lower(pg_catalog.btrim(smtp->'After'->>'Value',E' "\\t\\r\\n')) <> 'false'
                        OR COALESCE((SELECT s.{sql.Column(typeof(SystemSetting), nameof(SystemSetting.IsLocked))}
                            FROM {sql.Table(typeof(SystemSetting))} s WHERE s.{sql.Column(typeof(SystemSetting), nameof(SystemSetting.SettingKey))}
                                = {Text(GovernanceSettingKeys.Email.SmtpHost)}),false)
                        OR NOT EXISTS (SELECT 1 FROM {sql.Table(typeof(TenantSetting))} s
                            WHERE s.{sql.Column(typeof(TenantSetting), nameof(TenantSetting.TenantId))} = {tenant}
                                AND s.{sql.Column(typeof(TenantSetting), nameof(TenantSetting.SettingKey))} = {Text(GovernanceSettingKeys.Email.SmtpHost)})))
            """;
    }

    private static string GovernanceCompletion(ExploreDbContext context)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            IF admitted.request ? 'Governance' AND p_expected->'Rejected' = 'false'::jsonb THEN
                IF {GovernanceSingleTenant(context)} AND admitted.request->>'Producer' = 'governance-modules'
                    AND EXISTS (SELECT 1 FROM {sql.Table(typeof(ModuleDefinition))} m
                        WHERE (m.{sql.Column(typeof(ModuleDefinition), nameof(ModuleDefinition.ModuleKey))} = 'Mod_Islamic'
                            AND admitted.request->'Governance'->'Modules'->'Patch'->'EnableIslamicModule'->'HasValue' = 'true'::jsonb
                            OR m.{sql.Column(typeof(ModuleDefinition), nameof(ModuleDefinition.ModuleKey))} = 'Mod_Tech'
                            AND admitted.request->'Governance'->'Modules'->'Patch'->'EnableTechModule'->'HasValue' = 'true'::jsonb)
                        AND NOT (admitted.completed ? ('$governance-module:' || m.{sql.Column(typeof(ModuleDefinition), "Id")}::text)))
                THEN RAISE EXCEPTION 'governance_module_projection_omitted' USING ERRCODE = '42501'; END IF;
                IF {GovernanceSingleTenant(context)} AND admitted.request->>'Producer' = 'governance-branding'
                    AND admitted.request->'Governance'->'Branding'->'Patch'->'DefaultBrandDisplayName'->'HasValue' = 'true'::jsonb
                    AND NOT EXISTS (SELECT 1 FROM {sql.Table(typeof(TenantSettingsDocument))} d
                        WHERE d.{sql.Column(typeof(TenantSettingsDocument), "TenantId")} = '{PlatformDefaults.DefaultTenantId:D}'::uuid
                            AND d.{sql.Column(typeof(TenantSettingsDocument), nameof(TenantSettingsDocument.DocumentKey))} = {Text(SettingsDocumentKeys.Tenant.Branding)}
                            AND ({GovernanceTrim("COALESCE(admitted.request->'Governance'->'Branding'->'Patch'->'DefaultBrandDisplayName'->>'Value','')")} = ''
                                OR d.{sql.Column(typeof(TenantSettingsDocument), nameof(TenantSettingsDocument.PayloadJson))}->>'displayName'
                                    = {GovernanceTrim("admitted.request->'Governance'->'Branding'->'Patch'->'DefaultBrandDisplayName'->>'Value'")}))
                THEN RAISE EXCEPTION 'governance_branding_projection_omitted' USING ERRCODE = '42501'; END IF;
                IF EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(admitted.effects) e
                    WHERE e->>'Type' = 'SystemSetting'
                        AND e->'After'->>'SettingKey' = {Text(GovernanceSettingKeys.TenantDelegation.LockSmtp)}
                        AND (e->'Before'->'Value' IS DISTINCT FROM e->'After'->'Value'
                            OR e->'Before'->'IsLocked' IS DISTINCT FROM e->'After'->'IsLocked'))
                    AND NOT (admitted.completed ? '$governance-email-processor')
                THEN RAISE EXCEPTION 'governance_smtp_revision_omitted' USING ERRCODE = '42501'; END IF;
                IF EXISTS (SELECT 1 FROM {sql.Table(typeof(Tenant))} t
                    WHERE {GovernanceEmailTenantAffected(context, $"t.{sql.Column(typeof(Tenant), "Id")}")}
                        AND NOT (admitted.completed ? ('$governance-email-tenant:' || t.{sql.Column(typeof(Tenant), "Id")}::text)))
                THEN RAISE EXCEPTION 'governance_smtp_tenant_revision_omitted' USING ERRCODE = '42501'; END IF;
            END IF;
            """;
    }

    private static string GovernanceWriteBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        Type[] types = [typeof(SystemSetting), typeof(TenantCapability), typeof(TenantSettingsDocument),
            typeof(EmailDispatchProcessorState), typeof(EmailDispatchTenantControl)];
        string branches = string.Join("\n", types.Select(type =>
        {
            var properties = context.Model.FindEntityType(type)!.GetProperties().Select(property => property.Name)
                .Order(StringComparer.Ordinal).ToArray();
            string[] mutable = type == typeof(SystemSetting) ? ["Value", "IsLocked", "UpdatedAt", "UpdatedBy"] : UpdateColumns(type);
            string table = sql.Table(type);
            string id = sql.Column(type, "Id");
            string mapped = "pg_catalog.jsonb_build_object(" + string.Join(",", properties.Select(property =>
                $"{Text(sql.ColumnName(type, property))},after_row->{Text(property)}")) + ")";
            string purpose;
            if (type == typeof(SystemSetting))
                purpose = $"""
                    key := after_row->>'SettingKey';
                    SELECT value INTO effect FROM pg_catalog.jsonb_array_elements(admitted.accepted) WHERE value->>'Key' = key;
                    IF NOT FOUND OR admitted.request->>'Producer' <> 'governance-delegation'
                        OR key IS DISTINCT FROM {Text(GovernanceSettingKeys.TenantDelegation.LockSmtp)}
                        OR after_row->>'Value' IS DISTINCT FROM effect->>'Value'
                        OR after_row->'IsLocked' IS DISTINCT FROM effect->'IsLocked'
                    THEN RAISE EXCEPTION 'governance_smtp_effect_widened' USING ERRCODE = '42501'; END IF;
                    """;
            else if (type == typeof(TenantCapability))
                purpose = $"""
                    key := '$governance-module:' || (after_row->>'ModuleId');
                    SELECT m.{sql.Column(typeof(ModuleDefinition), nameof(ModuleDefinition.ModuleKey))} INTO module_key
                        FROM {sql.Table(typeof(ModuleDefinition))} m WHERE m.{sql.Column(typeof(ModuleDefinition), "Id")} = (after_row->>'ModuleId')::uuid;
                    input_name := CASE module_key WHEN 'Mod_Islamic' THEN 'EnableIslamicModule' WHEN 'Mod_Tech' THEN 'EnableTechModule' ELSE NULL END;
                    IF admitted.request->>'Producer' <> 'governance-modules' OR NOT ({GovernanceSingleTenant(context)})
                        OR input_name IS NULL
                        OR (after_row->>'TenantId')::uuid IS DISTINCT FROM '{PlatformDefaults.DefaultTenantId:D}'::uuid
                        OR admitted.request->'Governance'->'Modules'->'Patch'->input_name->'HasValue' IS DISTINCT FROM 'true'::jsonb
                        OR after_row->'IsEnabled' IS DISTINCT FROM admitted.request->'Governance'->'Modules'->'Patch'->input_name->'Value'
                        OR stored IS NULL AND after_row->'ConfigurationJson' IS DISTINCT FROM 'null'::jsonb
                        OR stored IS NOT NULL AND stored->>'EnabledAt' <> '0001-01-01T00:00:00.0000000Z'
                            AND (after_row->'EnabledAt' IS DISTINCT FROM stored->'EnabledAt' OR after_row->'EnabledBy' IS DISTINCT FROM stored->'EnabledBy')
                        OR stored IS NULL AND (after_row->>'EnabledBy')::uuid IS DISTINCT FROM admitted.actor
                    THEN RAISE EXCEPTION 'governance_module_effect_widened' USING ERRCODE = '42501'; END IF;
                    """;
            else if (type == typeof(TenantSettingsDocument))
                purpose = $"""
                    key := '$governance-branding';
                    display_name := {GovernanceTrim("COALESCE(admitted.request->'Governance'->'Branding'->'Patch'->'DefaultBrandDisplayName'->>'Value','')")};
                    IF stored IS NULL AND display_name = '' THEN
                        SELECT {GovernanceTrim($"t.{sql.Column(typeof(Tenant), nameof(Tenant.FullName))}")} INTO display_name
                            FROM {sql.Table(typeof(Tenant))} t WHERE t.{sql.Column(typeof(Tenant), "Id")} = '{PlatformDefaults.DefaultTenantId:D}'::uuid;
                    END IF;
                    expected_payload := pg_catalog.jsonb_build_object('displayName',NULLIF(display_name,''),
                        'logoUrl',COALESCE(stored->'PayloadJson'->'logoUrl','null'::jsonb),
                        'faviconUrl',COALESCE(stored->'PayloadJson'->'faviconUrl','null'::jsonb),
                        'customCssUrl',COALESCE(stored->'PayloadJson'->'customCssUrl','null'::jsonb));
                    IF admitted.request->>'Producer' <> 'governance-branding' OR NOT ({GovernanceSingleTenant(context)})
                        OR admitted.request->'Governance'->'Branding'->'Patch'->'DefaultBrandDisplayName'->'HasValue' IS DISTINCT FROM 'true'::jsonb
                        OR (after_row->>'TenantId')::uuid IS DISTINCT FROM '{PlatformDefaults.DefaultTenantId:D}'::uuid
                        OR after_row->>'DocumentKey' IS DISTINCT FROM {Text(SettingsDocumentKeys.Tenant.Branding)}
                        OR stored IS NOT NULL AND COALESCE(display_name,'') = ''
                        OR after_row->'PayloadJson' IS DISTINCT FROM expected_payload
                        OR stored IS NULL AND ((after_row->>'SchemaVersion')::integer <> {TenantBrandingSettingsDocumentDefaults.SchemaVersion}
                            OR after_row->>'DefaultsVersion' IS DISTINCT FROM {Text(TenantBrandingSettingsDocumentDefaults.DefaultsVersion)})
                        OR (after_row->>'ConcurrencyStamp')::uuid IS NULL
                        OR (after_row->>'ConcurrencyStamp')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
                        OR stored IS NOT NULL AND after_row->'ConcurrencyStamp' IS NOT DISTINCT FROM stored->'ConcurrencyStamp'
                    THEN RAISE EXCEPTION 'governance_branding_effect_widened' USING ERRCODE = '42501'; END IF;
                    """;
            else
                purpose = $"""
                    key := {(type == typeof(EmailDispatchProcessorState) ? "'$governance-email-processor'" : "'$governance-email-tenant:' || (after_row->>'TenantId')")};
                    IF admitted.request->>'Producer' <> 'governance-delegation'
                        OR NOT EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(admitted.effects) e
                            WHERE e->>'Type' = 'SystemSetting' AND e->'After'->>'SettingKey' = {Text(GovernanceSettingKeys.TenantDelegation.LockSmtp)}
                                AND (e->'Before'->'Value' IS DISTINCT FROM e->'After'->'Value'
                                    OR e->'Before'->'IsLocked' IS DISTINCT FROM e->'After'->'IsLocked'))
                        OR (after_row->>'DeliveryPolicyRevision')::bigint IS DISTINCT FROM COALESCE((stored->>'DeliveryPolicyRevision')::bigint,0) + 1
                        OR (after_row->>'OptionalSuppressedThroughRevision')::bigint > (after_row->>'DeliveryPolicyRevision')::bigint
                        OR {(type == typeof(EmailDispatchProcessorState)
                            ? $"after_row->>'ProcessorCode' IS DISTINCT FROM {Text(Explore.Persistence.Repositories.EmailDispatchOutboxRepository.SmtpProcessorCode)}"
                            : $"NOT EXISTS (SELECT 1 FROM {sql.Table(typeof(Tenant))} t WHERE t.{sql.Column(typeof(Tenant), "Id")} = (after_row->>'TenantId')::uuid)"
                                + $" OR NOT ({GovernanceEmailTenantAffected(context, "(after_row->>'TenantId')::uuid")})")}
                    THEN RAISE EXCEPTION 'governance_email_projection_widened' USING ERRCODE = '42501'; END IF;
                    """;
            return $"""
                WHEN {Text(type.Name)} THEN
                    SELECT {Projection(context, type, "r")} INTO stored FROM {table} r WHERE r.{id} = (item->>'Id')::uuid;
                    IF stored IS DISTINCT FROM NULLIF(item->'Before','null'::jsonb)
                        OR pg_catalog.jsonb_typeof(after_row) IS DISTINCT FROM 'object'
                        OR (after_row->>'Id')::uuid IS DISTINCT FROM (item->>'Id')::uuid
                        OR (SELECT count(*) FROM pg_catalog.jsonb_object_keys(after_row)) <> {properties.Length}
                        OR after_row - ARRAY[{string.Join(",", properties.Select(Text))}] <> pg_catalog.jsonb_build_object()
                        OR stored IS NOT NULL AND (stored - ARRAY[{string.Join(",", mutable.Select(Text))}])
                            IS DISTINCT FROM (after_row - ARRAY[{string.Join(",", mutable.Select(Text))}])
                        OR after_row ? 'CreatedBy' AND stored IS NULL AND (after_row->>'CreatedBy')::uuid IS DISTINCT FROM admitted.actor
                        OR after_row->>'UpdatedAt' IS NOT NULL AND (after_row->>'UpdatedBy')::uuid IS DISTINCT FROM admitted.actor
                    THEN RAISE EXCEPTION 'governance_noncanonical_row' USING ERRCODE = '42501'; END IF;
                    {purpose}
                    IF admitted.completed ? key
                    THEN RAISE EXCEPTION 'governance_replayed_writer' USING ERRCODE = '42501'; END IF;
                    {EmailProjectionGuard(context, type)}
                    IF stored IS NULL THEN
                        {EmailWitnessBegin(context, type, mapped, "item->>'Id'", "'INSERT'")}
                        INSERT INTO {table} ({string.Join(",", properties.Select(property => sql.Column(type, property)))})
                            SELECT {string.Join(",", properties.Select(property => $"n.{sql.Column(type, property)}"))}
                            FROM pg_catalog.jsonb_populate_record(NULL::{table},{mapped}) n;
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
            DECLARE admitted {sql.Name(AttemptTable)}%ROWTYPE; item jsonb; after_row jsonb; stored jsonb; effect jsonb;
                key text; module_key text; input_name text; display_name text; expected_payload jsonb;
                email_image jsonb; email_baseline jsonb;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, true)}
                SELECT * INTO admitted FROM {sql.Name(AttemptTable)}
                    WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid()
                        AND tenant IS NOT DISTINCT FROM p_tenant FOR UPDATE;
                IF NOT FOUND OR p_tenant IS NOT NULL OR NOT (admitted.request ? 'Governance')
                    OR pg_catalog.jsonb_typeof(admitted.accepted) IS DISTINCT FROM 'array'
                    OR pg_catalog.jsonb_typeof(p_rows) IS DISTINCT FROM 'array' OR pg_catalog.jsonb_array_length(p_rows) = 0
                THEN RAISE EXCEPTION 'governance_original_writer_required' USING ERRCODE = '42501'; END IF;
                FOR item IN SELECT value FROM pg_catalog.jsonb_array_elements(p_rows) LOOP
                    IF (item->>'Id')::uuid IS NULL OR (item->>'Id')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
                    THEN RAISE EXCEPTION 'governance_row_identity_required' USING ERRCODE = '42501'; END IF;
                    after_row := item->'After';
                    CASE item->>'Type'
                        {branches}
                        ELSE RAISE EXCEPTION 'governance_foreign_effect' USING ERRCODE = '42501';
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
