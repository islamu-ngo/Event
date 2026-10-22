using Explore.Application.Settings;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresMixedSettingsContract
{
    private static string Caller(PostgresNativeQueueEnrollment enrollment, bool attempt) => $"""
        {PostgresPrivilegeGuard.RestrictedCallerSql}
        IF current_user <> '{OwnerRole}' OR session_user <> {Text(enrollment.GlobalLogin)}
            OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
            OR pg_catalog.current_setting('session_replication_role') <> 'origin'
            OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid IS DISTINCT FROM p_tenant
            OR p_tenant = '00000000-0000-0000-0000-000000000000'::uuid
            {(attempt ? "OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'" : "")}
        THEN RAISE EXCEPTION 'mixed_settings_original_caller' USING ERRCODE = '42501'; END IF;
        """;

    private static string RequestGuard => $"""
        IF p_request IS NULL OR pg_catalog.jsonb_typeof(p_request) IS DISTINCT FROM 'object'
            OR pg_catalog.jsonb_typeof(p_request->'Values') IS DISTINCT FROM 'object'
            OR pg_catalog.jsonb_typeof(p_request->'Category') IS DISTINCT FROM 'string'
            OR NOT COALESCE((p_tenant IS NULL AND (p_request->>'Scope')::integer = {(int)SettingScope.Instance}
                OR p_tenant IS NOT NULL AND (p_request->>'Scope')::integer = {(int)SettingScope.Tenant}),false)
            OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_each(p_request->'Values') pair WHERE pg_catalog.jsonb_typeof(pair.value) <> 'string')
        THEN RAISE EXCEPTION 'mixed_settings_original_request' USING ERRCODE = '42501'; END IF;
        """;

    private static string Projection(ExploreDbContext context, Type type, string alias, bool typed = false) =>
        "pg_catalog.jsonb_build_object(" + string.Join(",", MixedSettingsDataImage.Properties(context, type, typed)
            .Select(property => $"{Text(property)},{ProjectedValue(context, type, alias, property)}")) + ")";

    private static string ProjectedValue(ExploreDbContext context, Type type, string alias, string name)
    {
        var property = context.Model.FindEntityType(type)!.FindProperty(name)!;
        string column = $"{alias}.{new HumanPrincipalSourceSql(context).Column(type, name)}";
        Type valueType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        if (valueType != typeof(DateTime) && valueType != typeof(DateTimeOffset)) return column;
        string value = property.GetRelationalTypeMapping().StoreType.Contains("with time zone", StringComparison.Ordinal)
            ? $"({column} AT TIME ZONE 'UTC')" : column;
        return $"pg_catalog.to_char({value},'YYYY-MM-DD\"T\"HH24:MI:SS.US') || '0Z'";
    }

    private static string DataBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, bool typed = false)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string keys = typed ? "SELECT unnest(ARRAY[" + string.Join(",", MixedSettingsDataImage.TypedKeys.Select(Text)) + "]::text[])"
            : "SELECT pg_catalog.jsonb_object_keys(p_request->'Values') UNION SELECT unnest(ARRAY["
            + string.Join(",", MixedSettingsDataImage.FixedKeys.Select(Text)) + "]::text[])";
        string Predicate(Type type) => typed && type == typeof(SystemSetting) ? "true"
            : typed && type == typeof(TenantSetting) ? $"(p_tenant IS NULL OR r.{sql.Column(type, "TenantId")} = p_tenant)"
            : MixedSettingsDataImage.Predicate(context, type, "p_tenant", keys);
        string data = string.Join(",", MixedSettingsDataImage.ReadTypes.Select(type =>
            $"{Text(type.Name)},(SELECT COALESCE(pg_catalog.jsonb_agg({Projection(context, type, "r", typed)} ORDER BY r.{sql.Column(type, "Id")}),"
            + $"'[]'::jsonb) FROM {sql.Table(type)} r WHERE {Predicate(type)})"));
        return $"""
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, false)}
                {(typed ? TypedRequestGuard : RequestGuard)}
                RETURN pg_catalog.jsonb_build_object({data}){(typed ? " || " + PlanData(context) + " || " + GovernanceData(context) : "")};
            END;
            """;
    }

    private static string ReadBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, bool typed = false)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string Rows(string query) => $"(SELECT COALESCE(pg_catalog.jsonb_agg(pg_catalog.to_jsonb(r) ORDER BY pg_catalog.to_jsonb(r)::text),'[]'::jsonb) FROM ({Native(query)}) r)";
        string[] principal = sql.Queries;
        string facts = string.Join("\n", principal.Take(10).Select((query, index) =>
            $"p := p || pg_catalog.jsonb_build_object('q{index}',{Rows(query)});"));
        string permissions = string.Join("\n", principal.Skip(11).Select((query, index) => $"""
            item := {Rows(query)};
            IF item = '[]'::jsonb AND NOT mappings THEN item := {Rows(sql.FallbackQueries[index % 2])}; END IF;
            p := p || pg_catalog.jsonb_build_object('q{index + 11}',item);
            """));
        string local = string.Join("\n", sql.LocalQueries.Select((query, index) =>
            $"p := p || pg_catalog.jsonb_build_object('l{index}',{Rows(query)});"));
        string support = string.Join("\n", new HumanSupportAccessSourceSql(context).Queries.Select((query, index) =>
            $"h := h || pg_catalog.jsonb_build_object('q{index}',{Rows(query)});"));
        string route = "NULL";
        if (typed)
        {
            var routes = new HumanAuthorizationRouteSql(context);
            string RouteRows(bool tenant)
            {
                string query = routes.Query(tenant);
                for (int index = 0; index < Explore.Application.Contracts.Persistence.HumanAuthorizationRouteFacts.Keys.Length; index++)
                    query = query.Replace($"@key{index}", Text(Explore.Application.Contracts.Persistence.HumanAuthorizationRouteFacts.Keys[index]),
                        StringComparison.Ordinal);
                return Rows(query);
            }
            route = $"pg_catalog.jsonb_build_object('System',{RouteRows(false)},'Tenant',{RouteRows(true)})";
        }
        return $"""
            DECLARE p jsonb := pg_catalog.jsonb_build_object(); h jsonb := pg_catalog.jsonb_build_object(); item jsonb; mappings boolean; generation bigint;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, false)}
                {(typed ? TypedRequestGuard : RequestGuard)}
                IF p_actor IS NULL OR p_actor = '00000000-0000-0000-0000-000000000000'::uuid
                    OR p_provider IS NULL OR p_provider NOT IN ({string.Join(",", Enum.GetValues<AuthenticationProviderKind>().Select(value => (int)value))})
                    OR p_account IS NULL OR p_account = '' OR p_session = '00000000-0000-0000-0000-000000000000'::uuid
                    OR p_tenant IS NOT NULL AND p_resolved IS DISTINCT FROM p_tenant
                    OR (p_provider = {(int)AuthenticationProviderKind.Local}) IS DISTINCT FROM (p_stamp IS NOT NULL)
                    OR p_stamp IS NOT NULL AND (p_account IS DISTINCT FROM p_actor::text OR length(p_stamp) = 0 OR length(p_stamp) > 256)
                THEN RAISE EXCEPTION 'mixed_settings_original_human' USING ERRCODE = '42501'; END IF;
                SELECT revision INTO generation FROM {sql.Name(SourceTable)} WHERE id = 1;
                IF generation IS NULL THEN RAISE EXCEPTION 'mixed_settings_source_absent' USING ERRCODE = '55000'; END IF;
                {facts}
                SELECT EXISTS ({Native(principal[10])}) INTO mappings;
                p := p || pg_catalog.jsonb_build_object('PermissionMappingsExist',mappings);
                {permissions}
                IF p_stamp IS NOT NULL THEN {local} END IF;
                {support}
                {(typed ? "IF (p_request->>'Actor')::uuid IS DISTINCT FROM p_actor THEN RAISE EXCEPTION 'typed_settings_actor_changed' USING ERRCODE = '42501'; END IF;" : "")}
                item := pg_catalog.jsonb_build_object('Revision',generation,'Principal',p,'Support',h,'Route',{route},
                    'Data',{FunctionSql(context, typed ? "typed_data" : "data")}(p_tenant,p_request));
                IF generation IS DISTINCT FROM (SELECT revision FROM {sql.Name(SourceTable)} WHERE id = 1)
                THEN RAISE EXCEPTION 'mixed_settings_observation_changed' USING ERRCODE = '40001'; END IF;
                RETURN item;
            END;
            """;
    }

    private static string AcquireBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, bool typed = false)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            DECLARE current_image jsonb;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, true)}
                {(typed ? TypedRequestGuard : RequestGuard)}
                -- Dispatch holds the claim lock before updating processor state.
                -- Source writers use that same order in the shared trigger.
                PERFORM pg_catalog.pg_advisory_xact_lock({EmailClaimLock});
                UPDATE {sql.Name(SourceTable)} SET revision = revision WHERE id = 1;
                IF NOT FOUND THEN RAISE EXCEPTION 'mixed_settings_source_absent' USING ERRCODE = '55000'; END IF;
                current_image := {FunctionSql(context, typed ? "typed_read" : "read")}(p_actor,p_provider,p_account,p_tenant,p_stamp,p_session,p_resolved,p_request);
                {(typed ? $"""
                    IF p_request->>'Producer' = 'tenant-plan-assignment' AND (
                        {string.Join(" OR ", MixedSettingsDataImage.PlanReadTypes.Select(type =>
                            $"p_before->'Data'->{Text(type.Name)} IS DISTINCT FROM current_image->'Data'->{Text(type.Name)}"))})
                    THEN RAISE EXCEPTION 'typed_settings_plan_selection_changed' USING ERRCODE = '42501'; END IF;
                    """ : "")}
                IF p_before IS DISTINCT FROM current_image
                THEN RAISE EXCEPTION 'mixed_settings_source_changed' USING ERRCODE = '40001'; END IF;
                INSERT INTO {sql.Name(AttemptTable)} (attempt,backend,tenant,actor,request,accepted,completed,effects,email_state)
                    VALUES (pg_catalog.txid_current(),pg_catalog.pg_backend_pid(),p_tenant,p_actor,p_request,NULL,
                        pg_catalog.jsonb_build_object(),'[]'::jsonb,pg_catalog.jsonb_build_object());
                RETURN current_image;
            END;
            """;
    }

    private static string CompileBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string definitions = string.Join(",", SettingRegistry.All
            .Where(definition => !AtprotoFederationSettingPolicy.IsDedicatedKey(definition.Key))
            .OrderBy(definition => definition.Key, StringComparer.Ordinal)
            .Select(definition => $"({Text(definition.Key)},{Text(definition.Category)},{(int)definition.MinScope},{(int)definition.MaxScope},"
                + Text(definition.ValueType switch
                {
                    SettingValueType.Boolean => "boolean",
                    SettingValueType.Integer => "integer",
                    SettingValueType.Long => "long",
                    SettingValueType.Decimal => "number",
                    SettingValueType.String or SettingValueType.DateTime => "string",
                    _ => "any"
                }) + ")"));
        return $"""
            DECLARE email_time timestamptz;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, true)}
                {RequestGuard}
                IF p_accepted IS NULL OR pg_catalog.jsonb_typeof(p_accepted) <> 'object'
                    OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_each(p_accepted) pair
                        LEFT JOIN (VALUES {definitions}) d(key,category,minimum,maximum,kind) ON d.key = pair.key COLLATE "C"
                        WHERE NOT (p_request->'Values' ? pair.key) OR d.key IS NULL
                            OR d.category IS DISTINCT FROM p_request->>'Category'
                            OR (p_request->>'Scope')::integer NOT BETWEEN d.minimum AND d.maximum
                            OR pg_catalog.jsonb_typeof(pair.value) <> 'string'
                            OR CASE WHEN d.kind = 'any' THEN false ELSE
                                pg_catalog.jsonb_typeof((pair.value #>> ARRAY[]::text[])::jsonb)
                                    IS DISTINCT FROM CASE WHEN d.kind IN ('integer','long') THEN 'number' ELSE d.kind END END
                            OR CASE d.kind
                                WHEN 'integer' THEN ((pair.value #>> ARRAY[]::text[])::jsonb #>> ARRAY[]::text[])
                                    IS DISTINCT FROM (((pair.value #>> ARRAY[]::text[])::jsonb #>> ARRAY[]::text[])::integer)::text
                                WHEN 'long' THEN ((pair.value #>> ARRAY[]::text[])::jsonb #>> ARRAY[]::text[])
                                    IS DISTINCT FROM (((pair.value #>> ARRAY[]::text[])::jsonb #>> ARRAY[]::text[])::bigint)::text
                                ELSE false END)
                THEN RAISE EXCEPTION 'mixed_settings_compilation_widened' USING ERRCODE = '42501'; END IF;
                UPDATE {sql.Name(AttemptTable)} SET accepted = p_accepted
                    WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid()
                        AND tenant IS NOT DISTINCT FROM p_tenant AND request = p_request AND accepted IS NULL;
                IF NOT FOUND THEN RAISE EXCEPTION 'mixed_settings_foreign_or_replayed_compilation' USING ERRCODE = '42501'; END IF;
                {EmailInitialize(context, false)}
            END;
            """;
    }

    private static string WriteBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string email = string.Join(",", EmailDeliverySettingKeys.All.Select(Text));
        string locationKeys = string.Join(",", LocationPrivacySettingDefinitions.All.Select(definition => Text(definition.Key)));
        string branches = string.Join("\n", MixedSettingsDataImage.WriteTypes.Select(type =>
        {
            var properties = context.Model.FindEntityType(type)!.GetProperties().Select(property => property.Name)
                .Order(StringComparer.Ordinal).ToArray();
            string table = sql.Table(type);
            string id = sql.Column(type, "Id");
            string projection = "pg_catalog.jsonb_build_object(" + string.Join(",", properties.Select(property =>
                $"{Text(property)},{ProjectedValue(context, type, "r", property)}")) + ")";
            string mapped = "pg_catalog.jsonb_build_object(" + string.Join(",", properties.Select(property =>
                $"{Text(sql.ColumnName(type, property))},after_row->{Text(property)}")) + ")";
            string columns = string.Join(",", properties.Select(property => sql.Column(type, property)));
            string values = string.Join(",", properties.Select(property => $"next_row.{sql.Column(type, property)}"));
            string updates = string.Join(",", UpdateColumns(type)
                .Select(property => $"{sql.Column(type, property)} = next_row.{sql.Column(type, property)}"));
            string insert = CanInsert(type)
                ? $"INSERT INTO {table} ({columns}) SELECT {values} FROM pg_catalog.jsonb_populate_record(NULL::{table},{mapped}) next_row;"
                : "RAISE EXCEPTION 'mixed_settings_existing_projection_required' USING ERRCODE = '42501';";
            string update = updates.Length == 0
                ? "RAISE EXCEPTION 'mixed_settings_append_only_effect' USING ERRCODE = '42501';"
                : $"UPDATE {table} SET {updates} FROM pg_catalog.jsonb_populate_record(NULL::{table},{mapped}) next_row"
                    + $" WHERE {table}.{id} = next_row.{id};";
            string settingGuard = type == typeof(SystemSetting) || type == typeof(TenantSetting) ? $"""
                IF NOT (admitted.accepted ? (after_row->>'SettingKey'))
                    OR after_row->>'Value' IS DISTINCT FROM admitted.accepted->>(after_row->>'SettingKey')
                    OR (p_slice = 'email') IS DISTINCT FROM ((after_row->>'SettingKey') IN ({email}))
                    OR NOT (p_changes->'Keys' ? (after_row->>'SettingKey'))
                THEN RAISE EXCEPTION 'mixed_settings_effect_widened' USING ERRCODE = '42501'; END IF;
                """ : "";
            string purposeGuard = type == typeof(EmailDispatchProcessorState) || type == typeof(EmailDispatchTenantControl) ? """
                IF p_slice <> 'email' THEN RAISE EXCEPTION 'mixed_settings_foreign_projection' USING ERRCODE = '42501'; END IF;
                """ : type == typeof(SystemSetting) || type == typeof(TenantSetting) ? "" : $"""
                IF p_slice <> 'settings' OR NOT EXISTS (
                    SELECT 1 FROM pg_catalog.jsonb_array_elements_text(p_changes->'Keys') key WHERE key IN ({locationKeys}))
                THEN RAISE EXCEPTION 'mixed_settings_foreign_projection' USING ERRCODE = '42501'; END IF;
                """;
            string immutableGuard = type == typeof(SystemSetting) || type == typeof(TenantSetting) ? """
                IF stored IS NOT NULL AND (stored - ARRAY['Value','UpdatedAt','UpdatedBy'])
                        IS DISTINCT FROM (after_row - ARRAY['Value','UpdatedAt','UpdatedBy'])
                    OR stored IS NULL AND after_row->'IsLocked' IS DISTINCT FROM 'false'::jsonb
                THEN RAISE EXCEPTION 'mixed_settings_setting_identity_widened' USING ERRCODE = '42501'; END IF;
                """ : type == typeof(EventLocation) ? """
                IF stored IS NULL
                    OR (stored - ARRAY['NeedsPrivacyReview','PolicyVersion','ConcurrencyStamp','UpdatedAt','UpdatedBy','LastPolicyActorUserId','LastPolicyChangedAtUtc'])
                        IS DISTINCT FROM (after_row - ARRAY['NeedsPrivacyReview','PolicyVersion','ConcurrencyStamp','UpdatedAt','UpdatedBy','LastPolicyActorUserId','LastPolicyChangedAtUtc'])
                    OR after_row->'NeedsPrivacyReview' IS DISTINCT FROM 'true'::jsonb
                    OR (after_row->>'PolicyVersion')::integer IS DISTINCT FROM (stored->>'PolicyVersion')::integer + 1
                    OR after_row->>'ConcurrencyStamp' IS NOT DISTINCT FROM stored->>'ConcurrencyStamp'
                    OR (after_row->>'ConcurrencyStamp')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
                    OR (after_row->>'LastPolicyActorUserId')::uuid IS DISTINCT FROM admitted.actor
                    OR after_row->'LastPolicyChangedAtUtc' IS DISTINCT FROM after_row->'UpdatedAt'
                THEN RAISE EXCEPTION 'mixed_settings_location_transition_widened' USING ERRCODE = '42501'; END IF;
                """ : type == typeof(EmailDispatchProcessorState) || type == typeof(EmailDispatchTenantControl) ? """
                IF stored IS NOT NULL AND (stored - ARRAY['DeliveryPolicyRevision','OptionalSuppressedThroughRevision','OptionalSuppressedThroughUtc','UpdatedAt','UpdatedBy'])
                        IS DISTINCT FROM (after_row - ARRAY['DeliveryPolicyRevision','OptionalSuppressedThroughRevision','OptionalSuppressedThroughUtc','UpdatedAt','UpdatedBy'])
                    OR (after_row->>'DeliveryPolicyRevision')::bigint IS DISTINCT FROM COALESCE((stored->>'DeliveryPolicyRevision')::bigint,0) + 1
                THEN RAISE EXCEPTION 'mixed_settings_email_projection_widened' USING ERRCODE = '42501'; END IF;
                """ : type == typeof(EventLocationDisclosureAudit) || type == typeof(LocationPrivacyCorrectionReceipt)
                    || type == typeof(OutboxMessage) ? """
                IF stored IS NOT NULL THEN RAISE EXCEPTION 'mixed_settings_append_only_effect' USING ERRCODE = '42501'; END IF;
                """ : "";
            string outboxGuard = type == typeof(OutboxMessage) ? $"""
                IF after_row->>'AggregateType' IS DISTINCT FROM {Text(nameof(EventLocation))}
                    OR after_row->>'EventType' IS DISTINCT FROM {Text(LocationPrivacyOutboxMessageFactory.ProjectionCorrectionEventType)}
                    OR (after_row->>'Status')::integer IS DISTINCT FROM {(int)OutboxMessageStatus.Pending}
                    OR (after_row->>'MaxRetries')::integer IS DISTINCT FROM 10
                    OR ((after_row->>'Payload')::jsonb->>'EventLocationId')::uuid IS DISTINCT FROM (after_row->>'AggregateId')::uuid
                    OR p_tenant IS NOT NULL AND ((after_row->>'Payload')::jsonb->>'TenantId')::uuid IS DISTINCT FROM p_tenant
                THEN RAISE EXCEPTION 'mixed_settings_outbox_effect_widened' USING ERRCODE = '42501'; END IF;
                """ : "";
            string targetGuard = type == typeof(SystemSetting)
                ? "IF p_tenant IS NOT NULL THEN RAISE EXCEPTION 'mixed_settings_foreign_target' USING ERRCODE = '42501'; END IF;"
                : type == typeof(TenantSetting)
                    ? """
                        IF p_tenant IS NULL OR (after_row->>'TenantId')::uuid IS DISTINCT FROM p_tenant
                        THEN RAISE EXCEPTION 'mixed_settings_foreign_target' USING ERRCODE = '42501'; END IF;
                        """
                : context.Model.FindEntityType(type)!.FindProperty("TenantId") is not null
                    ? """
                        IF p_tenant IS NOT NULL AND (after_row->>'TenantId')::uuid IS DISTINCT FROM p_tenant
                        THEN RAISE EXCEPTION 'mixed_settings_foreign_target' USING ERRCODE = '42501'; END IF;
                        """
                    : "";
            return $"""
                WHEN {Text(type.Name)} THEN
                    {settingGuard}
                    {targetGuard}
                    {purposeGuard}
                    {outboxGuard}
                    IF (SELECT count(*) FROM pg_catalog.jsonb_object_keys(after_row)) <> {properties.Length}
                        OR (after_row->>'Id')::uuid IS NULL OR (after_row->>'Id')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
                    THEN RAISE EXCEPTION 'mixed_settings_incomplete_effect' USING ERRCODE = '42501'; END IF;
                    SELECT {projection} INTO stored FROM {table} r WHERE r.{id} = (after_row->>'Id')::uuid;
                    IF stored IS DISTINCT FROM NULLIF(item->'Before','null'::jsonb)
                    THEN RAISE EXCEPTION 'mixed_settings_stale_effect' USING ERRCODE = '40001'; END IF;
                    {immutableGuard}
                    IF stored IS NOT NULL AND (
                            stored->'CreatedAt' IS DISTINCT FROM after_row->'CreatedAt'
                            OR stored->'CreatedBy' IS DISTINCT FROM after_row->'CreatedBy')
                        OR after_row ? 'ActorUserId' AND (after_row->>'ActorUserId')::uuid IS DISTINCT FROM admitted.actor
                        OR after_row->>'UpdatedAt' IS NOT NULL AND after_row ? 'UpdatedBy'
                            AND (after_row->>'UpdatedBy')::uuid IS DISTINCT FROM admitted.actor
                    THEN RAISE EXCEPTION 'mixed_settings_audit_effect_widened' USING ERRCODE = '42501'; END IF;
                    {EmailProjectionGuard(context, type)}
                    {EmailWitnessBegin(context, type, mapped, "after_row->>'Id'", "CASE WHEN stored IS NULL THEN 'INSERT' ELSE 'UPDATE' END")}
                    IF stored IS NULL THEN
                        {insert}
                    ELSE
                        {update}
                    END IF;
                    {EmailWitnessEnd(context, type, "after_row->>'Id'")}
                """;
        }));
        return $"""
            DECLARE admitted {sql.Name(AttemptTable)}%ROWTYPE; item jsonb; after_row jsonb; stored jsonb; key text;
                email_image jsonb; email_baseline jsonb;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, true)}
                SELECT * INTO admitted FROM {sql.Name(AttemptTable)}
                    WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid()
                        AND tenant IS NOT DISTINCT FROM p_tenant FOR UPDATE;
                IF NOT FOUND OR admitted.accepted IS NULL OR p_slice IS NULL OR p_slice NOT IN ('email','settings')
                    OR pg_catalog.jsonb_typeof(p_changes->'Keys') IS DISTINCT FROM 'array'
                    OR pg_catalog.jsonb_typeof(p_changes->'Rows') IS DISTINCT FROM 'array'
                THEN RAISE EXCEPTION 'mixed_settings_original_slice_required' USING ERRCODE = '42501'; END IF;
                IF pg_catalog.jsonb_array_length(p_changes->'Keys') = 0
                THEN RAISE EXCEPTION 'mixed_settings_original_slice_required' USING ERRCODE = '42501'; END IF;
                FOR key IN SELECT pg_catalog.jsonb_array_elements_text(p_changes->'Keys') LOOP
                    IF NOT (admitted.accepted ? key) OR admitted.completed ? key
                        OR (p_slice = 'email') IS DISTINCT FROM (key IN ({email}))
                    THEN RAISE EXCEPTION 'mixed_settings_slice_widened_or_replayed' USING ERRCODE = '42501'; END IF;
                END LOOP;
                FOR item IN SELECT value FROM pg_catalog.jsonb_array_elements(p_changes->'Rows') LOOP
                    after_row := item->'After';
                    CASE item->>'Type'
                        {branches}
                        ELSE RAISE EXCEPTION 'mixed_settings_foreign_effect_type' USING ERRCODE = '42501';
                    END CASE;
                END LOOP;
                IF p_changes->'Complete' IS DISTINCT FROM 'true'::jsonb AND p_changes->'Complete' IS DISTINCT FROM 'false'::jsonb
                THEN RAISE EXCEPTION 'mixed_settings_completion_shape' USING ERRCODE = '42501'; END IF;
                IF p_changes->'Complete' = 'true'::jsonb THEN
                    FOR key IN SELECT pg_catalog.jsonb_array_elements_text(p_changes->'Keys') LOOP
                        UPDATE {sql.Name(AttemptTable)} SET completed = completed || pg_catalog.jsonb_build_object(key,p_slice)
                            WHERE attempt = admitted.attempt AND backend = admitted.backend;
                    END LOOP;
                END IF;
                UPDATE {sql.Name(AttemptTable)} SET effects = effects || (p_changes->'Rows')
                    WHERE attempt = admitted.attempt AND backend = admitted.backend;
            END;
            """;
    }

    private static string CompleteBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string checks = string.Join("\n", MixedSettingsDataImage.WriteTypes.Select(type =>
        {
            string projection = "pg_catalog.jsonb_build_object(" + string.Join(",",
                context.Model.FindEntityType(type)!.GetProperties().OrderBy(property => property.Name, StringComparer.Ordinal)
                    .Select(property => $"{Text(property.Name)},{ProjectedValue(context, type, "r", property.Name)}")) + ")";
            return $"""
                WHEN {Text(type.Name)} THEN
                    SELECT {projection} INTO stored FROM {sql.Table(type)} r WHERE r.{sql.Column(type, "Id")} = (effect->'After'->>'Id')::uuid;
                """;
        }));
        return $"""
            DECLARE admitted {sql.Name(AttemptTable)}%ROWTYPE; effect jsonb; stored jsonb; key text; stored_values jsonb;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, true)}
                SELECT * INTO admitted FROM {sql.Name(AttemptTable)}
                    WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid()
                        AND tenant IS NOT DISTINCT FROM p_tenant FOR UPDATE;
                IF NOT FOUND OR admitted.accepted IS NULL
                    OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_object_keys(admitted.accepted) k(key) WHERE NOT (admitted.completed ? k.key))
                    OR p_expected IS DISTINCT FROM {FunctionSql(context, "data")}(p_tenant,admitted.request)
                THEN RAISE EXCEPTION 'mixed_settings_incomplete_group' USING ERRCODE = '42501'; END IF;
                {EmailCompletion}
                FOR effect IN SELECT DISTINCT ON (value->>'Type',value->'After'->>'Id') value
                    FROM pg_catalog.jsonb_array_elements(admitted.effects) WITH ORDINALITY e(value,ord)
                    ORDER BY value->>'Type',value->'After'->>'Id',ord DESC LOOP
                    CASE effect->>'Type'
                        {checks}
                        ELSE RAISE EXCEPTION 'mixed_settings_foreign_completion' USING ERRCODE = '42501';
                    END CASE;
                    IF stored IS DISTINCT FROM effect->'After'
                    THEN RAISE EXCEPTION 'mixed_settings_completion_changed' USING ERRCODE = '40001'; END IF;
                END LOOP;
                FOR key IN SELECT pg_catalog.jsonb_object_keys(admitted.accepted) LOOP
                    stored_values := p_expected->CASE WHEN p_tenant IS NULL THEN 'SystemSetting' ELSE 'TenantSetting' END;
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(stored_values) r
                        WHERE r->>'SettingKey' = key AND r->>'Value' = admitted.accepted->>key)
                    THEN RAISE EXCEPTION 'mixed_settings_omitted_effect' USING ERRCODE = '42501'; END IF;
                END LOOP;
                -- Keep the physical attempt key through settlement. Deleting it here
                -- permits a second acquisition in the same transaction/backend.
                UPDATE {sql.Name(AttemptTable)}
                    SET tenant = NULL,actor = '00000000-0000-0000-0000-000000000000'::uuid,
                        request = pg_catalog.jsonb_build_object(),accepted = NULL,
                        completed = pg_catalog.jsonb_build_object(),effects = '[]'::jsonb,email_state = pg_catalog.jsonb_build_object()
                    WHERE attempt = admitted.attempt AND backend = admitted.backend;
            END;
            """;
    }

    private static string ChangedBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            BEGIN
                IF current_user <> '{OwnerRole}' OR TG_LEVEL <> 'STATEMENT' OR TG_WHEN <> 'BEFORE'
                    OR TG_OP NOT IN ('INSERT','UPDATE','DELETE','TRUNCATE')
                    OR TG_RELID NOT IN ({string.Join(",", Sources(context).Select(source => $"{Text(sql.Table(source.Type))}::regclass"))})
                THEN RAISE EXCEPTION 'mixed_settings_trigger_target' USING ERRCODE = '55000'; END IF;
                {Guard(context, enrollment)}
                PERFORM pg_catalog.pg_advisory_xact_lock({EmailClaimLock});
                UPDATE {sql.Name(SourceTable)} SET revision = revision + 1 WHERE id = 1;
                IF NOT FOUND THEN RAISE EXCEPTION 'mixed_settings_source_absent' USING ERRCODE = '55000'; END IF;
                RETURN NULL;
            END;
            """;
    }

    private static IEnumerable<(string Name, string Signature, string Arguments, string Result, string Body)> Functions(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        // ExactBodies stays in the invoker; embedding it here would recursively enumerate this body.
        yield return ("ready", "uuid", "p_tenant uuid", "void", $"""
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, false)}
            END;
            """);
        foreach (var function in TypedFunctions(context, enrollment)) yield return function;
        yield return ("data", "uuid,jsonb", "p_tenant uuid,p_request jsonb", "jsonb", DataBody(context, enrollment));
        yield return ("read", HumanSignature, HumanArguments, "jsonb", ReadBody(context, enrollment));
        yield return ("acquire", HumanSignature + ",jsonb", HumanArguments + ",p_before jsonb", "jsonb", AcquireBody(context, enrollment));
        yield return ("compile", "uuid,jsonb,jsonb", "p_tenant uuid,p_request jsonb,p_accepted jsonb", "timestamptz", CompileBody(context, enrollment));
        yield return ("email_consume", EmailConsumeSignature, "p_relation oid,p_operation text,p_before jsonb,p_after jsonb",
            "void", EmailConsumeBody(context, enrollment));
        yield return ("write", "uuid,text,jsonb", "p_tenant uuid,p_slice text,p_changes jsonb", "void", WriteBody(context, enrollment));
        yield return ("complete", "uuid,jsonb", "p_tenant uuid,p_expected jsonb", "void", CompleteBody(context, enrollment));
        yield return ("changed", "", "", "trigger", ChangedBody(context, enrollment));
        // Only the transaction's own private receipt is checked at COMMIT.
        // No authority/source reads or clock sampling follow terminal admission.
        yield return ("settlement", "", "", "trigger", $"""
            BEGIN
                IF current_user <> '{OwnerRole}' OR TG_LEVEL <> 'ROW' OR TG_WHEN <> 'AFTER' OR TG_OP <> 'INSERT'
                    OR TG_TABLE_SCHEMA <> {Text(new HumanPrincipalSourceSql(context).Schema)} OR TG_TABLE_NAME <> '{AttemptTable}'
                THEN RAISE EXCEPTION 'mixed_settings_settlement_trigger' USING ERRCODE = '55000'; END IF;
                IF NOT EXISTS (SELECT 1 FROM {new HumanPrincipalSourceSql(context).Name(AttemptTable)}
                    WHERE attempt = NEW.attempt AND backend = NEW.backend AND tenant IS NULL
                        AND actor = '00000000-0000-0000-0000-000000000000'::uuid
                        AND request = pg_catalog.jsonb_build_object() AND accepted IS NULL
                        AND completed = pg_catalog.jsonb_build_object() AND effects = '[]'::jsonb
                        AND email_state = pg_catalog.jsonb_build_object())
                THEN RAISE EXCEPTION 'mixed_settings_unsettled_group' USING ERRCODE = '42501'; END IF;
                RETURN NULL;
            END;
            """);
    }
}
