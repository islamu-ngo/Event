using System.Globalization;
using System.Text.Json;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Persistence.Seed;

namespace Explore.Persistence.Security;

public static partial class PostgresConfigurationImportPublicationPolicyContract
{
    private static string FixedKeysSql() =>
        "ARRAY[" + string.Join(", ", PublicationKeys.Select(Text)) + "]::text[]";

    private static string[] PublicationKeys => Explore.Application.Settings.PublicationPolicySettingKeys.All.ToArray();

    private static string RegistrySql()
    {
        Guid[] ids =
        [
            SeedIds.SystemSettingEventReportingIntakeEnabledId,
            SeedIds.SystemSettingRequireApprovalId,
            SeedIds.SystemSettingUserSubmissionEnabledId,
            SeedIds.SystemSettingOrgSubmissionEnabledId,
            SeedIds.SystemSettingGroupSubmissionEnabledId
        ];
        return string.Join(",\n", PublicationKeys.Select((key, index) =>
        {
            SettingDefinition definition = SettingRegistry.Get(key)!;
            string allowed = definition.AllowedValues is null
                ? "NULL::jsonb" : Text(JsonSerializer.Serialize(definition.AllowedValues)) + "::jsonb";
            return $"({Text(key)}, '{ids[index]:D}'::uuid, {(int)definition.ValueType}, {allowed}, " +
                $"{NullableText(definition.Description)}, {NullableText(definition.Category)}, {index + 1})";
        }));
    }

    private static string NullableText(string? value) => value is null ? "NULL::text" : Text(value);

    private static string LockSql() => string.Join("\n", PublicationKeys.Order(StringComparer.Ordinal)
        .Select(key => "PERFORM pg_catalog.pg_advisory_xact_lock(" +
            RelationalSettingMutationLock.ComputeStableLockKey(key).ToString(CultureInfo.InvariantCulture) + "::bigint);"));

    private static string ReadBody(ExploreDbContext context)
    {
        string S(string property) => Column(context, typeof(SystemSetting), property);
        string T(string property) => Column(context, typeof(TenantSetting), property);
        return $"""
            DECLARE systems jsonb; tenants jsonb; next_cursor uuid; tenant_count integer;
            BEGIN
                PERFORM {FunctionSql(context, "assert_authority")}();
                IF p_limit IS NULL OR p_limit < 1 OR p_limit > {TenantPageSize}
                    OR p_tenant = '00000000-0000-0000-0000-000000000000'::uuid
                    OR p_tenant IS NOT NULL AND p_after IS NOT NULL THEN
                    RAISE EXCEPTION 'configuration_import_publication_read_shape' USING ERRCODE = '22023';
                END IF;
                WITH locked AS MATERIALIZED (
                    SELECT s.{S("SettingKey")} AS key, s.{S("Value")} AS value, s.{S("IsLocked")} AS locked
                    FROM {Table(context, typeof(SystemSetting))} s
                    WHERE s.{S("SettingKey")} = ANY ({FixedKeysSql()})
                    ORDER BY s.{S("SettingKey")} FOR UPDATE)
                SELECT COALESCE(pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
                    'key', key, 'value', value, 'locked', locked)), '[]'::jsonb) INTO systems FROM locked;
                WITH page AS MATERIALIZED (
                    SELECT DISTINCT t.{T("TenantId")} AS tenant
                    FROM {Table(context, typeof(TenantSetting))} t
                    WHERE t.{T("SettingKey")} = ANY ({FixedKeysSql()})
                        AND (p_tenant IS NULL OR t.{T("TenantId")} = p_tenant)
                        AND (p_after IS NULL OR t.{T("TenantId")} > p_after)
                    ORDER BY tenant LIMIT p_limit),
                locked AS MATERIALIZED (
                    SELECT t.{T("TenantId")} AS tenant, t.{T("SettingKey")} AS key, t.{T("Value")} AS value
                    FROM {Table(context, typeof(TenantSetting))} t JOIN page p ON p.tenant = t.{T("TenantId")}
                    WHERE t.{T("SettingKey")} = ANY ({FixedKeysSql()})
                    ORDER BY t.{T("TenantId")}, t.{T("SettingKey")} FOR UPDATE OF t)
                SELECT COALESCE((SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
                            'tenant', tenant, 'key', key, 'value', value) ORDER BY tenant, key) FROM locked), '[]'::jsonb),
                    (SELECT count(*) FROM page),
                    (SELECT tenant FROM page ORDER BY tenant DESC LIMIT 1)
                    INTO tenants, tenant_count, next_cursor;
                RETURN pg_catalog.jsonb_build_object('systems', systems, 'tenants', tenants,
                    'next', CASE WHEN p_tenant IS NULL AND tenant_count = p_limit THEN next_cursor ELSE NULL END);
            END;
            """;
    }

    private static string WriteBody(ExploreDbContext context)
    {
        string S(string property) => Column(context, typeof(SystemSetting), property);
        string T(string property) => Column(context, typeof(TenantSetting), property);
        string system = Table(context, typeof(SystemSetting));
        string tenant = Table(context, typeof(TenantSetting));
        return $"""
            DECLARE mutation jsonb; definition record; old_value text; old_id uuid; old_lock boolean;
                old_type integer; old_allowed jsonb; old_description text; old_category text; old_order integer;
                old_created timestamptz; old_creator uuid; old_updated timestamptz; old_updater uuid;
                row_exists boolean; affected integer; result jsonb := '[]'::jsonb; new_value text;
                new_id uuid; new_lock boolean;
            BEGIN
                PERFORM {FunctionSql(context, "assert_authority")}();
                IF p_mutations IS NULL OR pg_catalog.jsonb_typeof(p_mutations) <> 'array'
                    OR pg_catalog.jsonb_array_length(p_mutations) > {PublicationKeys.Length}
                    OR p_at IS NULL OR NOT pg_catalog.isfinite(p_at)
                    OR p_actor = '00000000-0000-0000-0000-000000000000'::uuid
                    OR p_tenant = '00000000-0000-0000-0000-000000000000'::uuid
                    OR p_tenant IS NULL AND p_actor IS NULL THEN
                    RAISE EXCEPTION 'configuration_import_publication_write_shape' USING ERRCODE = '22023';
                END IF;
                IF EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(p_mutations) m
                    WHERE pg_catalog.jsonb_typeof(m) <> 'object'
                        OR NOT (m ?& ARRAY['key','kind','value','tenant','locked','id'])
                        OR (SELECT count(*) FROM pg_catalog.jsonb_object_keys(m)) <> 6
                        OR pg_catalog.jsonb_typeof(m->'key') <> 'string'
                        OR NOT (m->>'key' = ANY ({FixedKeysSql()}))
                        OR pg_catalog.jsonb_typeof(m->'kind') <> 'string'
                        OR m->>'kind' NOT IN ('set','remove')
                        OR (m->>'tenant')::uuid IS DISTINCT FROM p_tenant
                        OR (p_tenant IS NULL AND m->'tenant' <> 'null'::jsonb)
                        OR (p_tenant IS NOT NULL AND pg_catalog.jsonb_typeof(m->'tenant') <> 'string')
                        OR (m->>'kind' = 'remove' AND (m->'value' <> 'null'::jsonb
                            OR m->'locked' <> 'null'::jsonb OR m->'id' <> 'null'::jsonb))
                        OR (m->>'kind' = 'set' AND (
                            pg_catalog.jsonb_typeof(m->'value') <> 'string'
                            OR pg_catalog.jsonb_typeof((m->>'value')::jsonb) <> 'boolean'
                            OR (p_tenant IS NULL AND (pg_catalog.jsonb_typeof(m->'locked') <> 'boolean'
                                OR m->'id' <> 'null'::jsonb))
                            OR (p_tenant IS NOT NULL AND (m->'locked' <> 'null'::jsonb
                                OR pg_catalog.jsonb_typeof(m->'id') <> 'string'
                                OR (m->>'id') !~ {Text("^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")})))))
                    OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(p_mutations) m
                        GROUP BY m->>'key' HAVING count(*) > 1) THEN
                    RAISE EXCEPTION 'configuration_import_publication_mutation_shape' USING ERRCODE = '22023';
                END IF;
                FOR mutation IN SELECT m FROM pg_catalog.jsonb_array_elements(p_mutations) m
                    ORDER BY pg_catalog.array_position({FixedKeysSql()}, m->>'key') LOOP
                    new_value := mutation->>'value';
                    new_lock := (mutation->>'locked')::boolean;
                    IF p_tenant IS NOT NULL THEN
                        SELECT t.{T("Id")}, t.{T("Value")}, t.{T("IsLocked")},
                            t.{T("CreatedAt")}, t.{T("CreatedBy")}, t.{T("UpdatedAt")}, t.{T("UpdatedBy")}
                            INTO old_id, old_value, old_lock, old_created, old_creator, old_updated, old_updater
                            FROM {tenant} t WHERE t.{T("TenantId")} = p_tenant
                                AND t.{T("SettingKey")} = mutation->>'key' FOR UPDATE;
                        row_exists := FOUND;
                        IF mutation->>'kind' = 'remove' THEN
                            IF NOT row_exists THEN CONTINUE; END IF;
                            DELETE FROM {tenant} t WHERE t.{T("Id")} = old_id
                                AND t.{T("TenantId")} = p_tenant AND t.{T("SettingKey")} = mutation->>'key'
                                AND ROW(t.{T("Value")}, t.{T("IsLocked")}, t.{T("CreatedAt")}, t.{T("CreatedBy")},
                                    t.{T("UpdatedAt")}, t.{T("UpdatedBy")})
                                IS NOT DISTINCT FROM ROW(old_value, old_lock, old_created, old_creator,
                                    old_updated, old_updater);
                        ELSIF NOT row_exists THEN
                            new_id := (mutation->>'id')::uuid;
                            INSERT INTO {tenant} ({T("Id")}, {T("TenantId")}, {T("SettingKey")},
                                {T("Value")}, {T("IsLocked")}, {T("CreatedAt")}, {T("CreatedBy")})
                            VALUES (new_id, p_tenant, mutation->>'key', new_value, false, p_at, p_actor)
                            ON CONFLICT ({T("TenantId")}, {T("SettingKey")}) DO NOTHING;
                        ELSE
                            IF old_value IS NOT DISTINCT FROM new_value THEN CONTINUE; END IF;
                            UPDATE {tenant} t SET {T("Value")} = new_value,
                                {T("UpdatedAt")} = p_at, {T("UpdatedBy")} = p_actor
                            WHERE t.{T("Id")} = old_id AND t.{T("TenantId")} = p_tenant
                                AND t.{T("SettingKey")} = mutation->>'key'
                                AND ROW(t.{T("Value")}, t.{T("IsLocked")}, t.{T("CreatedAt")}, t.{T("CreatedBy")},
                                    t.{T("UpdatedAt")}, t.{T("UpdatedBy")})
                                IS NOT DISTINCT FROM ROW(old_value, old_lock, old_created, old_creator,
                                    old_updated, old_updater);
                        END IF;
                    ELSE
                        SELECT * INTO STRICT definition FROM (VALUES {RegistrySql()})
                            d(key, id, value_type, allowed_values, description, category, display_order)
                            WHERE d.key = mutation->>'key';
                        SELECT s.{S("Id")}, s.{S("Value")}, s.{S("IsLocked")}, s.{S("SettingValueTypeId")},
                            s.{S("AllowedValues")}, s.{S("Description")}, s.{S("Category")}, s.{S("DisplayOrder")},
                            s.{S("CreatedAt")}, s.{S("CreatedBy")}, s.{S("UpdatedAt")}, s.{S("UpdatedBy")}
                            INTO old_id, old_value, old_lock, old_type, old_allowed, old_description,
                                old_category, old_order, old_created, old_creator, old_updated, old_updater
                            FROM {system} s WHERE s.{S("SettingKey")} = mutation->>'key' FOR UPDATE;
                        row_exists := FOUND;
                        IF mutation->>'kind' = 'remove' THEN
                            IF NOT row_exists THEN CONTINUE; END IF;
                            DELETE FROM {system} s WHERE s.{S("Id")} = old_id
                                AND s.{S("SettingKey")} = mutation->>'key'
                                AND ROW(s.{S("Value")}, s.{S("IsLocked")}, s.{S("SettingValueTypeId")},
                                    s.{S("AllowedValues")}, s.{S("Description")}, s.{S("Category")},
                                    s.{S("DisplayOrder")}, s.{S("CreatedAt")}, s.{S("CreatedBy")},
                                    s.{S("UpdatedAt")}, s.{S("UpdatedBy")})
                                IS NOT DISTINCT FROM ROW(old_value, old_lock, old_type, old_allowed,
                                    old_description, old_category, old_order, old_created, old_creator,
                                    old_updated, old_updater);
                        ELSIF NOT row_exists THEN
                            INSERT INTO {system} ({S("Id")}, {S("SettingKey")}, {S("Value")}, {S("SettingValueTypeId")},
                                {S("IsLocked")}, {S("AllowedValues")}, {S("Description")}, {S("Category")},
                                {S("DisplayOrder")}, {S("CreatedAt")}, {S("CreatedBy")})
                            VALUES (definition.id, definition.key, new_value, definition.value_type, new_lock,
                                definition.allowed_values, definition.description, definition.category,
                                definition.display_order, p_at, p_actor)
                            ON CONFLICT ({S("SettingKey")}) DO NOTHING;
                        ELSE
                            IF ROW(old_value, old_lock, old_type, old_allowed, old_description, old_category, old_order)
                                IS NOT DISTINCT FROM ROW(new_value, new_lock, definition.value_type,
                                    definition.allowed_values, definition.description, definition.category,
                                    definition.display_order) THEN CONTINUE; END IF;
                            UPDATE {system} s SET {S("Value")} = new_value, {S("IsLocked")} = new_lock,
                                {S("SettingValueTypeId")} = definition.value_type,
                                {S("AllowedValues")} = definition.allowed_values,
                                {S("Description")} = definition.description, {S("Category")} = definition.category,
                                {S("DisplayOrder")} = definition.display_order,
                                {S("UpdatedAt")} = p_at, {S("UpdatedBy")} = p_actor
                            WHERE s.{S("Id")} = old_id AND s.{S("SettingKey")} = mutation->>'key'
                                AND ROW(s.{S("Value")}, s.{S("IsLocked")}, s.{S("SettingValueTypeId")},
                                    s.{S("AllowedValues")}, s.{S("Description")}, s.{S("Category")},
                                    s.{S("DisplayOrder")}, s.{S("CreatedAt")}, s.{S("CreatedBy")},
                                    s.{S("UpdatedAt")}, s.{S("UpdatedBy")})
                                IS NOT DISTINCT FROM ROW(old_value, old_lock, old_type, old_allowed,
                                    old_description, old_category, old_order, old_created, old_creator,
                                    old_updated, old_updater);
                        END IF;
                    END IF;
                    GET DIAGNOSTICS affected = ROW_COUNT;
                    IF affected <> 1 THEN
                        RAISE EXCEPTION 'configuration_import_publication_state_changed' USING ERRCODE = '40001';
                    END IF;
                    result := result || pg_catalog.jsonb_build_array(pg_catalog.jsonb_build_object(
                        'key', mutation->>'key', 'old', CASE WHEN row_exists THEN old_value ELSE NULL END,
                        'new', CASE WHEN mutation->>'kind' = 'remove' THEN NULL ELSE new_value END));
                END LOOP;
                RETURN result;
            END;
            """;
    }
}
