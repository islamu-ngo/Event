using Explore.Domain.Settings.Documents;

namespace Explore.Persistence.Security;

public static partial class PostgresConfigurationImportBrandingContract
{
    private static string WriteBody(ExploreDbContext context)
    {
        string C(string property) => Column(context, typeof(TenantSettingsDocument), property);
        return $"""
            BEGIN
                PERFORM {FunctionSql(context, "assert_authority")}();
                IF p_tenant IS NULL OR p_tenant = '{Guid.Empty}'::uuid
                    OR p_id IS NULL OR p_id = '{Guid.Empty}'::uuid
                    OR p_actor IS NULL OR p_actor = '{Guid.Empty}'::uuid
                    OR p_stamp IS NULL OR p_stamp = '{Guid.Empty}'::uuid
                    OR p_stamp IS NOT DISTINCT FROM p_expected
                    OR p_key IS DISTINCT FROM {Text(SettingsDocumentKeys.Tenant.Branding)}
                    OR p_schema IS DISTINCT FROM {TenantBrandingSettingsDocumentDefaults.SchemaVersion}
                    OR p_defaults IS DISTINCT FROM {Text(TenantBrandingSettingsDocumentDefaults.DefaultsVersion)}
                    OR p_payload IS NULL OR pg_catalog.jsonb_typeof(p_payload) <> 'object'
                    OR p_at IS NULL OR NOT pg_catalog.isfinite(p_at)
                    OR p_at <= '0001-01-01 00:00:00+00'::timestamptz
                    OR p_at >= '10000-01-01 00:00:00+00'::timestamptz THEN
                    RAISE EXCEPTION 'configuration_import_branding_shape' USING ERRCODE = '22023';
                END IF;
                IF EXISTS (SELECT 1 FROM pg_catalog.jsonb_each(p_payload) AS field(field_name, field_value)
                    WHERE field.field_name NOT IN ('displayName','logoUrl','faviconUrl','customCssUrl')
                        OR pg_catalog.jsonb_typeof(field.field_value) NOT IN ('string','null')
                        OR pg_catalog.jsonb_typeof(field.field_value) = 'string' AND
                            (char_length(field.field_value #>> ARRAY[]::text[]) +
                                char_length(regexp_replace(field.field_value #>> ARRAY[]::text[],
                                    U&'[\0001-\FFFF]', '', 'g'))) >
                            CASE WHEN field.field_name = 'displayName' THEN 200 ELSE 2048 END) THEN
                    RAISE EXCEPTION 'configuration_import_branding_payload_shape' USING ERRCODE = '22023';
                END IF;
                PERFORM pg_catalog.set_config('app.configuration_import_branding_tenant', p_tenant::text, true);
                IF p_expected IS NULL THEN
                    INSERT INTO {Table(context, typeof(TenantSettingsDocument))}
                        ({C("Id")}, {C("TenantId")}, {C("DocumentKey")}, {C("SchemaVersion")},
                         {C("DefaultsVersion")}, {C("PayloadJson")}, {C("ConcurrencyStamp")},
                         {C("CreatedAt")}, {C("CreatedBy")}, {C("UpdatedAt")}, {C("UpdatedBy")})
                    VALUES (p_id, p_tenant, p_key, p_schema, p_defaults, p_payload,
                        p_stamp, p_at, p_actor, NULL, NULL)
                    ON CONFLICT ({C("TenantId")}, {C("DocumentKey")}) DO NOTHING;
                ELSE
                    UPDATE {Table(context, typeof(TenantSettingsDocument))} SET
                        {C("SchemaVersion")} = p_schema, {C("DefaultsVersion")} = p_defaults,
                        {C("PayloadJson")} = p_payload, {C("ConcurrencyStamp")} = p_stamp,
                        {C("UpdatedAt")} = p_at, {C("UpdatedBy")} = p_actor
                    WHERE {C("Id")} = p_id AND {C("TenantId")} = p_tenant AND {C("DocumentKey")} = p_key
                        AND {C("SchemaVersion")} = p_schema AND {C("DefaultsVersion")} = p_defaults
                        AND {C("ConcurrencyStamp")} = p_expected;
                END IF;
                RETURN FOUND;
            END;
            """;
    }
}
