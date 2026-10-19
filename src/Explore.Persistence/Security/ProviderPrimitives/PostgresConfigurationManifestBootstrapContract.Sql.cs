using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;

namespace Explore.Persistence.Security;

public static partial class PostgresConfigurationManifestBootstrapContract
{
    /// <summary>Preserves literal text without terminating an enclosing dollar-quoted SQL body.</summary>
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static string Values(IEnumerable<string> values) => "ARRAY[" + string.Join(", ", values.Select(Text)) + "]::text[]";
    private static string Ids(string tag) => $"SELECT value::uuid FROM pg_catalog.jsonb_array_elements_text(p_scope->{Text(tag)}) value";
    private static string Ticks(string value) => $"((EXTRACT(EPOCH FROM {value}) * 10000000)::bigint + 621355968000000000)";

    private static string Project(ConfigurationManifestBootstrapData.Relation relation, string alias) =>
        string.Join(" || ", relation.Columns.Chunk(20).Select(columns =>
            "pg_catalog.jsonb_build_object(" + string.Join(", ", columns.Select(column =>
                Text(column.Name) + ", " + (column.Time ? Ticks(alias + "." + column.Sql) : alias + "." + column.Sql))) + ")"));

    private static string Value(ConfigurationManifestBootstrapData.Column column, string row)
    {
        string value = $"{row}->>{Text(column.Name)}";
        if (column.Time)
        {
            string ticks = $"({value})::bigint";
            return $"(TIMESTAMPTZ '0001-01-01 00:00:00+00' + ({ticks} / 864000000000)::integer * INTERVAL '1 day' " +
                $"+ (({ticks} % 864000000000) / 10)::bigint * INTERVAL '1 microsecond')";
        }
        return column.Json ? $"NULLIF({row}->{Text(column.Name)}, 'null'::jsonb)::{column.StoreType}" : $"({value})::{column.StoreType}";
    }

    private static string Predicate(ConfigurationManifestBootstrapData data, ConfigurationManifestBootstrapData.Relation relation)
    {
        string F(string name) => "r." + relation.Field(name);
        var tenant = data.Relations.Single(item => item.Tag == "Tenants");
        var policy = data.Relations.Single(item => item.Tag == "Policies");
        string tenants = $"SELECT {tenant.Field("Id")} FROM {tenant.Table} WHERE {tenant.Field("Slug")} IN " +
            "(SELECT value FROM pg_catalog.jsonb_array_elements_text(p_scope->'Slugs') value)";
        string policies = $"SELECT {policy.Field("Id")} FROM {policy.Table} WHERE {policy.Field("IsActive")} " +
            $"OR {policy.Field("Id")} IN ({Ids("Policies")})";
        return relation.Tag switch
        {
            "Systems" => $"{F("SettingKey")} = ANY ({Values(ConfigurationManifestBootstrapData.SystemReadKeys)})",
            "Tenants" => $"{F("Id")} IN ({tenants}) OR {F("Id")} IN ({Ids("Tenants")})",
            "Overrides" => $"{F("SettingKey")} = ANY ({Values(ConfigurationManifestBootstrapData.CrossTenantKeys)}) OR " +
                $"({F("TenantId")} IN ({tenants}) OR {F("TenantId")} IN ({Ids("Tenants")})) AND " +
                $"{F("SettingKey")} = ANY ({Values(ConfigurationManifestCatalog.TenantSettings.Keys)})",
            "Documents" => $"({F("TenantId")} IN ({tenants}) OR {F("TenantId")} IN ({Ids("Tenants")}) OR {F("Id")} IN ({Ids("Documents")})) " +
                $"AND {F("DocumentKey")} = ANY ({Values([SettingsDocumentKeys.Tenant.Branding, SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity])})",
            "Policies" => $"{F("IsActive")} OR {F("Id")} IN ({Ids("Policies")})",
            "Organizers" or "Currencies" or "Refunds" or "Limits" => $"{F("PaidEventPolicyVersionId")} IN ({policies})",
            "Operations" => $"{F("Id")} IN ({Ids("Operations")}) OR {F("Id")} = (SELECT {relation.Field("Id")} FROM {relation.Table} " +
                $"WHERE {relation.Field("Status")} = {(int)ConfigurationManifestOperationStatus.Applied} " +
                $"AND {relation.Field("BootstrapGeneration")} IS NOT NULL ORDER BY {relation.Field("BootstrapGeneration")} DESC, " +
                $"{relation.Field("CompletedAt")} DESC, {relation.Field("Id")} DESC LIMIT 1)",
            "Results" => $"{F("OperationId")} = (p_scope->>'Operation')::uuid",
            "Outbox" => $"{F("Id")} = (p_scope->>'Effect')::uuid AND {F("AggregateId")} = (p_scope->>'Operation')::uuid " +
                $"AND {F("AggregateType")} = {Text(ConfigurationManifestEffectOutbox.AggregateType)} AND {F("EventType")} = {Text(ConfigurationManifestEffectOutbox.EventType)}",
            _ => throw new InvalidOperationException("Unknown bootstrap source relation.")
        };
    }

    private static string ScopeCheck() => """
        IF p_scope IS NULL OR pg_catalog.jsonb_typeof(p_scope) <> 'object'
            OR NOT (p_scope ?& ARRAY['Slugs','Tenants','CreatedTenants','Documents','Policies','Operations','Operation','Effect'])
            OR (SELECT count(*) FROM pg_catalog.jsonb_object_keys(p_scope)) <> 8
            OR EXISTS (SELECT 1 FROM unnest(ARRAY['Slugs','Tenants','CreatedTenants','Documents','Policies','Operations']) key
                WHERE pg_catalog.jsonb_typeof(p_scope->key) <> 'array')
            OR pg_catalog.jsonb_array_length(p_scope->'Slugs') NOT BETWEEN 1 AND 256
            OR pg_catalog.jsonb_typeof(p_scope->'Operation') <> 'string'
            OR pg_catalog.jsonb_typeof(p_scope->'Effect') <> 'string' THEN
            RAISE EXCEPTION 'manifest_bootstrap_scope_invalid' USING ERRCODE = '22023';
        END IF;
        """;

    private static string ReadBody(ConfigurationManifestBootstrapData data, string authority, bool retainSource)
    {
        string relationFence = retainSource
            ? $"LOCK TABLE {string.Join(", ", data.Relations.Select(relation => relation.Table).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))} IN SHARE MODE;"
            : "";
        var reads = data.Relations.Select(relation =>
        {
            string columns = string.Join(", ", relation.Columns.Select(column => "r." + column.Sql));
            string order = string.Join(", ", relation.Keys.Select(column => "r." + column.Sql
                + (column.Property.ClrType == typeof(string) ? " COLLATE \"C\"" : "")));
            return $"""
                SELECT COALESCE(pg_catalog.jsonb_agg({Project(relation, "r")} ORDER BY {order}), '[]'::jsonb)
                    INTO rows FROM (SELECT {columns} FROM {relation.Table} r
                        WHERE {Predicate(data, relation)} ORDER BY {order} {(retainSource ? "FOR UPDATE" : "")}) r;
                image := image || pg_catalog.jsonb_build_object({Text(relation.Tag)}, rows);
                """;
        });
        return $"""
            DECLARE image jsonb := pg_catalog.jsonb_build_object(); rows jsonb;
            BEGIN
                PERFORM {authority}();
                {ScopeCheck()}
                {(retainSource ? $"PERFORM pg_catalog.pg_advisory_xact_lock({RelationalSettingMutationLock.ComputeStableLockKey(ConfigurationManifestLockKeys.InstanceManifest).ToString(System.Globalization.CultureInfo.InvariantCulture)}::bigint);" : "")}
                {relationFence}
                {string.Join("\n", reads)}
                RETURN image;
            END;
            """;
    }

    private static string[] UpdateNames(ConfigurationManifestBootstrapData.Relation relation) => relation.Tag switch
    {
        "Systems" => relation.Columns.Where(column => column.Name is not ("Id" or "SettingKey" or "CreatedAt" or "CreatedBy"))
            .Select(column => column.Name).ToArray(),
        "Policies" => ["IsActive", "ActiveUniquenessSlot", "UpdatedAt", "UpdatedBy"],
        _ => []
    };

    private static string Shape(ConfigurationManifestBootstrapData.Relation relation)
    {
        string In(string field, string tag) => $"(after_row->>{Text(field)})::uuid IN ({Ids(tag)})";
        string InsertOnly(string condition) => $"before_row <> 'null'::jsonb OR NOT ({condition})";
        return relation.Tag switch
        {
            "Systems" => $"NOT (after_row->>'SettingKey' = ANY ({Values(ConfigurationManifestCatalog.InstanceSettings.Keys)}))",
            "Tenants" => InsertOnly($"{In("Id", "CreatedTenants")} AND after_row->>'Slug' IN " +
                $"(SELECT value FROM pg_catalog.jsonb_array_elements_text(p_scope->'Slugs') value) " +
                $"AND (after_row->>'TenantStatusId')::integer = {(int)TenantStatusEnum.Provisioning}"),
            "Overrides" => InsertOnly($"{In("TenantId", "CreatedTenants")} AND after_row->>'SettingKey' = ANY " +
                $"({Values(ConfigurationManifestCatalog.TenantSettings.Keys)}) AND after_row->'IsLocked' = 'false'::jsonb"),
            "Documents" => InsertOnly($"{In("TenantId", "CreatedTenants")} AND {In("Id", "Documents")} " +
                $"AND after_row->>'DocumentKey' = ANY ({Values([SettingsDocumentKeys.Tenant.Branding, SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity])})"),
            "Policies" => $"NOT ({In("Id", "Policies")}) OR (before_row = 'null'::jsonb " +
                $"AND after_row->'TenantId' <> 'null'::jsonb AND NOT ({In("TenantId", "CreatedTenants")})) " +
                "OR before_row <> 'null'::jsonb AND (before_row->'TenantId' <> 'null'::jsonb " +
                "OR after_row->'IsActive' <> 'false'::jsonb OR after_row->'ActiveUniquenessSlot' <> before_row->'VersionNumber')",
            "Organizers" or "Currencies" or "Refunds" or "Limits" => InsertOnly(In("PaidEventPolicyVersionId", "Policies")),
            "Operations" => InsertOnly($"after_row->>'Id' = p_scope->>'Operation' " +
                $"AND (after_row->>'Mode')::integer = {(int)ConfigurationManifestAuditMode.Bootstrap} " +
                $"AND (after_row->>'Status')::integer IN ({(int)ConfigurationManifestOperationStatus.Applied},{(int)ConfigurationManifestOperationStatus.Failed})"),
            "Results" => InsertOnly($"after_row->>'OperationId' = p_scope->>'Operation' AND {In("TenantId", "Tenants")}"),
            "Outbox" => InsertOnly($"after_row->>'Id' = p_scope->>'Effect' AND after_row->>'AggregateId' = p_scope->>'Operation' " +
                $"AND after_row->>'AggregateType' = {Text(ConfigurationManifestEffectOutbox.AggregateType)} " +
                $"AND after_row->>'EventType' = {Text(ConfigurationManifestEffectOutbox.EventType)} AND after_row->'Payload' = 'null'::jsonb " +
                $"AND (after_row->>'Status')::integer = {(int)OutboxMessageStatus.Pending}"),
            _ => "true"
        };
    }

    private static string WriteBody(ConfigurationManifestBootstrapData data, string authority, string source)
    {
        var branches = data.Relations.Select(relation =>
        {
            string fields = Values(relation.Columns.Select(column => column.Name));
            string predicate = string.Join(" AND ", relation.Columns.Select(column =>
                $"r.{column.Sql} IS NOT DISTINCT FROM {Value(column, "before_row")}"));
            var updates = UpdateNames(relation).Select(name => relation.Columns.Single(column => column.Name == name)).ToArray();
            string immutable = string.Join(" OR ", relation.Columns.Except(updates).Select(column =>
                $"after_row->{Text(column.Name)} IS DISTINCT FROM before_row->{Text(column.Name)}"));
            string update = updates.Length == 0
                ? "RAISE EXCEPTION 'manifest_bootstrap_update_forbidden' USING ERRCODE = '42501';"
                : $"IF {immutable} THEN RAISE EXCEPTION 'manifest_bootstrap_identity_changed' USING ERRCODE = '42501'; END IF;\n" +
                    $"UPDATE {relation.Table} r SET {string.Join(", ", updates.Select(column => column.Sql + " = " + Value(column, "after_row")))} " +
                    $"WHERE {predicate};";
            string audit = relation.Columns.Any(column => column.Name == "CreatedBy")
                ? "OR before_row = 'null'::jsonb AND after_row->'CreatedBy' <> 'null'::jsonb" : "";
            audit += relation.Columns.Any(column => column.Name == "UpdatedBy")
                ? " OR after_row->'UpdatedBy' <> 'null'::jsonb" : "";
            string key = "pg_catalog.jsonb_build_array(" + string.Join(", ", relation.Keys.Select(column => $"after_row->{Text(column.Name)}")) + ")";
            return $"""
                WHEN {Text(relation.Tag)} THEN
                    IF NOT (after_row ?& {fields}) OR (SELECT count(*) FROM pg_catalog.jsonb_object_keys(after_row)) <> {relation.Columns.Length}
                        OR before_row <> 'null'::jsonb AND (NOT (before_row ?& {fields})
                            OR (SELECT count(*) FROM pg_catalog.jsonb_object_keys(before_row)) <> {relation.Columns.Length})
                        OR {Shape(relation)} {audit} THEN
                        RAISE EXCEPTION 'manifest_bootstrap_effect_invalid' USING ERRCODE = '42501';
                    END IF;
                    identity := pg_catalog.jsonb_build_array(change->>'Tag', {key});
                    IF seen @> pg_catalog.jsonb_build_array(identity) THEN
                        RAISE EXCEPTION 'manifest_bootstrap_duplicate_effect' USING ERRCODE = '42501';
                    END IF;
                    seen := seen || pg_catalog.jsonb_build_array(identity);
                    IF before_row = 'null'::jsonb THEN
                        INSERT INTO {relation.Table} ({string.Join(", ", relation.Columns.Select(column => column.Sql))})
                        VALUES ({string.Join(", ", relation.Columns.Select(column => Value(column, "after_row")))}) ON CONFLICT DO NOTHING;
                    ELSE
                        {update}
                    END IF;
                """;
        });
        return $"""
            DECLARE change jsonb; before_row jsonb; after_row jsonb; affected integer;
                seen jsonb := '[]'::jsonb; identity jsonb;
            BEGIN
                PERFORM {authority}();
                {ScopeCheck()}
                IF p_before IS NULL OR p_after IS NULL OR p_changes IS NULL OR pg_catalog.jsonb_typeof(p_changes) <> 'array'
                    OR {source}(p_scope) IS DISTINCT FROM p_before THEN
                    RAISE EXCEPTION 'manifest_bootstrap_original_changed' USING ERRCODE = '40001';
                END IF;
                FOR change IN SELECT value FROM pg_catalog.jsonb_array_elements(p_changes) value LOOP
                    IF NOT (change ?& ARRAY['Tag','Before','After']) OR (SELECT count(*) FROM pg_catalog.jsonb_object_keys(change)) <> 3 THEN
                        RAISE EXCEPTION 'manifest_bootstrap_effect_invalid' USING ERRCODE = '42501';
                    END IF;
                    before_row := change->'Before'; after_row := change->'After';
                    IF pg_catalog.jsonb_typeof(after_row) <> 'object' THEN
                        RAISE EXCEPTION 'manifest_bootstrap_effect_invalid' USING ERRCODE = '42501';
                    END IF;
                    CASE change->>'Tag'
                        {string.Join("\n", branches)}
                        ELSE RAISE EXCEPTION 'manifest_bootstrap_relation_invalid' USING ERRCODE = '42501';
                    END CASE;
                    GET DIAGNOSTICS affected = ROW_COUNT;
                    IF affected <> 1 THEN
                        RAISE EXCEPTION 'manifest_bootstrap_preimage_changed' USING ERRCODE = '40001';
                    END IF;
                END LOOP;
                IF {source}(p_scope) IS DISTINCT FROM p_after THEN
                    RAISE EXCEPTION 'manifest_bootstrap_postimage_changed' USING ERRCODE = '40001';
                END IF;
                RETURN p_after;
            END;
            """;
    }
}
