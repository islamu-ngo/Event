using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Settings;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

public static partial class PostgresMixedSettingsContract
{
    private static string TypedRequestGuard => $"""
        IF p_request IS NULL OR pg_catalog.jsonb_typeof(p_request) IS DISTINCT FROM 'object'
            OR (SELECT count(*) FROM pg_catalog.jsonb_object_keys(p_request)) <>
                (CASE WHEN p_request->>'Producer' = 'tenant-plan-assignment' OR p_request ? 'Governance' THEN 7 ELSE 6 END)
            OR p_request->>'Producer' NOT IN ('event-policy','tenant-onboarding','reporting-intake','tenant-plan-assignment',
                {string.Join(",", GovernanceFamilies.Select(pair => Text(pair.Producer)))})
            OR p_request->>'Producer' IS NULL
            OR (p_request ? 'Governance') IS DISTINCT FROM
                (p_request->>'Producer' IN ({string.Join(",", GovernanceFamilies.Select(pair => Text(pair.Producer)))}))
            OR (p_request->>'TenantId')::uuid IS DISTINCT FROM p_tenant
            OR (p_request->>'Actor')::uuid IS NULL
            OR (p_request->>'Actor')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
            OR (CASE p_request->>'Producer'
                WHEN 'event-policy' THEN p_tenant IS NOT NULL
                    OR pg_catalog.jsonb_typeof(p_request->'EventPolicy'->'Patch') IS DISTINCT FROM 'object'
                    OR (p_request->'EventPolicy'->>'UserId')::uuid IS DISTINCT FROM (p_request->>'Actor')::uuid
                    OR p_request->'Onboarding' IS DISTINCT FROM 'null'::jsonb
                    OR p_request->'Reporting' IS DISTINCT FROM 'null'::jsonb
                WHEN 'tenant-onboarding' THEN p_tenant IS NULL
                    OR pg_catalog.jsonb_typeof(p_request->'Onboarding'->'Settings') IS DISTINCT FROM 'object'
                    OR pg_catalog.jsonb_typeof(p_request->'Onboarding'->'DirectoryOperatorIdentity') IS DISTINCT FROM 'object'
                    OR pg_catalog.jsonb_typeof(p_request->'Onboarding'->'Settings'->'AiAssistantAllowedModelIds') IS DISTINCT FROM 'array'
                    OR (p_request->'Onboarding'->>'UserId')::uuid IS DISTINCT FROM (p_request->>'Actor')::uuid
                    OR p_request->'EventPolicy' IS DISTINCT FROM 'null'::jsonb
                    OR p_request->'Reporting' IS DISTINCT FROM 'null'::jsonb
                WHEN 'reporting-intake' THEN p_tenant IS NULL
                    OR pg_catalog.jsonb_typeof(p_request->'Reporting'->'Policy'->'Enabled') IS DISTINCT FROM 'boolean'
                    OR (p_request->'Reporting'->>'ActorUserId')::uuid IS DISTINCT FROM (p_request->>'Actor')::uuid
                    OR (p_request->'Reporting'->>'TenantId')::uuid IS DISTINCT FROM p_tenant
                    OR p_request->'EventPolicy' IS DISTINCT FROM 'null'::jsonb
                    OR p_request->'Onboarding' IS DISTINCT FROM 'null'::jsonb
                WHEN 'tenant-plan-assignment' THEN p_tenant IS NULL
                    OR pg_catalog.jsonb_typeof(p_request->'PlanAssignment') IS DISTINCT FROM 'object'
                    OR (p_request->'PlanAssignment'->>'AppliedByUserId')::uuid IS DISTINCT FROM (p_request->>'Actor')::uuid
                    OR (p_request->'PlanAssignment'->>'TenantId')::uuid IS DISTINCT FROM p_tenant
                    OR (p_request->'PlanAssignment'->>'AssignmentId')::uuid IS NULL
                    OR (p_request->'PlanAssignment'->>'AssignmentId')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
                    OR p_request->'EventPolicy' IS DISTINCT FROM 'null'::jsonb
                    OR p_request->'Onboarding' IS DISTINCT FROM 'null'::jsonb
                    OR p_request->'Reporting' IS DISTINCT FROM 'null'::jsonb
                ELSE ({GovernanceRequestInvalid}) END)
        THEN RAISE EXCEPTION 'typed_settings_original_request' USING ERRCODE = '42501'; END IF;
        """;

    internal static async Task<string> ReadTypedHumanAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment, TypedSettingsOriginal original, HumanCommitSubject subject,
        HumanAuthenticationLifetime lifetime, HumanSupportForwardingObservation forwarding,
        string? before, CancellationToken token)
    {
        await RequireReadyAsync(context, enrollment, token);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT {FunctionSql(context, before is null ? "typed_read" : "typed_acquire")}"
            + $"(@actor,@provider,@account,@tenant,@stamp,@session,@resolved,@request{(before is null ? "" : ",@before")})::text";
        void Add(string name, NpgsqlDbType type, object? value) =>
            command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });
        Add("actor", NpgsqlDbType.Uuid, subject.PrincipalId);
        Add("provider", NpgsqlDbType.Integer, (int)subject.AccountKey!.ProviderKind);
        Add("account", NpgsqlDbType.Text, subject.AccountKey.Value);
        Add("tenant", NpgsqlDbType.Uuid, original.TenantId);
        Add("stamp", NpgsqlDbType.Text, lifetime.LocalSession?.SecurityStamp);
        Add("session", NpgsqlDbType.Uuid, forwarding.SessionId);
        Add("resolved", NpgsqlDbType.Uuid, forwarding.ResolvedTenantId);
        Add("request", NpgsqlDbType.Jsonb, original.Serialize());
        if (before is not null) Add("before", NpgsqlDbType.Jsonb, before);
        return (string)(await command.ExecuteScalarAsync(token)
            ?? throw new UnauthorizedAccessException("The original typed native image is absent."));
    }

    internal static async Task<string> ReadTypedDataAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment, TypedSettingsOriginal original, CancellationToken token)
    {
        await RequireReadyAsync(context, enrollment, token);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT {FunctionSql(context, "typed_data")}(@tenant,@request)::text";
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = (object?)original.TenantId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("request", NpgsqlDbType.Jsonb) { Value = original.Serialize() });
        return (string)(await command.ExecuteScalarAsync(token)
            ?? throw new UnauthorizedAccessException("The original typed data is absent."));
    }

    internal static async Task<DateTime> CompileTypedAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        TypedSettingsOriginal original, IReadOnlyList<TypedSettingsEffect> effects, CancellationToken token)
    {
        await RequireReadyAsync(context, enrollment, token);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = $"SELECT {FunctionSql(context, "typed_compile")}(@tenant,@request,@payload)";
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = (object?)original.TenantId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("request", NpgsqlDbType.Jsonb) { Value = original.Serialize() });
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(effects) });
        return (DateTime)(await command.ExecuteScalarAsync(token)
            ?? throw new UnauthorizedAccessException("The native typed compilation time is absent."));
    }

    internal static Task WriteTypedAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        TypedSettingsOriginal original, string rows, CancellationToken token) =>
        ExecuteTypedAsync(context, enrollment, original, "typed_write", rows, token);

    internal static Task CompleteTypedAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        TypedSettingsOriginal original, string expected, CancellationToken token) =>
        ExecuteTypedAsync(context, enrollment, original, "typed_complete", expected, token);

    private static async Task ExecuteTypedAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        TypedSettingsOriginal original, string function, string payload, CancellationToken token)
    {
        await RequireReadyAsync(context, enrollment, token);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = $"SELECT {FunctionSql(context, function)}(@tenant,"
            + (function == "typed_compile" ? "@request," : "") + "@payload)";
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = (object?)original.TenantId ?? DBNull.Value });
        if (function == "typed_compile")
            command.Parameters.Add(new NpgsqlParameter("request", NpgsqlDbType.Jsonb) { Value = original.Serialize() });
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = payload });
        await command.ExecuteNonQueryAsync(token);
    }

    private static string TypedCompileBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string[] instanceKeys = [GovernanceSettingKeys.Events.UserSubmissionEnabled,
            GovernanceSettingKeys.Events.OrganizationSubmissionEnabled, GovernanceSettingKeys.Events.GroupSubmissionEnabled,
            GovernanceSettingKeys.Events.CardClickOpensDetailPage];
        string[] patchProperties = ["AllowUserSubmittedEvents", "AllowOrganizationSubmittedEvents", "AllowGroupSubmittedEvents"];
        string instanceExpected = string.Join(" UNION ALL ", instanceKeys.Take(3).Select((key, index) =>
            $"SELECT {Text(key)} AS key,(patch->{Text(patchProperties[index])}->'Value')::text AS value,false AS locked"
            + $" WHERE patch->{Text(patchProperties[index])}->'HasValue' = 'true'::jsonb"))
            + $"""
                 UNION ALL SELECT {Text(GovernanceSettingKeys.Events.CardClickOpensDetailPage)},
                    CASE WHEN patch->'EventCardClickOpensDetailPage'->'HasValue' = 'true'::jsonb
                        THEN (patch->'EventCardClickOpensDetailPage'->'Value')::text
                        ELSE CASE WHEN pg_catalog.lower(pg_catalog.btrim(COALESCE((SELECT {sql.Column(typeof(SystemSetting), nameof(SystemSetting.Value))}
                            FROM {sql.Table(typeof(SystemSetting))} WHERE {sql.Column(typeof(SystemSetting), nameof(SystemSetting.SettingKey))} =
                                {Text(GovernanceSettingKeys.Events.CardClickOpensDetailPage)}),
                            {Text(Explore.Domain.Settings.SettingRegistry.Get(GovernanceSettingKeys.Events.CardClickOpensDetailPage)!.DefaultValue)}),E' \\t\\r\\n'))
                            = 'false' THEN 'false' ELSE 'true' END END,
                    CASE WHEN patch->'LockTenantEventCardClickBehavior'->'HasValue' = 'true'::jsonb
                        THEN (patch->'LockTenantEventCardClickBehavior'->>'Value')::boolean
                        ELSE COALESCE((SELECT {sql.Column(typeof(SystemSetting), nameof(SystemSetting.IsLocked))}
                            FROM {sql.Table(typeof(SystemSetting))} WHERE {sql.Column(typeof(SystemSetting), nameof(SystemSetting.SettingKey))} =
                                {Text(GovernanceSettingKeys.Events.CardClickOpensDetailPage)}),false) END
                    WHERE patch->'EventCardClickOpensDetailPage'->'HasValue' = 'true'::jsonb
                        OR patch->'LockTenantEventCardClickBehavior'->'HasValue' = 'true'::jsonb
                """;
        return $"""
            DECLARE admitted {sql.Name(AttemptTable)}%ROWTYPE; effect jsonb; key text; patch jsonb; original_effects jsonb;
                stored_revision text; revision integer; locked boolean; input_name text; email_time timestamptz;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, true)}
                {TypedRequestGuard}
                SELECT * INTO admitted FROM {sql.Name(AttemptTable)}
                    WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid()
                        AND tenant IS NOT DISTINCT FROM p_tenant FOR UPDATE;
                IF NOT FOUND OR admitted.request IS DISTINCT FROM p_request OR admitted.accepted IS NOT NULL
                    OR pg_catalog.jsonb_typeof(p_accepted) IS DISTINCT FROM 'array'
                    OR pg_catalog.jsonb_array_length(p_accepted) = 0 AND p_request->>'Producer' <> 'tenant-plan-assignment'
                    OR (SELECT count(*) FROM pg_catalog.jsonb_array_elements(p_accepted)) <>
                        (SELECT count(DISTINCT e->>'Key') FROM pg_catalog.jsonb_array_elements(p_accepted) e)
                THEN RAISE EXCEPTION 'typed_settings_original_compilation' USING ERRCODE = '42501'; END IF;
                IF p_request->>'Producer' = 'event-policy' THEN
                    patch := p_request->'EventPolicy'->'Patch';
                    SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object('Key',e.key,'Value',e.value,'IsLocked',e.locked,'Remove',false)
                        ORDER BY e.key) INTO original_effects FROM ({instanceExpected}) e;
                    IF original_effects IS DISTINCT FROM
                        (SELECT pg_catalog.jsonb_agg(value ORDER BY value->>'Key') FROM pg_catalog.jsonb_array_elements(p_accepted))
                    THEN RAISE EXCEPTION 'typed_settings_patch_substituted' USING ERRCODE = '42501'; END IF;
                ELSIF p_request->>'Producer' = 'tenant-onboarding' THEN
                    IF ARRAY(SELECT value->>'Key' FROM pg_catalog.jsonb_array_elements(p_accepted) ORDER BY value->>'Key')
                        IS DISTINCT FROM ARRAY(SELECT requested.setting_key
                            FROM unnest(ARRAY[{string.Join(",", TenantPolicySettingService.OriginalWriteKeys.Select(Text))}]::text[]) AS requested(setting_key)
                            WHERE requested.setting_key <> {Text(GovernanceSettingKeys.PublicExperience.AnnouncementBarRevision)}
                                OR p_request->'Onboarding'->'Settings'->'ForceAnnouncementBarRedisplay' = 'true'::jsonb ORDER BY requested.setting_key)
                    THEN RAISE EXCEPTION 'typed_settings_onboarding_footprint_changed' USING ERRCODE = '42501'; END IF;
                ELSIF p_request->>'Producer' = 'tenant-plan-assignment' THEN
                    {PlanCompile(context)}
                ELSIF p_request ? 'Governance' THEN
                    {GovernanceCompile(context)}
                ELSIF pg_catalog.jsonb_array_length(p_accepted) <> 1 THEN
                    RAISE EXCEPTION 'typed_settings_reporting_footprint_changed' USING ERRCODE = '42501';
                END IF;
                FOR effect IN SELECT value FROM pg_catalog.jsonb_array_elements(p_accepted) LOOP
                    key := effect->>'Key';
                    IF pg_catalog.jsonb_typeof(effect) IS DISTINCT FROM 'object'
                        OR (SELECT count(*) FROM pg_catalog.jsonb_object_keys(effect)) <> 4
                        OR pg_catalog.jsonb_typeof(effect->'Key') IS DISTINCT FROM 'string'
                        OR pg_catalog.jsonb_typeof(effect->'Remove') IS DISTINCT FROM 'boolean'
                        OR NOT (effect ? 'Value') OR NOT (effect ? 'IsLocked')
                        OR key IS NULL OR effect->'Remove' IS DISTINCT FROM (effect->'Value' = 'null'::jsonb)::text::jsonb
                        OR (effect->'Value' <> 'null'::jsonb AND pg_catalog.jsonb_typeof(effect->'Value') <> 'string')
                        OR p_tenant IS NOT NULL AND p_request->>'Producer' <> 'tenant-plan-assignment'
                            AND effect->'IsLocked' IS DISTINCT FROM 'null'::jsonb
                        OR p_tenant IS NULL AND pg_catalog.jsonb_typeof(effect->'IsLocked') IS DISTINCT FROM 'boolean'
                        OR (CASE p_request->>'Producer'
                            WHEN 'event-policy' THEN key NOT IN ({string.Join(",", instanceKeys.Select(Text))}) OR effect->'Remove' = 'true'::jsonb
                            WHEN 'tenant-onboarding' THEN key NOT IN ({string.Join(",", TenantPolicySettingService.OriginalWriteKeys.Select(Text))})
                            WHEN 'reporting-intake' THEN key <> {Text(GovernanceSettingKeys.EventReporting.IntakeEnabled)}
                                OR effect->'Remove' = 'true'::jsonb
                                OR effect->>'Value' IS DISTINCT FROM (p_request->'Reporting'->'Policy'->'Enabled')::text
                            WHEN 'tenant-plan-assignment' THEN effect->'Remove' = 'true'::jsonb
                            ELSE NOT (p_request ? 'Governance') OR effect->'Remove' = 'true'::jsonb END)
                    THEN RAISE EXCEPTION 'typed_settings_effect_widened' USING ERRCODE = '42501'; END IF;
                    IF p_request->>'Producer' NOT IN ('reporting-intake','tenant-plan-assignment')
                        AND p_tenant IS NOT NULL AND effect->'Remove' = 'false'::jsonb AND EXISTS (
                        SELECT 1 FROM {sql.Table(typeof(SystemSetting))}
                        WHERE {sql.Column(typeof(SystemSetting), nameof(SystemSetting.SettingKey))} = key
                            AND {sql.Column(typeof(SystemSetting), nameof(SystemSetting.IsLocked))})
                    THEN RAISE EXCEPTION 'typed_settings_instance_ceiling_ignored' USING ERRCODE = '42501'; END IF;
                    IF p_request->>'Producer' = 'tenant-onboarding' THEN
                        SELECT COALESCE((SELECT {sql.Column(typeof(SystemSetting), nameof(SystemSetting.IsLocked))}
                            FROM {sql.Table(typeof(SystemSetting))}
                            WHERE {sql.Column(typeof(SystemSetting), nameof(SystemSetting.SettingKey))} = key),false) INTO locked;
                        input_name := CASE key
                            WHEN {Text(GovernanceSettingKeys.Events.RequireApproval)} THEN 'RequireEventApproval'
                            WHEN {Text(GovernanceSettingKeys.Events.UserSubmissionEnabled)} THEN 'AllowUserSubmittedEvents'
                            WHEN {Text(GovernanceSettingKeys.Events.OrganizationSubmissionEnabled)} THEN 'AllowOrganizationSubmittedEvents'
                            WHEN {Text(GovernanceSettingKeys.Events.GroupSubmissionEnabled)} THEN 'AllowGroupSubmittedEvents'
                            ELSE NULL END;
                        IF input_name IS NOT NULL AND (
                            effect->'Remove' IS DISTINCT FROM pg_catalog.to_jsonb(locked)
                            OR NOT locked AND effect->>'Value' IS DISTINCT FROM (p_request->'Onboarding'->'Settings'->input_name)::text)
                        THEN RAISE EXCEPTION 'typed_settings_publication_cleanup_changed' USING ERRCODE = '42501'; END IF;
                        IF key = {Text(GovernanceSettingKeys.PublicExperience.AnnouncementBarRevision)} THEN
                            SELECT {sql.Column(typeof(TenantSetting), nameof(TenantSetting.Value))} INTO stored_revision
                                FROM {sql.Table(typeof(TenantSetting))}
                                WHERE {sql.Column(typeof(TenantSetting), nameof(TenantSetting.TenantId))} = p_tenant
                                    AND {sql.Column(typeof(TenantSetting), nameof(TenantSetting.SettingKey))} = key;
                            BEGIN
                                revision := COALESCE(pg_catalog.btrim(stored_revision,'"'),'0')::integer;
                            EXCEPTION WHEN invalid_text_representation OR numeric_value_out_of_range THEN revision := 0;
                            END;
                            revision := CASE WHEN revision = 2147483647 THEN -2147483648 ELSE revision + 1 END;
                            IF effect->'Remove' IS DISTINCT FROM pg_catalog.to_jsonb(locked)
                                OR NOT locked AND effect->>'Value' IS DISTINCT FROM revision::text
                            THEN RAISE EXCEPTION 'typed_settings_original_revision_changed' USING ERRCODE = '42501'; END IF;
                        END IF;
                    END IF;
                END LOOP;
                UPDATE {sql.Name(AttemptTable)} SET accepted = p_accepted
                    WHERE attempt = admitted.attempt AND backend = admitted.backend;
                {EmailInitialize(context, true)}
            END;
            """;
    }

    private static string TypedWriteBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string branches = string.Join("\n", new[] { typeof(SystemSetting), typeof(TenantSetting) }.Select(type =>
        {
            string table = sql.Table(type);
            string id = sql.Column(type, "Id");
            var properties = context.Model.FindEntityType(type)!.GetProperties().Select(property => property.Name)
                .Order(StringComparer.Ordinal).ToArray();
            string mapped = "pg_catalog.jsonb_build_object(" + string.Join(",", properties.Select(property =>
                $"{Text(sql.ColumnName(type, property))},after_row->{Text(property)}")) + ")";
            string columns = string.Join(",", properties.Select(property => sql.Column(type, property)));
            string values = string.Join(",", properties.Select(property => $"n.{sql.Column(type, property)}"));
            string[] changed = ["Value", "IsLocked", "UpdatedAt", "UpdatedBy"];
            string[] updateColumns = type == typeof(SystemSetting)
                ? [.. changed, "SettingValueTypeId", "AllowedValues", "Description", "Category", "DisplayOrder"] : changed;
            string mutable = type == typeof(SystemSetting)
                ? $"CASE WHEN COALESCE({GovernanceSuppliesValue("admitted.request", "key")},false)"
                    + $" THEN ARRAY[{string.Join(",", updateColumns.Select(Text))}] ELSE ARRAY[{string.Join(",", changed.Select(Text))}] END"
                : $"ARRAY[{string.Join(",", changed.Select(Text))}]";
            string updates = string.Join(",", updateColumns.Select(property => $"{sql.Column(type, property)} = n.{sql.Column(type, property)}"));
            return $"""
                WHEN {Text(type.Name)} THEN
                    IF {(type == typeof(SystemSetting) ? "p_tenant IS NOT NULL" : "p_tenant IS NULL")}
                        OR {(type == typeof(TenantSetting) ? """
                            (item->>'TenantId')::uuid IS DISTINCT FROM p_tenant
                            OR COALESCE(after_row,NULLIF(item->'Before','null'::jsonb)) IS NOT NULL
                                AND (COALESCE(after_row,item->'Before')->>'TenantId')::uuid IS DISTINCT FROM p_tenant
                            """ : "false")}
                    THEN RAISE EXCEPTION 'typed_settings_foreign_target' USING ERRCODE = '42501'; END IF;
                    SELECT {Projection(context, type, "r")} INTO stored FROM {table} r
                        WHERE r.{id} = (item->>'Id')::uuid;
                    IF stored IS DISTINCT FROM NULLIF(item->'Before','null'::jsonb)
                        OR stored IS NOT NULL AND stored->>'SettingKey' IS DISTINCT FROM key
                    THEN RAISE EXCEPTION 'typed_settings_stale_row' USING ERRCODE = '40001'; END IF;
                    IF effect->'Remove' = 'true'::jsonb THEN
                        IF after_row IS NOT NULL OR p_tenant IS NULL
                        THEN RAISE EXCEPTION 'typed_settings_remove_widened' USING ERRCODE = '42501'; END IF;
                        IF stored IS NOT NULL THEN
                            {EmailWitnessBegin(context, type, mapped, "item->>'Id'", "'DELETE'")}
                            {(type == typeof(TenantSetting) ? $"DELETE FROM {table} WHERE {id} = (item->>'Id')::uuid;" : "NULL;")}
                            {EmailWitnessEnd(context, type, "item->>'Id'")}
                        END IF;
                    ELSE
                        IF after_row IS NULL OR after_row->>'SettingKey' IS DISTINCT FROM key
                            OR after_row->>'Value' IS DISTINCT FROM effect->>'Value'
                            OR (after_row->>'Id')::uuid IS DISTINCT FROM (item->>'Id')::uuid
                            OR (SELECT count(*) FROM pg_catalog.jsonb_object_keys(after_row)) <> {properties.Length}
                            OR stored IS NOT NULL AND (stored - ({mutable}))
                                IS DISTINCT FROM (after_row - ({mutable}))
                            OR p_tenant IS NULL AND after_row->'IsLocked' IS DISTINCT FROM effect->'IsLocked'
                            OR p_tenant IS NOT NULL AND after_row->'IsLocked' IS DISTINCT FROM
                                (CASE WHEN admitted.request->>'Producer' = 'tenant-plan-assignment'
                                    AND effect->'IsLocked' <> 'null'::jsonb THEN effect->'IsLocked'
                                    ELSE COALESCE(stored->'IsLocked','false'::jsonb) END)
                            OR stored IS NULL AND (after_row->>'CreatedBy')::uuid IS DISTINCT FROM admitted.actor
                            OR stored IS NOT NULL AND stored IS DISTINCT FROM after_row
                                AND (after_row->>'UpdatedBy')::uuid IS DISTINCT FROM admitted.actor
                        THEN RAISE EXCEPTION 'typed_settings_noncanonical_row' USING ERRCODE = '42501'; END IF;
                        {(type == typeof(SystemSetting) ? GovernanceMetadata("admitted.request", "key") : "")}
                        IF p_tenant IS NOT NULL AND EXISTS (
                            SELECT 1 FROM {sql.Table(typeof(SystemSetting))} ceiling
                            WHERE ceiling.{sql.Column(typeof(SystemSetting), nameof(SystemSetting.SettingKey))} = key
                                AND ceiling.{sql.Column(typeof(SystemSetting), nameof(SystemSetting.IsLocked))})
                        THEN RAISE EXCEPTION 'typed_settings_instance_ceiling_ignored' USING ERRCODE = '42501'; END IF;
                        IF stored IS NULL THEN
                            {EmailWitnessBegin(context, type, mapped, "item->>'Id'", "'INSERT'")}
                            INSERT INTO {table} ({columns}) SELECT {values} FROM pg_catalog.jsonb_populate_record(NULL::{table},{mapped}) n;
                            {EmailWitnessEnd(context, type, "item->>'Id'")}
                        ELSIF stored IS DISTINCT FROM after_row THEN
                            {EmailWitnessBegin(context, type, mapped, "item->>'Id'", "'UPDATE'")}
                            UPDATE {table} SET {updates} FROM pg_catalog.jsonb_populate_record(NULL::{table},{mapped}) n
                                WHERE {table}.{id} = n.{id};
                            {EmailWitnessEnd(context, type, "item->>'Id'")}
                        END IF;
                    END IF;
                """;
        }));
        return $"""
            DECLARE admitted {sql.Name(AttemptTable)}%ROWTYPE; item jsonb; effect jsonb; after_row jsonb; stored jsonb; key text;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, true)}
                SELECT * INTO admitted FROM {sql.Name(AttemptTable)}
                    WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid()
                        AND tenant IS NOT DISTINCT FROM p_tenant FOR UPDATE;
                IF NOT FOUND OR pg_catalog.jsonb_typeof(admitted.accepted) IS DISTINCT FROM 'array'
                    OR pg_catalog.jsonb_typeof(p_rows) IS DISTINCT FROM 'array' OR pg_catalog.jsonb_array_length(p_rows) = 0
                THEN RAISE EXCEPTION 'typed_settings_original_writer' USING ERRCODE = '42501'; END IF;
                FOR item IN SELECT value FROM pg_catalog.jsonb_array_elements(p_rows) LOOP
                    key := item->>'Key';
                    SELECT value INTO effect FROM pg_catalog.jsonb_array_elements(admitted.accepted) WHERE value->>'Key' = key;
                    IF NOT FOUND OR admitted.completed ? key OR (item->>'Id')::uuid IS NULL
                        OR (item->>'Id')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
                        OR admitted.request->>'Producer' = 'tenant-plan-assignment'
                            AND key IN ({string.Join(",", Explore.Application.Settings.EmailDeliverySettingKeys.All.Select(Text))})
                        OR admitted.request ? 'Governance' AND key = {Text(GovernanceSettingKeys.TenantDelegation.LockSmtp)}
                    THEN RAISE EXCEPTION 'typed_settings_widened_or_replayed_slice' USING ERRCODE = '42501'; END IF;
                    after_row := NULLIF(item->'After','null'::jsonb);
                    CASE item->>'Type'
                        {branches}
                        ELSE RAISE EXCEPTION 'typed_settings_foreign_row' USING ERRCODE = '42501';
                    END CASE;
                    admitted.completed := admitted.completed || pg_catalog.jsonb_build_object(key,true);
                END LOOP;
                UPDATE {sql.Name(AttemptTable)} SET completed = admitted.completed, effects = effects || p_rows
                    WHERE attempt = admitted.attempt AND backend = admitted.backend;
            END;
            """;
    }

    private static string TypedCompleteBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            DECLARE admitted {sql.Name(AttemptTable)}%ROWTYPE; effect jsonb; rows jsonb;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, true)}
                SELECT * INTO admitted FROM {sql.Name(AttemptTable)}
                    WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid()
                        AND tenant IS NOT DISTINCT FROM p_tenant FOR UPDATE;
                IF NOT FOUND OR pg_catalog.jsonb_typeof(admitted.accepted) IS DISTINCT FROM 'array'
                    OR p_expected->'Data' IS DISTINCT FROM {FunctionSql(context, "typed_data")}(p_tenant,admitted.request)
                    OR pg_catalog.jsonb_typeof(p_expected->'Rejected') IS DISTINCT FROM 'boolean'
                    OR p_expected->'Rejected' = 'true'::jsonb
                        AND (admitted.effects <> '[]'::jsonb OR admitted.completed <> pg_catalog.jsonb_build_object())
                    OR p_expected->'Rejected' = 'false'::jsonb AND
                        EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(admitted.accepted) e
                            WHERE NOT (admitted.completed ? (e->>'Key')))
                THEN RAISE EXCEPTION 'typed_settings_incomplete_original' USING ERRCODE = '42501'; END IF;
                {EmailCompletion}
                {PlanCompletion(context)}
                {GovernanceCompletion(context)}
                FOR effect IN SELECT value FROM pg_catalog.jsonb_array_elements(admitted.effects) LOOP
                    rows := p_expected->'Data'->(effect->>'Type');
                    IF NULLIF(effect->'After','null'::jsonb) IS DISTINCT FROM
                        (SELECT value FROM pg_catalog.jsonb_array_elements(rows) r WHERE r->>'Id' = effect->>'Id')
                    THEN RAISE EXCEPTION 'typed_settings_completed_row_changed' USING ERRCODE = '42501'; END IF;
                END LOOP;
                UPDATE {sql.Name(AttemptTable)}
                    SET tenant = NULL,actor = '00000000-0000-0000-0000-000000000000'::uuid,
                        request = pg_catalog.jsonb_build_object(),accepted = NULL,
                        completed = pg_catalog.jsonb_build_object(),effects = '[]'::jsonb,email_state = pg_catalog.jsonb_build_object()
                    WHERE attempt = admitted.attempt AND backend = admitted.backend;
            END;
            """;
    }

    private static IEnumerable<(string Name, string Signature, string Arguments, string Result, string Body)> TypedFunctions(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        yield return ("typed_data", "uuid,jsonb", "p_tenant uuid,p_request jsonb", "jsonb", DataBody(context, enrollment, true));
        yield return ("typed_read", HumanSignature, HumanArguments, "jsonb", ReadBody(context, enrollment, true));
        yield return ("typed_acquire", HumanSignature + ",jsonb", HumanArguments + ",p_before jsonb", "jsonb", AcquireBody(context, enrollment, true));
        yield return ("typed_compile", "uuid,jsonb,jsonb", "p_tenant uuid,p_request jsonb,p_accepted jsonb", "timestamptz", TypedCompileBody(context, enrollment));
        yield return ("typed_write", "uuid,jsonb", "p_tenant uuid,p_rows jsonb", "void", TypedWriteBody(context, enrollment));
        yield return ("typed_complete", "uuid,jsonb", "p_tenant uuid,p_expected jsonb", "void", TypedCompleteBody(context, enrollment));
        yield return ("plan_write", "uuid,jsonb", "p_tenant uuid,p_rows jsonb", "void", PlanWriteBody(context, enrollment));
        yield return ("governance_write", "uuid,jsonb", "p_tenant uuid,p_rows jsonb", "void", GovernanceWriteBody(context, enrollment));
    }
}
