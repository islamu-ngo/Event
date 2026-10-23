using System.Globalization;
using System.Text.Json;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;

namespace Explore.Persistence.Security;

public static partial class PostgresConfigurationImportEventResourceContract
{
    private static string FixedKeysSql() =>
        "ARRAY[" + string.Join(", ", EventResourceSettingMutationGuard.Keys.Select(Text)) + "]::text[]";

    private static string TenantKeysSql() =>
        "ARRAY[" + string.Join(", ", EventResourceSettingMutationGuard.Keys
            .Where(key => SettingRegistry.Get(key)!.MaxScope >= SettingScope.Tenant).Select(Text)) + "]::text[]";

    private static string RegistrySql() => string.Join(",\n", EventResourceSettingMutationGuard.Keys.Select(key =>
    {
        SettingDefinition definition = ValidateCatalog(key, null);
        var entry = ConfigurationManifestCatalog.InstanceSettings[key];
        string items = entry.StringArray?.AllowedValues is { } allowed
            ? Text(JsonSerializer.Serialize(allowed)) + "::jsonb" : "NULL::jsonb";
        long minimum = definition.ValueType == SettingValueType.Long ? 1 : 0;
        long maximum = key == EventResourceSettingDefinitions.AuditRetentionDays.Key ? 90
            : key == EventResourceSettingDefinitions.MaxActiveResources.Key ? 500 : long.MaxValue;
        return $"({Text(key)}, {(int)definition.ValueType}, {NullableText(definition.Description)}, " +
            $"{NullableText(definition.Category)}, {(definition.MaxScope >= SettingScope.Tenant ? "true" : "false")}, " +
            $"{items}, {minimum.ToString(CultureInfo.InvariantCulture)}::bigint, " +
            $"{maximum.ToString(CultureInfo.InvariantCulture)}::bigint)";
    }));

    private static string NullableText(string? value) => value is null ? "NULL::text" : Text(value);

    private static string LockSql() => string.Join("\n", EventResourceSettingMutationGuard.Keys.Order(StringComparer.Ordinal)
        .Select(key => "PERFORM pg_catalog.pg_advisory_xact_lock(" +
            RelationalSettingMutationLock.ComputeStableLockKey(key).ToString(CultureInfo.InvariantCulture) + "::bigint);"));

    private static string SnapshotSql(ExploreDbContext context, Type type, string alias)
    {
        string C(string property) => alias + "." + Column(context, type, property);
        bool system = type == typeof(SystemSetting);
        return $"""
            pg_catalog.jsonb_build_object(
                'id', {C("Id")}, 'tenant', {(system ? "NULL::uuid" : C("TenantId"))},
                'key', {C("SettingKey")}, 'value', {C("Value")}, 'locked', {C("IsLocked")},
                'type', {(system ? C("SettingValueTypeId") : "NULL::integer")},
                'allowed', {(system ? C("AllowedValues") + "::text" : "NULL::text")},
                'description', {(system ? C("Description") : "NULL::text")},
                'category', {(system ? C("Category") : "NULL::text")},
                'order', {(system ? C("DisplayOrder") : "NULL::integer")},
                'created', {C("CreatedAt")}, 'creator', {C("CreatedBy")},
                'updated', {C("UpdatedAt")}, 'updater', {C("UpdatedBy")})
            """;
    }

    private static string ReadBody(ExploreDbContext context) => $"""
        DECLARE result jsonb;
        BEGIN
            PERFORM {FunctionSql(context, "assert_authority")}();
            WITH locked AS MATERIALIZED (
                SELECT {SnapshotSql(context, typeof(SystemSetting), "s")} AS snapshot,
                    s.{Column(context, typeof(SystemSetting), "SettingKey")} AS key
                FROM {Table(context, typeof(SystemSetting))} s
                WHERE s.{Column(context, typeof(SystemSetting), "SettingKey")} = ANY ({FixedKeysSql()})
                ORDER BY s.{Column(context, typeof(SystemSetting), "SettingKey")} FOR UPDATE)
            SELECT COALESCE(pg_catalog.jsonb_agg(s.snapshot ORDER BY s.key), '[]'::jsonb)
                INTO result FROM locked s;
            RETURN result;
        END;
        """;

    private static string WriteBody(ExploreDbContext context)
    {
        string S(string property) => Column(context, typeof(SystemSetting), property);
        string T(string property) => Column(context, typeof(TenantSetting), property);
        string system = Table(context, typeof(SystemSetting));
        string tenant = Table(context, typeof(TenantSetting));
        string shape = """
            'id','tenant','key','value','locked','type','allowed','description','category','order',
            'created','creator','updated','updater'
            """;
        string Expected(string alias, Type type)
        {
            string C(string property) => alias + "." + Column(context, type, property);
            (string Property, string Field, string Cast)[] fields =
            [
                ("Value", "value", "text"), ("IsLocked", "locked", "boolean"),
                ("CreatedAt", "created", "timestamptz"), ("CreatedBy", "creator", "uuid"),
                ("UpdatedAt", "updated", "timestamptz"), ("UpdatedBy", "updater", "uuid")
            ];
            if (type == typeof(SystemSetting))
                fields = [.. fields, ("SettingValueTypeId", "type", "integer"),
                    ("AllowedValues", "allowed", "jsonb"), ("Description", "description", "text"),
                    ("Category", "category", "text"), ("DisplayOrder", "order", "integer")];
            return "ROW(" + string.Join(", ", fields.Select(field => C(field.Property))) +
                ") IS NOT DISTINCT FROM ROW(" +
                string.Join(", ", fields.Select(field => $"(p_original->>{Text(field.Field)})::{field.Cast}")) + ")";
        }
        return $"""
            DECLARE definition record; snapshot jsonb; requested jsonb;
                target uuid; affected integer;
            BEGIN
                PERFORM {FunctionSql(context, "assert_authority")}();
                IF p_current IS NULL OR p_actor IS NULL
                    OR p_actor = '00000000-0000-0000-0000-000000000000'::uuid
                    OR p_at IS NULL OR NOT pg_catalog.isfinite(p_at) THEN
                    RAISE EXCEPTION 'configuration_import_resource_write_shape' USING ERRCODE = '22023';
                END IF;
                FOREACH snapshot IN ARRAY ARRAY[p_current, p_original] LOOP
                    IF snapshot IS NULL THEN CONTINUE; END IF;
                    IF pg_catalog.jsonb_typeof(snapshot) <> 'object' THEN
                        RAISE EXCEPTION 'configuration_import_resource_row_shape' USING ERRCODE = '22023';
                    END IF;
                    IF NOT (snapshot ?& ARRAY[{shape}])
                        OR (SELECT count(*) FROM pg_catalog.jsonb_object_keys(snapshot)) <> 14
                        OR pg_catalog.jsonb_typeof(snapshot->'id') <> 'string'
                        OR (snapshot->>'id')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
                        OR pg_catalog.jsonb_typeof(snapshot->'key') <> 'string'
                        OR NOT (snapshot->>'key' = ANY ({FixedKeysSql()}))
                        OR pg_catalog.jsonb_typeof(snapshot->'value') <> 'string'
                        OR pg_catalog.jsonb_typeof(snapshot->'locked') <> 'boolean'
                        OR pg_catalog.jsonb_typeof(snapshot->'tenant') NOT IN ('string','null')
                        OR (snapshot->>'tenant')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
                        OR pg_catalog.jsonb_typeof(snapshot->'created') <> 'string'
                        OR NOT pg_catalog.isfinite((snapshot->>'created')::timestamptz)
                        OR pg_catalog.jsonb_typeof(snapshot->'updated') NOT IN ('string','null')
                        OR snapshot->'updated' <> 'null'::jsonb AND NOT pg_catalog.isfinite((snapshot->>'updated')::timestamptz)
                        OR pg_catalog.jsonb_typeof(snapshot->'creator') NOT IN ('string','null')
                        OR pg_catalog.jsonb_typeof(snapshot->'updater') NOT IN ('string','null')
                        OR pg_catalog.jsonb_typeof(snapshot->'allowed') NOT IN ('string','null')
                        OR pg_catalog.jsonb_typeof(snapshot->'description') NOT IN ('string','null')
                        OR pg_catalog.jsonb_typeof(snapshot->'category') NOT IN ('string','null')
                        OR snapshot->'tenant' = 'null'::jsonb AND (
                            pg_catalog.jsonb_typeof(snapshot->'type') <> 'number'
                            OR pg_catalog.jsonb_typeof(snapshot->'order') <> 'number')
                        OR snapshot->'tenant' <> 'null'::jsonb AND (
                            snapshot->'type' <> 'null'::jsonb OR snapshot->'order' <> 'null'::jsonb
                            OR snapshot->'allowed' <> 'null'::jsonb OR snapshot->'description' <> 'null'::jsonb
                            OR snapshot->'category' <> 'null'::jsonb) THEN
                        RAISE EXCEPTION 'configuration_import_resource_row_shape' USING ERRCODE = '22023';
                    END IF;
                END LOOP;
                SELECT * INTO STRICT definition FROM (VALUES {RegistrySql()})
                    d(key, value_type, description, category, tenant_allowed, items, minimum, maximum)
                    WHERE d.key = p_current->>'key';
                target := (p_current->>'tenant')::uuid;
                requested := (p_current->>'value')::jsonb;
                IF target IS NOT NULL AND NOT definition.tenant_allowed
                    OR (p_current->>'updated')::timestamptz IS DISTINCT FROM p_at
                    OR (p_current->>'updater')::uuid IS DISTINCT FROM p_actor THEN
                    RAISE EXCEPTION 'configuration_import_resource_scope_shape' USING ERRCODE = '22023';
                END IF;
                IF definition.value_type = {(int)SettingValueType.Boolean} THEN
                    IF pg_catalog.jsonb_typeof(requested) <> 'boolean' THEN
                        RAISE EXCEPTION 'configuration_import_resource_value_shape' USING ERRCODE = '22023';
                    END IF;
                ELSIF definition.value_type IN ({(int)SettingValueType.Integer}, {(int)SettingValueType.Long}) THEN
                    IF pg_catalog.jsonb_typeof(requested) <> 'number'
                        OR (p_current->>'value') !~ '^-?[0-9]+$' THEN
                        RAISE EXCEPTION 'configuration_import_resource_value_shape' USING ERRCODE = '22023';
                    END IF;
                    IF (p_current->>'value')::bigint NOT BETWEEN definition.minimum AND definition.maximum THEN
                        RAISE EXCEPTION 'configuration_import_resource_value_range' USING ERRCODE = '22023';
                    END IF;
                ELSE
                    IF pg_catalog.jsonb_typeof(requested) <> 'array' THEN
                        RAISE EXCEPTION 'configuration_import_resource_value_shape' USING ERRCODE = '22023';
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(requested) item
                        WHERE pg_catalog.jsonb_typeof(item) <> 'string'
                            OR definition.items IS NOT NULL AND NOT (definition.items @> pg_catalog.jsonb_build_array(item))
                            OR definition.items IS NULL AND (
                                item #>> ARRAY[]::text[] = ''
                                OR item #>> ARRAY[]::text[] !~ '^https://[^/?#@[:space:]]+$')) THEN
                        RAISE EXCEPTION 'configuration_import_resource_array_shape' USING ERRCODE = '22023';
                    END IF;
                END IF;
                IF p_original IS NULL THEN
                    IF p_current->>'id' !~ {Text("^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")}
                        OR (p_current->>'locked')::boolean
                        OR (p_current->>'created')::timestamptz IS DISTINCT FROM p_at
                        OR (p_current->>'creator')::uuid IS DISTINCT FROM p_actor
                        OR target IS NULL AND (
                            (p_current->>'type')::integer IS DISTINCT FROM definition.value_type
                            OR p_current->>'description' IS DISTINCT FROM definition.description
                            OR p_current->>'category' IS DISTINCT FROM definition.category
                            OR p_current->'allowed' <> 'null'::jsonb OR (p_current->>'order')::integer <> 0) THEN
                        RAISE EXCEPTION 'configuration_import_resource_creation_shape' USING ERRCODE = '22023';
                    END IF;
                ELSE
                    IF (p_current - ARRAY['value','updated','updater','created']) IS DISTINCT FROM
                            (p_original - ARRAY['value','updated','updater','created'])
                        OR (p_current->>'created')::timestamptz IS DISTINCT FROM (p_original->>'created')::timestamptz THEN
                        RAISE EXCEPTION 'configuration_import_resource_metadata_changed' USING ERRCODE = '22023';
                    END IF;
                END IF;
                IF target IS NOT NULL THEN
                    IF EXISTS (SELECT 1 FROM {system} s WHERE s.{S("SettingKey")} = definition.key AND s.{S("IsLocked")}) THEN
                        RAISE EXCEPTION 'configuration_import_resource_system_locked' USING ERRCODE = '42501';
                    END IF;
                    PERFORM 1
                        FROM {tenant} t WHERE t.{T("TenantId")} = target AND t.{T("SettingKey")} = definition.key FOR UPDATE;
                    IF p_original IS NULL THEN
                        IF FOUND THEN RETURN 'false'::jsonb; END IF;
                        INSERT INTO {tenant} ({T("Id")}, {T("TenantId")}, {T("SettingKey")}, {T("Value")},
                            {T("IsLocked")}, {T("CreatedAt")}, {T("CreatedBy")}, {T("UpdatedAt")}, {T("UpdatedBy")})
                        VALUES ((p_current->>'id')::uuid, target, definition.key, p_current->>'value',
                            false, p_at, p_actor, p_at, p_actor)
                        ON CONFLICT ({T("TenantId")}, {T("SettingKey")}) DO NOTHING;
                    ELSE
                        UPDATE {tenant} t SET {T("Value")} = p_current->>'value', {T("UpdatedAt")} = p_at, {T("UpdatedBy")} = p_actor
                        WHERE t.{T("Id")} = (p_original->>'id')::uuid AND t.{T("TenantId")} = target
                            AND t.{T("SettingKey")} = definition.key AND {Expected("t", typeof(TenantSetting))};
                    END IF;
                ELSE
                    PERFORM 1
                        FROM {system} s WHERE s.{S("SettingKey")} = definition.key FOR UPDATE;
                    IF p_original IS NULL THEN
                        IF FOUND THEN RETURN 'false'::jsonb; END IF;
                        INSERT INTO {system} ({S("Id")}, {S("SettingKey")}, {S("Value")}, {S("SettingValueTypeId")},
                            {S("IsLocked")}, {S("AllowedValues")}, {S("Description")}, {S("Category")}, {S("DisplayOrder")},
                            {S("CreatedAt")}, {S("CreatedBy")}, {S("UpdatedAt")}, {S("UpdatedBy")})
                        VALUES ((p_current->>'id')::uuid, definition.key, p_current->>'value', definition.value_type,
                            false, NULL, definition.description, definition.category, 0, p_at, p_actor, p_at, p_actor)
                        ON CONFLICT ({S("SettingKey")}) DO NOTHING;
                    ELSE
                        UPDATE {system} s SET {S("Value")} = p_current->>'value', {S("UpdatedAt")} = p_at, {S("UpdatedBy")} = p_actor
                        WHERE s.{S("Id")} = (p_original->>'id')::uuid AND s.{S("SettingKey")} = definition.key
                            AND {Expected("s", typeof(SystemSetting))};
                    END IF;
                END IF;
                GET DIAGNOSTICS affected = ROW_COUNT;
                RETURN pg_catalog.to_jsonb(affected = 1);
            END;
            """;
    }
}
