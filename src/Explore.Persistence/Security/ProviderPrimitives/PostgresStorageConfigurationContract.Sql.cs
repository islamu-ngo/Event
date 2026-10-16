using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Secrets;

namespace Explore.Persistence.Security;

public static partial class PostgresStorageConfigurationContract
{
    private static string KeysSql => string.Join(",", StorageConfigurationState.Keys.Select(Text));

    private static string Caller(PostgresNativeQueueEnrollment enrollment, bool transaction) => $"""
        {PostgresPrivilegeGuard.RestrictedCallerSql}
        IF current_user <> '{OwnerRole}' OR p_ambient IS NULL
            OR p_ambient = '00000000-0000-0000-0000-000000000000'::uuid
            OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
            OR pg_catalog.current_setting('session_replication_role') <> 'origin'
            {(transaction ? "OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'" : "")}
            OR NOT COALESCE(
                session_user = {Text(enrollment.GlobalLogin)}
                    AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NULL
                OR session_user = {Text(enrollment.RuntimeLogin)}
                    AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid = p_ambient,false)
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE (pg_catalog.pg_has_role(session_user,r.oid,'USAGE') OR pg_catalog.pg_has_role(session_user,r.oid,'SET'))
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
        THEN RAISE EXCEPTION 'storage_configuration_caller' USING ERRCODE = '42501'; END IF;
        """;

    private static string Projection(HumanPrincipalSourceSql sql, Type type, string alias) =>
        "pg_catalog.jsonb_build_object(" + string.Join(",", Fields.Select(field =>
        {
            bool missing = type == typeof(SystemSetting) ? field == "TenantId"
                : field is "ValueType" or "AllowedValues" or "Description" or "Category" or "DisplayOrder";
            string property = field switch { "Key" => "SettingKey", "ValueType" => "SettingValueTypeId", _ => field };
            string value = missing ? "NULL" : $"{alias}.{sql.Column(type, property)}";
            if (!missing && field == "AllowedValues") value += "::text";
            if (field is "CreatedAt" or "UpdatedAt") value = Instant(value);
            return $"{Text(field)},{value}";
        })) + ")";

    private static string Instant(string expression) =>
        $"pg_catalog.to_char({expression} AT TIME ZONE 'UTC','YYYY-MM-DD\"T\"HH24:MI:SS.US\"Z\"')";

    private static string ReadBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string Rows(Type type, bool tenant) => $"""
            (SELECT pg_catalog.jsonb_object_agg(k.key,
                CASE WHEN r.{sql.Column(type, "Id")} IS NULL THEN 'null'::jsonb ELSE {Projection(sql, type, "r")} END)
                FROM unnest(ARRAY[{KeysSql}]::text[]) k(key)
                LEFT JOIN {sql.Table(type)} r ON r.{sql.Column(type, "SettingKey")} COLLATE "C" = k.key COLLATE "C"
                    {(tenant ? $"AND r.{sql.Column(type, "TenantId")} = p_ambient" : "")})
            """;
        string Binding(string property) => "b." + sql.Column(typeof(SecretBinding), property);
        string bindingFields = string.Join(",", context.Model.FindEntityType(typeof(SecretBinding))!.GetProperties()
            .OrderBy(property => property.Name).Select(property => $"{Text(property.Name)},"
                + (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?)
                    ? Instant(Binding(property.Name)) : Binding(property.Name))));
        return $"""
            DECLARE generation bigint; bindings jsonb;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, false)}
                SELECT revision INTO generation FROM {sql.Name(SourceTable)} WHERE id = 1;
                IF generation IS NULL THEN RAISE EXCEPTION 'storage_configuration_anchor_missing' USING ERRCODE = '55000'; END IF;
                SELECT COALESCE(pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object({bindingFields})
                    ORDER BY {Binding("Id")}), '[]'::jsonb) INTO bindings FROM {sql.Table(typeof(SecretBinding))} b
                    WHERE {Binding("SettingKey")} COLLATE "C" IN (
                        {Text(SecretDefinitionRegistry.Keys.Storage.AccessKeyId)},{Text(SecretDefinitionRegistry.Keys.Storage.SecretAccessKey)})
                    AND {Binding("Qualifier")} = ''
                    AND ({Binding("SettingScopeId")} = 1 AND {Binding("ScopeId")} IS NULL
                        OR {Binding("SettingScopeId")} = 2 AND {Binding("ScopeId")} = p_ambient);
                RETURN pg_catalog.jsonb_build_object('System',{Rows(typeof(SystemSetting), false)},
                    'Tenant',{Rows(typeof(TenantSetting), true)},'Bindings',bindings::text,'Revision',generation);
            END;
            """;
    }

    private static string AcquireBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) => $"""
        DECLARE generation bigint;
        BEGIN
            {Guard(context, enrollment)}
            {Caller(enrollment, true)}
            UPDATE {new HumanPrincipalSourceSql(context).Name(SourceTable)} SET revision = revision WHERE id = 1 RETURNING revision INTO generation;
            IF generation IS NULL THEN RAISE EXCEPTION 'storage_configuration_anchor_missing' USING ERRCODE = '55000'; END IF;
            IF p_revision IS NULL OR generation IS DISTINCT FROM p_revision
            THEN RAISE EXCEPTION 'storage_configuration_source_changed' USING ERRCODE = '40001'; END IF;
            RETURN generation;
        END;
        """;

    private static string ChangedBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            BEGIN
                IF current_user <> '{OwnerRole}' OR TG_WHEN <> 'BEFORE' OR TG_LEVEL <> 'STATEMENT'
                    OR TG_OP NOT IN ('INSERT','UPDATE','DELETE','TRUNCATE')
                    OR TG_RELID NOT IN ({string.Join(",", Sources.Select(type => $"{Text(sql.Table(type))}::regclass"))})
                THEN RAISE EXCEPTION 'storage_configuration_trigger_target' USING ERRCODE = '55000'; END IF;
                {Guard(context, enrollment)}
                UPDATE {sql.Name(SourceTable)} SET revision = revision + 1 WHERE id = 1;
                IF NOT FOUND THEN RAISE EXCEPTION 'storage_configuration_anchor_missing' USING ERRCODE = '55000'; END IF;
                RETURN NULL;
            END;
            """;
    }

    private static string Shape(string row)
    {
        const string instant = "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}([.][0-9]{1,6})?Z$";
        string kinds = string.Join(" AND ", Fields.Select(field =>
        {
            string kind = field is "ValueType" or "DisplayOrder" ? "number" : field == "IsLocked" ? "boolean" : "string";
            bool nullable = field is "TenantId" or "ValueType" or "AllowedValues" or "Description" or "Category"
                or "DisplayOrder" or "CreatedBy" or "UpdatedAt" or "UpdatedBy";
            return $"(pg_catalog.jsonb_typeof({row}->{Text(field)}) = '{kind}'"
                + (nullable ? $" OR {row}->{Text(field)} = 'null'::jsonb)" : ")");
        }));
        return $"""
            IF pg_catalog.jsonb_typeof({row}) IS DISTINCT FROM 'object'
            THEN RAISE EXCEPTION 'storage_configuration_row_shape' USING ERRCODE = '22023'; END IF;
            IF ((SELECT count(*) FROM pg_catalog.jsonb_object_keys({row})) = 14
                AND {row} ?& ARRAY[{string.Join(",", Fields.Select(Text))}]
                AND {kinds}
                AND pg_catalog.jsonb_typeof({row}->'Key') = 'string'
                AND pg_catalog.jsonb_typeof({row}->'Id') = 'string'
                AND pg_catalog.jsonb_typeof({row}->'Value') = 'string'
                AND pg_catalog.jsonb_typeof({row}->'IsLocked') = 'boolean'
                AND {row}->>'CreatedAt' ~ {Text(instant)}
                AND ({row}->'UpdatedAt' = 'null'::jsonb OR {row}->>'UpdatedAt' ~ {Text(instant)})) IS DISTINCT FROM true
            THEN RAISE EXCEPTION 'storage_configuration_row_shape' USING ERRCODE = '22023'; END IF;
            """;
    }

    private static string WriteBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        const string uuid7 = "^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$";
        var sql = new HumanPrincipalSourceSql(context);
        string S(string name) => sql.Column(typeof(SystemSetting), name);
        string T(string name) => sql.Column(typeof(TenantSetting), name);
        string specs = string.Join(",", Enumerable.Range(0, 12).Select(index =>
        {
            var metadata = StorageSettingsMutation.Metadata(index);
            return $"({index},{Text(StorageConfigurationState.Keys[index])},{(int)metadata.Type},"
                + $"{Text(metadata.Category)},{metadata.Order},{Text(metadata.Description)})";
        }));
        return $"""
            DECLARE target uuid := NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid;
                normalized jsonb := p_before; original jsonb; effect jsonb; actual jsonb; value jsonb;
                group_name text; key text; field text; spec record; affected integer; generation bigint;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, true)}
                IF p_actor IS NULL OR p_actor = '00000000-0000-0000-0000-000000000000'::uuid
                    OR p_occurred IS NULL OR NOT pg_catalog.isfinite(p_occurred)
                    OR p_occurred < '0001-01-01T00:00:00Z'::timestamptz OR p_occurred >= '10000-01-01T00:00:00Z'::timestamptz
                    OR pg_catalog.jsonb_typeof(p_before) IS DISTINCT FROM 'object'
                    OR pg_catalog.jsonb_typeof(p_effects) IS DISTINCT FROM 'array'
                THEN RAISE EXCEPTION 'storage_configuration_input' USING ERRCODE = '22023'; END IF;
                IF (SELECT count(*) FROM pg_catalog.jsonb_object_keys(p_before)) <> 4
                    OR NOT p_before ?& ARRAY['System','Tenant','Bindings','Revision']
                    OR pg_catalog.jsonb_array_length(p_effects) NOT BETWEEN 1 AND 12
                    OR (SELECT count(DISTINCT e->>'Key') FROM pg_catalog.jsonb_array_elements(p_effects) e) <> pg_catalog.jsonb_array_length(p_effects)
                THEN RAISE EXCEPTION 'storage_configuration_input' USING ERRCODE = '22023'; END IF;
                PERFORM {FunctionSql(context, "acquire")}(p_ambient,(p_before->>'Revision')::bigint);
                FOREACH group_name IN ARRAY ARRAY['System','Tenant'] LOOP
                    IF pg_catalog.jsonb_typeof(p_before->group_name) IS DISTINCT FROM 'object'
                    THEN RAISE EXCEPTION 'storage_configuration_sources' USING ERRCODE = '22023'; END IF;
                    IF (SELECT count(*) FROM pg_catalog.jsonb_object_keys(p_before->group_name)) <> 13
                        OR NOT (p_before->group_name) ?& ARRAY[{KeysSql}]
                    THEN RAISE EXCEPTION 'storage_configuration_sources' USING ERRCODE = '22023'; END IF;
                    FOREACH key IN ARRAY ARRAY[{KeysSql}] LOOP
                        original := p_before->group_name->key;
                        IF original <> 'null'::jsonb THEN
                            {Shape("original")}
                            FOREACH field IN ARRAY ARRAY['CreatedAt','UpdatedAt'] LOOP
                                normalized := pg_catalog.jsonb_set(normalized, ARRAY[group_name,key,field],
                                    COALESCE(pg_catalog.to_jsonb({Instant("(original->>field)::timestamptz")}), 'null'::jsonb));
                            END LOOP;
                        END IF;
                    END LOOP;
                END LOOP;
                actual := {FunctionSql(context, "read")}(p_ambient);
                IF actual IS DISTINCT FROM normalized
                THEN RAISE EXCEPTION 'storage_configuration_sources_changed' USING ERRCODE = '40001'; END IF;
                IF target IS NULL THEN
                    IF (SELECT count(*) FROM pg_catalog.jsonb_array_elements(p_effects) e
                        WHERE e->>'Key' IN ({string.Join(",", StorageConfigurationState.Keys.Take(6).Select(Text))})) NOT IN (0,6)
                        OR (SELECT count(*) FROM pg_catalog.jsonb_array_elements(p_effects) e
                        WHERE e->>'Key' IN ({string.Join(",", StorageConfigurationState.Keys.Skip(6).Take(6).Select(Text))})) NOT IN (0,6)
                    THEN RAISE EXCEPTION 'storage_configuration_complete_group' USING ERRCODE = '42501'; END IF;
                END IF;
                FOR effect IN SELECT e.value FROM pg_catalog.jsonb_array_elements(p_effects) e(value) LOOP
                    {Shape("effect")}
                    key := effect->>'Key';
                    SELECT * INTO spec FROM (VALUES {specs}) s(index,key,type,category,ordering,description)
                        WHERE s.key COLLATE "C" = (effect->>'Key') COLLATE "C";
                    IF NOT FOUND OR target IS NOT NULL AND spec.index IN (3,4)
                    THEN RAISE EXCEPTION 'storage_configuration_foreign_effect' USING ERRCODE = '42501'; END IF;
                    original := p_before->(CASE WHEN target IS NULL THEN 'System' ELSE 'Tenant' END)->key;
                    IF (effect->>'TenantId')::uuid IS DISTINCT FROM target
                        OR (effect->>'UpdatedBy')::uuid IS DISTINCT FROM p_actor
                        OR (effect->>'UpdatedAt')::timestamptz IS DISTINCT FROM p_occurred
                        OR (effect->>'Id')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
                        OR effect->'AllowedValues' IS DISTINCT FROM 'null'::jsonb
                        OR (target IS NULL AND ((effect->>'ValueType')::int IS DISTINCT FROM spec.type
                            OR effect->>'Category' IS DISTINCT FROM spec.category
                            OR (effect->>'DisplayOrder')::int IS DISTINCT FROM spec.ordering
                            OR effect->>'Description' IS DISTINCT FROM spec.description
                            OR (effect->>'IsLocked')::boolean IS DISTINCT FROM false))
                        OR (target IS NOT NULL AND (effect->'ValueType' IS DISTINCT FROM 'null'::jsonb
                            OR effect->'Category' IS DISTINCT FROM 'null'::jsonb
                            OR effect->'DisplayOrder' IS DISTINCT FROM 'null'::jsonb
                            OR effect->'Description' IS DISTINCT FROM 'null'::jsonb
                            OR (effect->>'IsLocked')::boolean IS DISTINCT FROM COALESCE((original->>'IsLocked')::boolean,false)))
                    THEN RAISE EXCEPTION 'storage_configuration_fixed_effect' USING ERRCODE = '42501'; END IF;
                    IF original = 'null'::jsonb THEN
                        IF effect->>'Id' !~ {Text(uuid7)}
                            OR (effect->>'CreatedAt')::timestamptz IS DISTINCT FROM p_occurred
                            OR (effect->>'CreatedBy')::uuid IS DISTINCT FROM p_actor
                        THEN RAISE EXCEPTION 'storage_configuration_creation' USING ERRCODE = '42501'; END IF;
                    ELSIF effect->'Id' IS DISTINCT FROM original->'Id'
                        OR effect->'CreatedBy' IS DISTINCT FROM original->'CreatedBy'
                        OR (effect->>'CreatedAt')::timestamptz IS DISTINCT FROM (original->>'CreatedAt')::timestamptz
                    THEN RAISE EXCEPTION 'storage_configuration_creation' USING ERRCODE = '42501'; END IF;
                    value := (effect->>'Value')::jsonb;
                    IF (spec.type = {(int)SettingValueType.String} AND pg_catalog.jsonb_typeof(value) IS DISTINCT FROM 'string')
                        OR (spec.type = {(int)SettingValueType.Boolean} AND pg_catalog.jsonb_typeof(value) IS DISTINCT FROM 'boolean')
                        OR (spec.type IN ({(int)SettingValueType.Long},{(int)SettingValueType.Integer})
                            AND (pg_catalog.jsonb_typeof(value) IS DISTINCT FROM 'number'
                                OR value::text !~ '^[0-9]+$' OR (value::text)::bigint <= 0))
                        OR (spec.index = 0 AND value NOT IN ('"local"'::jsonb,'"s3_compatible"'::jsonb))
                        OR (spec.index = 11 AND (value::text)::bigint > 1440)
                        OR (spec.index = 5 AND (pg_catalog.jsonb_typeof(value) IS DISTINCT FROM 'object'
                            OR pg_catalog.jsonb_typeof(value->'Routes') IS DISTINCT FROM 'array'))
                    THEN RAISE EXCEPTION 'storage_configuration_value' USING ERRCODE = '22023'; END IF;
                    IF spec.index = 5 THEN
                        IF value->>'Version' IS DISTINCT FROM '1'
                            OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(value->'Routes') r
                                WHERE r->>'RouteKey' NOT IN ('images','documents','general')
                                    OR r->>'Provider' NOT IN ('local','s3_compatible')
                                    OR pg_catalog.jsonb_typeof(r->'MaxUploadBytes') IS DISTINCT FROM 'number'
                                    OR COALESCE(r->>'MaxUploadBytes','') !~ '^[0-9]+$'
                                    OR (r->>'MaxUploadBytes')::bigint <= 0
                                    OR r->>'RouteKey' IS NULL OR r->>'Provider' IS NULL)
                            OR (SELECT count(DISTINCT r->>'RouteKey') FROM pg_catalog.jsonb_array_elements(value->'Routes') r)
                                <> pg_catalog.jsonb_array_length(value->'Routes')
                        THEN RAISE EXCEPTION 'storage_configuration_routes' USING ERRCODE = '22023'; END IF;
                    END IF;
                    IF target IS NULL THEN
                        IF original = 'null'::jsonb THEN
                            INSERT INTO {sql.Table(typeof(SystemSetting))} (
                                {S("Id")},{S("SettingKey")},{S("Value")},{S("SettingValueTypeId")},{S("IsLocked")},
                                {S("AllowedValues")},{S("Description")},{S("Category")},{S("DisplayOrder")},
                                {S("CreatedAt")},{S("CreatedBy")},{S("UpdatedAt")},{S("UpdatedBy")})
                            VALUES ((effect->>'Id')::uuid,key,effect->>'Value',spec.type,false,NULL,
                                spec.description,spec.category,spec.ordering,p_occurred,p_actor,p_occurred,p_actor);
                        ELSE
                            UPDATE {sql.Table(typeof(SystemSetting))} SET {S("Value")} = effect->>'Value',
                                {S("SettingValueTypeId")} = spec.type,{S("IsLocked")} = false,{S("AllowedValues")} = NULL,
                                {S("Description")} = spec.description,{S("Category")} = spec.category,{S("DisplayOrder")} = spec.ordering,
                                {S("UpdatedAt")} = p_occurred,{S("UpdatedBy")} = p_actor
                                WHERE {S("Id")} = (effect->>'Id')::uuid AND {S("SettingKey")} COLLATE "C" = key COLLATE "C";
                        END IF;
                    ELSE
                        IF original = 'null'::jsonb THEN
                            INSERT INTO {sql.Table(typeof(TenantSetting))} (
                                {T("Id")},{T("TenantId")},{T("SettingKey")},{T("Value")},{T("IsLocked")},
                                {T("CreatedAt")},{T("CreatedBy")},{T("UpdatedAt")},{T("UpdatedBy")})
                            VALUES ((effect->>'Id')::uuid,target,key,effect->>'Value',false,p_occurred,p_actor,p_occurred,p_actor);
                        ELSE
                            UPDATE {sql.Table(typeof(TenantSetting))} SET {T("Value")} = effect->>'Value',
                                {T("UpdatedAt")} = p_occurred,{T("UpdatedBy")} = p_actor
                                WHERE {T("Id")} = (effect->>'Id')::uuid AND {T("TenantId")} = target
                                    AND {T("SettingKey")} COLLATE "C" = key COLLATE "C";
                        END IF;
                    END IF;
                    GET DIAGNOSTICS affected = ROW_COUNT;
                    IF affected <> 1 THEN RAISE EXCEPTION 'storage_configuration_write_conflict' USING ERRCODE = '40001'; END IF;
                END LOOP;
                {Guard(context, enrollment)}
                {Caller(enrollment, true)}
                SELECT revision INTO generation FROM {sql.Name(SourceTable)} WHERE id = 1;
                RETURN generation;
            END;
            """;
    }

    private static (string Name, string Signature, string Arguments, string Result, string Body)[] Functions(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
    [
        ("read", "uuid", "p_ambient uuid", "jsonb", ReadBody(context, enrollment)),
        ("acquire", "uuid,bigint", "p_ambient uuid,p_revision bigint", "bigint", AcquireBody(context, enrollment)),
        ("write", "uuid,uuid,jsonb,jsonb,timestamp with time zone",
            "p_ambient uuid,p_actor uuid,p_before jsonb,p_effects jsonb,p_occurred timestamptz", "bigint", WriteBody(context, enrollment)),
        ("changed", "", "", "trigger", ChangedBody(context, enrollment))
    ];
}
