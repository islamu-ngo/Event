using Explore.Application.Settings;
using Explore.Domain;

namespace Explore.Persistence.Security;

public static partial class PostgresResolverConfigurationContract
{
    private static string KeysSql => string.Join(",", ResolverConfigurationMutation.Keys.Select(Text));

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
        THEN RAISE EXCEPTION 'resolver_configuration_caller' USING ERRCODE = '42501'; END IF;
        """;

    private static string Projection(HumanPrincipalSourceSql sql, string alias) =>
        "pg_catalog.jsonb_build_object(" + string.Join(",", Columns.Select(property =>
        {
            string name = property switch { "SettingKey" => "Key", "SettingValueTypeId" => "ValueType", _ => property };
            string value = $"{alias}.{sql.Column(typeof(SystemSetting), property)}";
            if (property == "AllowedValues") value += "::text";
            if (property is "CreatedAt" or "UpdatedAt")
                value = $"pg_catalog.to_char({value} AT TIME ZONE 'UTC','YYYY-MM-DD\"T\"HH24:MI:SS.US\"Z\"')";
            return $"{Text(name)},{value}";
        })) + ")";

    private static string Touch(ExploreDbContext context) => $"""
        UPDATE {new HumanPrincipalSourceSql(context).Name(Sentinel)} SET revision = NOT revision WHERE id = 1;
        IF NOT FOUND THEN RAISE EXCEPTION 'resolver_configuration_anchor_missing' USING ERRCODE = '55000'; END IF;
        """;

    private static string ReadBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            DECLARE result jsonb;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, false)}
                IF NOT EXISTS (SELECT 1 FROM {sql.Name(Sentinel)} WHERE id = 1)
                THEN RAISE EXCEPTION 'resolver_configuration_anchor_missing' USING ERRCODE = '55000'; END IF;
                SELECT pg_catalog.jsonb_object_agg(k.key,
                    CASE WHEN r.{sql.Column(typeof(SystemSetting), "Id")} IS NULL THEN 'null'::jsonb
                        ELSE {Projection(sql, "r")} END) INTO result
                    FROM unnest(ARRAY[{KeysSql}]::text[]) k(key)
                    LEFT JOIN {sql.Table(typeof(SystemSetting))} r
                        ON r.{sql.Column(typeof(SystemSetting), "SettingKey")} COLLATE "C" = k.key COLLATE "C";
                RETURN result;
            END;
            """;
    }

    private static string ChangedBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string key = sql.Column(typeof(SystemSetting), "SettingKey");
        return $"""
            BEGIN
                IF TG_RELID <> pg_catalog.to_regclass({Text(sql.Table(typeof(SystemSetting)))})
                    OR NOT (TG_WHEN = 'AFTER' AND TG_LEVEL = 'ROW' AND TG_OP IN ('INSERT','UPDATE','DELETE')
                        OR TG_WHEN = 'BEFORE' AND TG_LEVEL = 'STATEMENT' AND TG_OP = 'TRUNCATE')
                THEN RAISE EXCEPTION 'resolver_configuration_trigger_target' USING ERRCODE = '55000'; END IF;
                IF TG_OP = 'INSERT' THEN
                    IF NEW.{key} COLLATE "C" NOT IN ({KeysSql}) THEN RETURN NEW; END IF;
                ELSIF TG_OP = 'DELETE' THEN
                    IF OLD.{key} COLLATE "C" NOT IN ({KeysSql}) THEN RETURN OLD; END IF;
                ELSIF TG_OP = 'UPDATE' THEN
                    IF OLD.{key} COLLATE "C" NOT IN ({KeysSql}) AND NEW.{key} COLLATE "C" NOT IN ({KeysSql})
                    THEN RETURN NEW; END IF;
                END IF;
                {Guard(context, enrollment)}
                {Touch(context)}
                IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                IF TG_OP = 'TRUNCATE' THEN RETURN NULL; END IF;
                RETURN NEW;
            END;
            """;
    }

    private static string Shape(string name)
    {
        const string instant = "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}([.][0-9]{1,6})?Z$";
        string[] fields = ["Key", "Id", "Value", "ValueType", "IsLocked", "AllowedValues", "Description",
            "Category", "DisplayOrder", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"];
        string kinds = string.Join(" AND ", fields.Select(field =>
        {
            string kind = field is "ValueType" or "DisplayOrder" ? "number" : field == "IsLocked" ? "boolean" : "string";
            bool nullable = field is "AllowedValues" or "Description" or "Category" or "CreatedBy" or "UpdatedAt" or "UpdatedBy";
            return $"(pg_catalog.jsonb_typeof({name}->{Text(field)}) = '{kind}'"
                + (nullable ? $" OR {name}->{Text(field)} = 'null'::jsonb)" : ")");
        }));
        return $"""
            IF pg_catalog.jsonb_typeof({name}) IS DISTINCT FROM 'object'
            THEN RAISE EXCEPTION 'resolver_configuration_row_shape' USING ERRCODE = '22023'; END IF;
            IF ((SELECT count(*) FROM pg_catalog.jsonb_object_keys({name})) = 13
                AND {name} ?& ARRAY[{string.Join(",", fields.Select(Text))}] AND {kinds}
                AND {name}->>'CreatedAt' ~ {Text(instant)}
                AND ({name}->'UpdatedAt' = 'null'::jsonb OR {name}->>'UpdatedAt' ~ {Text(instant)})) IS DISTINCT FROM true
            THEN RAISE EXCEPTION 'resolver_configuration_row_shape' USING ERRCODE = '22023'; END IF;
            """;
    }

    private static string WriteBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        const string uuid7 = "^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$";
        var sql = new HumanPrincipalSourceSql(context);
        string S(string name) => sql.Column(typeof(SystemSetting), name);
        string[] descriptions =
        [
            "Whether the header tenant resolver is enabled. Must remain enabled for YARP propagation.",
            "Whether the custom-domain tenant resolver is enabled.",
            "Whether the subdomain tenant resolver is enabled.",
            "Whether the path-based tenant resolver is enabled.",
            "Path prefix used by the path-based tenant resolver.",
            "Base domain used for tenant subdomain generation.",
            "Whether tenant administrators may configure a custom domain."
        ];
        string specs = string.Join(",", ResolverConfigurationMutation.Keys.Select((key, index) =>
            $"({Text(key)},{(index is 4 or 5 ? (int)SettingValueType.String : (int)SettingValueType.Boolean)},"
            + $"{(index == 0 ? "true" : "false")},{Text(index < 5 ? "Routing" : "Domains")},"
            + $"{(index < 5 ? index + 20 : index - 4)},{Text(descriptions[index])})"));
        return $"""
            DECLARE key text; supplied jsonb; actual jsonb; effect jsonb; spec record;
                old_row {sql.Table(typeof(SystemSetting))}%ROWTYPE; present boolean; affected integer;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, true)}
                IF p_actor IS NULL OR p_actor = '00000000-0000-0000-0000-000000000000'::uuid
                    OR p_occurred IS NULL OR NOT pg_catalog.isfinite(p_occurred)
                    OR p_occurred < '0001-01-01T00:00:00Z'::timestamptz
                    OR p_occurred >= '10000-01-01T00:00:00Z'::timestamptz
                    OR pg_catalog.jsonb_typeof(p_before) IS DISTINCT FROM 'object'
                    OR pg_catalog.jsonb_typeof(p_effects) IS DISTINCT FROM 'array'
                THEN RAISE EXCEPTION 'resolver_configuration_input' USING ERRCODE = '22023'; END IF;
                IF (SELECT count(*) FROM pg_catalog.jsonb_object_keys(p_before)) <> 7
                    OR NOT p_before ?& ARRAY[{KeysSql}]
                    OR pg_catalog.jsonb_array_length(p_effects) NOT BETWEEN 1 AND 7
                    OR (SELECT count(DISTINCT e->>'Key') FROM pg_catalog.jsonb_array_elements(p_effects) e)
                        <> pg_catalog.jsonb_array_length(p_effects)
                THEN RAISE EXCEPTION 'resolver_configuration_source_set' USING ERRCODE = '22023'; END IF;
                -- This write conflicts with every committed resolver-source mutation since
                -- the Serializable snapshot, including an insertion into an absent leaf.
                -- The same row remains held through original-owner completion and COMMIT.
                {Touch(context)}
                FOREACH key IN ARRAY ARRAY[{KeysSql}] LOOP
                    supplied := p_before->key;
                    SELECT * INTO old_row FROM {sql.Table(typeof(SystemSetting))} r
                        WHERE r.{S("SettingKey")} COLLATE "C" = key COLLATE "C" FOR UPDATE;
                    present := FOUND;
                    IF present IS DISTINCT FROM (supplied <> 'null'::jsonb)
                    THEN RAISE EXCEPTION 'resolver_configuration_source_changed' USING ERRCODE = '40001'; END IF;
                    IF present THEN
                        {Shape("supplied")}
                        actual := {Projection(sql, "old_row")};
                        IF actual - 'CreatedAt' - 'UpdatedAt' IS DISTINCT FROM supplied - 'CreatedAt' - 'UpdatedAt'
                            OR old_row.{S("CreatedAt")} IS DISTINCT FROM (supplied->>'CreatedAt')::timestamptz
                            OR old_row.{S("UpdatedAt")} IS DISTINCT FROM (supplied->>'UpdatedAt')::timestamptz
                        THEN RAISE EXCEPTION 'resolver_configuration_source_changed' USING ERRCODE = '40001'; END IF;
                    END IF;
                END LOOP;
                FOR effect IN SELECT value FROM pg_catalog.jsonb_array_elements(p_effects) LOOP
                    {Shape("effect")}
                    SELECT * INTO spec FROM (VALUES {specs}) s(key,type,locked,category,ordering,description)
                        WHERE s.key COLLATE "C" = (effect->>'Key') COLLATE "C";
                    IF NOT FOUND THEN RAISE EXCEPTION 'resolver_configuration_foreign_key' USING ERRCODE = '42501'; END IF;
                    key := effect->>'Key';
                    supplied := p_before->key;
                    IF (effect->>'ValueType')::integer IS DISTINCT FROM spec.type
                        OR (effect->>'IsLocked')::boolean IS DISTINCT FROM spec.locked
                        OR effect->'AllowedValues' IS DISTINCT FROM 'null'::jsonb
                        OR effect->>'Description' IS DISTINCT FROM spec.description
                        OR effect->>'Category' IS DISTINCT FROM spec.category
                        OR (effect->>'DisplayOrder')::integer IS DISTINCT FROM spec.ordering
                        OR (effect->>'UpdatedAt')::timestamptz IS DISTINCT FROM p_occurred
                        OR (effect->>'UpdatedBy')::uuid IS DISTINCT FROM p_actor
                        OR (effect->>'Id')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
                        OR (spec.type = {(int)SettingValueType.Boolean} AND effect->>'Value' NOT IN ('true','false'))
                        OR (spec.locked AND effect->>'Value' <> 'true')
                        OR (spec.type = {(int)SettingValueType.String} AND pg_catalog.jsonb_typeof((effect->>'Value')::jsonb) <> 'string')
                    THEN RAISE EXCEPTION 'resolver_configuration_fixed_effect' USING ERRCODE = '42501'; END IF;
                    IF supplied = 'null'::jsonb THEN
                        IF effect->>'Id' !~ {Text(uuid7)}
                            OR (effect->>'CreatedAt')::timestamptz IS DISTINCT FROM p_occurred
                            OR (effect->>'CreatedBy')::uuid IS DISTINCT FROM p_actor
                        THEN RAISE EXCEPTION 'resolver_configuration_creation' USING ERRCODE = '42501'; END IF;
                        INSERT INTO {sql.Table(typeof(SystemSetting))} ({string.Join(",", Columns.Select(S))})
                        VALUES ((effect->>'Id')::uuid,key,effect->>'Value',spec.type,spec.locked,NULL,
                            spec.description,spec.category,spec.ordering,p_occurred,p_actor,p_occurred,p_actor);
                    ELSE
                        IF effect->'Id' IS DISTINCT FROM supplied->'Id'
                            OR effect->'CreatedBy' IS DISTINCT FROM supplied->'CreatedBy'
                            OR (effect->>'CreatedAt')::timestamptz IS DISTINCT FROM (supplied->>'CreatedAt')::timestamptz
                        THEN RAISE EXCEPTION 'resolver_configuration_creation' USING ERRCODE = '42501'; END IF;
                        UPDATE {sql.Table(typeof(SystemSetting))} SET
                            {S("Value")} = effect->>'Value', {S("SettingValueTypeId")} = spec.type,
                            {S("IsLocked")} = spec.locked, {S("AllowedValues")} = NULL,
                            {S("Description")} = spec.description, {S("Category")} = spec.category,
                            {S("DisplayOrder")} = spec.ordering, {S("UpdatedAt")} = p_occurred, {S("UpdatedBy")} = p_actor
                            WHERE {S("Id")} = (effect->>'Id')::uuid AND {S("SettingKey")} COLLATE "C" = key COLLATE "C";
                    END IF;
                    GET DIAGNOSTICS affected = ROW_COUNT;
                    IF affected <> 1 THEN RAISE EXCEPTION 'resolver_configuration_write_conflict' USING ERRCODE = '40001'; END IF;
                END LOOP;
                {Guard(context, enrollment)}
                {Caller(enrollment, true)}
            END;
            """;
    }

    private static (string Operation, string Signature, string Arguments, string Result, string Body)[] Functions(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
    [
        ("read", "", "", "jsonb", ReadBody(context, enrollment)),
        ("write", WriteSignature, "p_actor uuid,p_before jsonb,p_effects jsonb,p_occurred timestamptz", "void", WriteBody(context, enrollment)),
        ("changed", "", "", "trigger", ChangedBody(context, enrollment))
    ];
}
