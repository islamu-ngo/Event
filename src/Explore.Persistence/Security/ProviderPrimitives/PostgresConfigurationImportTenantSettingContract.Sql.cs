using System.Globalization;
using System.Text.Json;
using Explore.Domain;

namespace Explore.Persistence.Security;

public static partial class PostgresConfigurationImportTenantSettingContract
{
    private static string TenantKeysSql() =>
        "ARRAY[" + string.Join(", ", OrdinaryEntries().Select(entry => Text(entry.Definition.Key))) + "]::text[]";

    private static string RegistrySql() => string.Join(",\n", OrdinaryEntries().Select(entry =>
    {
        var definition = entry.Definition;
        if (definition.ValueType is not (SettingValueType.String or SettingValueType.Boolean))
            throw new InvalidOperationException("configuration_import_tenant_setting_value_contract_changed");
        string allowed = definition.AllowedValues is null
            ? "NULL::jsonb" : Text(JsonSerializer.Serialize(definition.AllowedValues)) + "::jsonb";
        return $"({Text(definition.Key)}, {(int)definition.ValueType}, {allowed}, " +
            $"{(entry.MaximumStringLength is { } maximum ? maximum.ToString(CultureInfo.InvariantCulture) : "NULL::integer")}, " +
            $"{RelationalSettingMutationLock.ComputeStableLockKey(definition.Key).ToString(CultureInfo.InvariantCulture)}::bigint)";
    }));

    private static string WriteBody(ExploreDbContext context)
    {
        string S(string property) => Column(context, typeof(SystemSetting), property);
        string T(string property) => Column(context, typeof(TenantSetting), property);
        string system = Table(context, typeof(SystemSetting));
        string tenant = Table(context, typeof(TenantSetting));
        return $"""
            DECLARE mutation jsonb; original_row jsonb; definition record; parsed jsonb;
                affected integer; existing_lock boolean; row_exists boolean;
            BEGIN
                PERFORM {FunctionSql(context, "assert_authority")}();
                IF p_tenant IS NULL OR p_tenant = '00000000-0000-0000-0000-000000000000'::uuid
                    OR p_actor IS NULL OR p_actor = '00000000-0000-0000-0000-000000000000'::uuid
                    OR p_at IS NULL OR NOT pg_catalog.isfinite(p_at)
                    OR p_at < '0001-01-01T00:00:00Z'::timestamptz
                    OR p_at >= '10000-01-01T00:00:00Z'::timestamptz
                    OR p_writes IS NULL OR pg_catalog.jsonb_typeof(p_writes) <> 'array' THEN
                    RAISE EXCEPTION 'configuration_import_tenant_setting_batch_shape' USING ERRCODE = '22023';
                END IF;
                IF pg_catalog.jsonb_array_length(p_writes) NOT BETWEEN 1 AND {OrdinaryEntries().Length}
                    OR (SELECT count(DISTINCT item->>'key') FROM pg_catalog.jsonb_array_elements(p_writes) item)
                        <> pg_catalog.jsonb_array_length(p_writes) THEN
                    RAISE EXCEPTION 'configuration_import_tenant_setting_batch_shape' USING ERRCODE = '22023';
                END IF;
                -- Acquire the complete requested batch in the same ordinal order and
                -- with the same advisory hashes as RelationalSettingMutationLock.
                FOR definition IN SELECT d.* FROM (VALUES {RegistrySql()})
                    d(key, value_type, allowed_values, maximum_length, lock_key)
                    WHERE EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(p_writes) item
                        WHERE item->>'key' COLLATE "C" = d.key COLLATE "C")
                    ORDER BY d.key COLLATE "C"
                LOOP
                    PERFORM pg_catalog.pg_advisory_xact_lock(definition.lock_key);
                END LOOP;
                FOR mutation IN SELECT item FROM pg_catalog.jsonb_array_elements(p_writes) item
                    ORDER BY item->>'key' COLLATE "C"
                LOOP
                    IF pg_catalog.jsonb_typeof(mutation) <> 'object' THEN
                        RAISE EXCEPTION 'configuration_import_tenant_setting_mutation_shape' USING ERRCODE = '22023';
                    END IF;
                    IF NOT (mutation ?& ARRAY['key','value','id','original'])
                        OR (SELECT count(*) FROM pg_catalog.jsonb_object_keys(mutation)) <> 4
                        OR pg_catalog.jsonb_typeof(mutation->'key') <> 'string'
                        OR pg_catalog.jsonb_typeof(mutation->'value') <> 'string'
                        OR pg_catalog.jsonb_typeof(mutation->'id') <> 'string'
                        OR (mutation->>'id')::uuid = '00000000-0000-0000-0000-000000000000'::uuid THEN
                        RAISE EXCEPTION 'configuration_import_tenant_setting_mutation_shape' USING ERRCODE = '22023';
                    END IF;
                    SELECT d.* INTO definition FROM (VALUES {RegistrySql()})
                        d(key, value_type, allowed_values, maximum_length, lock_key)
                        WHERE d.key COLLATE "C" = (mutation->>'key') COLLATE "C";
                    IF NOT FOUND THEN
                        RAISE EXCEPTION 'configuration_import_tenant_setting_key_not_allowed' USING ERRCODE = '22023';
                    END IF;
                    parsed := (mutation->>'value')::jsonb;
                    IF (definition.value_type = 2 AND pg_catalog.jsonb_typeof(parsed) <> 'boolean')
                        OR (definition.value_type = 0 AND (
                            pg_catalog.jsonb_typeof(parsed) <> 'string'
                            OR definition.maximum_length IS NOT NULL AND (
                                SELECT COALESCE(sum(CASE WHEN pg_catalog.ascii(pg_catalog.substr(
                                    parsed #>> ARRAY[]::text[], character_position, 1)) > 65535 THEN 2 ELSE 1 END), 0)
                                FROM pg_catalog.generate_series(1, pg_catalog.length(parsed #>> ARRAY[]::text[]))
                                    positions(character_position)) > definition.maximum_length
                            OR definition.allowed_values IS NOT NULL AND NOT EXISTS (
                                SELECT 1 FROM pg_catalog.jsonb_array_elements_text(definition.allowed_values) allowed(value)
                                WHERE allowed.value COLLATE "C" = (parsed #>> ARRAY[]::text[]) COLLATE "C")))
                        OR definition.value_type NOT IN (0, 2) THEN
                        RAISE EXCEPTION 'configuration_import_tenant_setting_value_shape' USING ERRCODE = '22023';
                    END IF;
                    original_row := NULLIF(mutation->'original', 'null'::jsonb);
                    IF original_row IS NOT NULL THEN
                        IF pg_catalog.jsonb_typeof(original_row) <> 'object' THEN
                            RAISE EXCEPTION 'configuration_import_tenant_setting_original_shape' USING ERRCODE = '22023';
                        END IF;
                        IF NOT (original_row ?& ARRAY['id','tenant','key','value','locked','created','creator','updated','updater'])
                            OR (SELECT count(*) FROM pg_catalog.jsonb_object_keys(original_row)) <> 9
                            OR pg_catalog.jsonb_typeof(original_row->'id') <> 'string'
                            OR (original_row->>'id')::uuid IS DISTINCT FROM (mutation->>'id')::uuid
                            OR pg_catalog.jsonb_typeof(original_row->'tenant') <> 'string'
                            OR (original_row->>'tenant')::uuid IS DISTINCT FROM p_tenant
                            OR pg_catalog.jsonb_typeof(original_row->'key') <> 'string'
                            OR (original_row->>'key') COLLATE "C" IS DISTINCT FROM definition.key COLLATE "C"
                            OR pg_catalog.jsonb_typeof(original_row->'value') <> 'string'
                            OR pg_catalog.jsonb_typeof(original_row->'locked') <> 'boolean'
                            OR pg_catalog.jsonb_typeof(original_row->'created') <> 'string'
                            OR NOT pg_catalog.isfinite((original_row->>'created')::timestamptz)
                            OR pg_catalog.jsonb_typeof(original_row->'updated') NOT IN ('string','null')
                            OR original_row->'updated' <> 'null'::jsonb
                                AND NOT pg_catalog.isfinite((original_row->>'updated')::timestamptz)
                            OR pg_catalog.jsonb_typeof(original_row->'creator') NOT IN ('string','null')
                            OR pg_catalog.jsonb_typeof(original_row->'updater') NOT IN ('string','null') THEN
                            RAISE EXCEPTION 'configuration_import_tenant_setting_original_shape' USING ERRCODE = '22023';
                        END IF;
                    ELSIF mutation->>'id' !~ {Text("^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")} THEN
                        RAISE EXCEPTION 'configuration_import_tenant_setting_creation_shape' USING ERRCODE = '22023';
                    END IF;
                    IF EXISTS (SELECT 1 FROM {system} s
                        WHERE s.{S("SettingKey")} COLLATE "C" = definition.key COLLATE "C" AND s.{S("IsLocked")}) THEN
                        RAISE EXCEPTION 'configuration_import_tenant_setting_system_locked' USING ERRCODE = '42501';
                    END IF;
                    SELECT t.{T("IsLocked")} INTO existing_lock FROM {tenant} t
                        WHERE t.{T("TenantId")} = p_tenant
                            AND t.{T("SettingKey")} COLLATE "C" = definition.key COLLATE "C" FOR UPDATE;
                    row_exists := FOUND;
                    IF row_exists AND existing_lock THEN
                        RAISE EXCEPTION 'configuration_import_tenant_setting_tenant_locked' USING ERRCODE = '42501';
                    END IF;
                    IF original_row IS NULL THEN
                        IF row_exists THEN
                            RAISE EXCEPTION 'configuration_import_tenant_setting_state_changed' USING ERRCODE = '40001';
                        END IF;
                        INSERT INTO {tenant} ({T("Id")}, {T("TenantId")}, {T("SettingKey")}, {T("Value")},
                            {T("IsLocked")}, {T("CreatedAt")}, {T("CreatedBy")}, {T("UpdatedAt")}, {T("UpdatedBy")})
                        VALUES ((mutation->>'id')::uuid, p_tenant, definition.key, mutation->>'value',
                            false, p_at, p_actor, NULL, NULL)
                        ON CONFLICT ({T("TenantId")}, {T("SettingKey")}) DO NOTHING;
                    ELSE
                        UPDATE {tenant} t SET {T("Value")} = mutation->>'value',
                            {T("UpdatedAt")} = p_at, {T("UpdatedBy")} = p_actor
                        WHERE t.{T("Id")} = (original_row->>'id')::uuid AND t.{T("TenantId")} = p_tenant
                            AND t.{T("SettingKey")} COLLATE "C" = definition.key COLLATE "C"
                            AND ROW(t.{T("Value")} COLLATE "C", t.{T("IsLocked")}, t.{T("CreatedAt")},
                                t.{T("CreatedBy")}, t.{T("UpdatedAt")}, t.{T("UpdatedBy")})
                                IS NOT DISTINCT FROM ROW((original_row->>'value') COLLATE "C",
                                    (original_row->>'locked')::boolean, (original_row->>'created')::timestamptz,
                                    (original_row->>'creator')::uuid, (original_row->>'updated')::timestamptz,
                                    (original_row->>'updater')::uuid);
                    END IF;
                    GET DIAGNOSTICS affected = ROW_COUNT;
                    IF affected <> 1 THEN
                        RAISE EXCEPTION 'configuration_import_tenant_setting_state_changed' USING ERRCODE = '40001';
                    END IF;
                END LOOP;
                RETURN 'true'::jsonb;
            END;
            """;
    }
}
