using System.Globalization;
using System.Text.Json;
using Explore.Domain;

namespace Explore.Persistence.Security;

public static partial class PostgresConfigurationImportScalarSettingContract
{
    private static string RegistrySql() =>
        string.Join(",\n", OrdinaryEntries().Select(entry =>
        {
            var definition = entry.Definition;
            // Today's ordinary catalog contains strings and booleans only. A future
            // numeric/document admission needs its own explicit SQL shape contract.
            if (definition.ValueType is not (SettingValueType.String or SettingValueType.Boolean))
                throw new InvalidOperationException("configuration_import_scalar_value_contract_changed");
            string allowed = definition.AllowedValues is null
                ? "NULL::jsonb" : Text(JsonSerializer.Serialize(definition.AllowedValues)) + "::jsonb";
            return $"({Text(definition.Key)}, {(int)definition.ValueType}, " +
                $"{Text(definition.Description)}, {Text(definition.Category)}, {allowed}, " +
                $"{(entry.MaximumStringLength is { } maximum ? maximum.ToString(CultureInfo.InvariantCulture) : "NULL::integer")}, " +
                $"{RelationalSettingMutationLock.ComputeStableLockKey(definition.Key).ToString(CultureInfo.InvariantCulture)}::bigint)";
        }));

    private static string UpsertBody(ExploreDbContext context)
    {
        string S(string property) => Column(context, property);
        string table = Table(context);
        const string uuidPattern = "^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$";
        const string utcPattern = "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\\.[0-9]{7}Z$";
        return $"""
            DECLARE definition record; parsed jsonb; occurred_at timestamptz;
                old_id uuid; old_value text; affected integer; row_exists boolean;
            BEGIN
                PERFORM {FunctionSql(context, "assert_authority")}();
                SELECT d.* INTO definition FROM (VALUES {RegistrySql()})
                    d(key, value_type, description, category, allowed_values, maximum_length, lock_key)
                    WHERE d.key COLLATE "C" = p_key COLLATE "C";
                IF NOT FOUND THEN
                    RAISE EXCEPTION 'configuration_import_scalar_key_not_allowed' USING ERRCODE = '22023';
                END IF;
                IF p_value IS NULL OR p_type IS DISTINCT FROM definition.value_type
                    OR p_description COLLATE "C" IS DISTINCT FROM definition.description COLLATE "C"
                    OR p_category COLLATE "C" IS DISTINCT FROM definition.category COLLATE "C"
                    OR p_locked IS DISTINCT FROM false OR p_allowed IS NOT NULL
                    OR p_order IS DISTINCT FROM 0 OR p_id IS NULL OR p_id::text !~ {Text(uuidPattern)}
                    OR p_actor IS NULL OR p_actor = '00000000-0000-0000-0000-000000000000'::uuid
                    OR p_created IS NULL OR p_created !~ {Text(utcPattern)}
                    OR p_updated IS DISTINCT FROM p_created THEN
                    RAISE EXCEPTION 'configuration_import_scalar_metadata_invalid' USING ERRCODE = '22023';
                END IF;
                occurred_at := p_created::timestamptz;
                IF NOT pg_catalog.isfinite(occurred_at)
                    OR occurred_at < '0001-01-01T00:00:00Z'::timestamptz
                    OR occurred_at >= '10000-01-01T00:00:00Z'::timestamptz THEN
                    RAISE EXCEPTION 'configuration_import_scalar_timestamp_invalid' USING ERRCODE = '22023';
                END IF;
                parsed := p_value::jsonb;
                IF (definition.value_type = 2 AND pg_catalog.jsonb_typeof(parsed) <> 'boolean')
                    OR (definition.value_type = 0 AND (
                        pg_catalog.jsonb_typeof(parsed) <> 'string'
                        OR definition.maximum_length IS NOT NULL AND (
                            SELECT COALESCE(sum(CASE WHEN pg_catalog.ascii(pg_catalog.substr(
                                parsed #>> ARRAY[]::text[], character_position, 1)) > 65535 THEN 2 ELSE 1 END), 0)
                            FROM pg_catalog.generate_series(1, pg_catalog.length(parsed #>> ARRAY[]::text[]))
                                positions(character_position)
                        ) > definition.maximum_length
                        OR definition.allowed_values IS NOT NULL AND NOT EXISTS (
                            SELECT 1 FROM pg_catalog.jsonb_array_elements_text(definition.allowed_values) allowed(value)
                            WHERE allowed.value COLLATE "C" = (parsed #>> ARRAY[]::text[]) COLLATE "C")))
                    OR definition.value_type NOT IN (0, 2) THEN
                    RAISE EXCEPTION 'configuration_import_scalar_value_invalid' USING ERRCODE = '22023';
                END IF;
                PERFORM pg_catalog.pg_advisory_xact_lock(definition.lock_key);
                SELECT s.{S("Id")}, s.{S("Value")} INTO old_id, old_value
                    FROM {table} s WHERE s.{S("SettingKey")} COLLATE "C" = definition.key COLLATE "C"
                    FOR UPDATE;
                row_exists := FOUND;
                IF row_exists THEN
                    UPDATE {table} s SET {S("Value")} = p_value,
                        {S("SettingValueTypeId")} = definition.value_type,
                        {S("IsLocked")} = false, {S("AllowedValues")} = NULL,
                        {S("Description")} = definition.description, {S("Category")} = definition.category,
                        {S("DisplayOrder")} = 0, {S("UpdatedAt")} = occurred_at, {S("UpdatedBy")} = p_actor
                    WHERE s.{S("Id")} = old_id
                        AND s.{S("SettingKey")} COLLATE "C" = definition.key COLLATE "C";
                ELSE
                    INSERT INTO {table} ({S("Id")}, {S("SettingKey")}, {S("Value")},
                        {S("SettingValueTypeId")}, {S("IsLocked")}, {S("AllowedValues")},
                        {S("Description")}, {S("Category")}, {S("DisplayOrder")},
                        {S("CreatedAt")}, {S("CreatedBy")}, {S("UpdatedAt")}, {S("UpdatedBy")})
                    VALUES (p_id, definition.key, p_value, definition.value_type, false, NULL,
                        definition.description, definition.category, 0, occurred_at, p_actor, occurred_at, p_actor)
                    ON CONFLICT ({S("SettingKey")}) DO NOTHING;
                END IF;
                GET DIAGNOSTICS affected = ROW_COUNT;
                IF affected <> 1 THEN
                    RAISE EXCEPTION 'configuration_import_scalar_state_changed' USING ERRCODE = '40001';
                END IF;
                RETURN old_value;
            END;
            """;
    }
}
