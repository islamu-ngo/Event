using Explore.Domain;
using Explore.Persistence.Services;

namespace Explore.Persistence.Security;

public static partial class PostgresConfigurationImportTenantIdentityContract
{
    private static string FenceBody(ExploreDbContext context)
    {
        string id = Column(context, typeof(Tenant), "Id");
        return $"""
            DECLARE tenant uuid;
            BEGIN
                PERFORM {FunctionSql(context, "assert_authority")}();
                IF p_tenants IS NULL OR cardinality(p_tenants) > {EventDiscoveryDisclosureMutationScope.MaximumAffectedTenants}
                    OR array_position(p_tenants, NULL) IS NOT NULL
                    OR '{Guid.Empty}'::uuid = ANY(p_tenants) THEN
                    RAISE EXCEPTION 'discovery_disclosure_fanout_exceeded' USING ERRCODE = '54000';
                END IF;
                PERFORM pg_catalog.set_config('app.configuration_import_identity_tenants', p_tenants::text, true);
                FOR tenant IN SELECT DISTINCT unnest(p_tenants) ORDER BY 1 LOOP
                    -- A write, rather than SELECT FOR UPDATE alone, rejects a stale
                    -- serializable snapshot exactly like the ordinary parent fence.
                    UPDATE {Table(context, typeof(Tenant))} SET {id} = {id} WHERE {id} = tenant;
                END LOOP;
                RETURN;
            END;
            """;
    }

    private static string WriteBody(ExploreDbContext context)
    {
        string T(string property) => Column(context, typeof(Tenant), property);
        return $"""
            BEGIN
                PERFORM {FunctionSql(context, "assert_authority")}();
                IF p_tenant IS NULL OR p_tenant = '{Guid.Empty}'::uuid
                    OR p_actor IS NULL OR p_actor = '{Guid.Empty}'::uuid
                    OR p_name IS NULL OR p_name !~ '[^[:space:]]'
                    OR (char_length(p_name) +
                        char_length(regexp_replace(p_name, U&'[\0001-\FFFF]', '', 'g'))) > 500
                    OR p_at IS NULL OR NOT pg_catalog.isfinite(p_at)
                    OR p_at <= '0001-01-01 00:00:00+00'::timestamptz
                    OR p_at >= '10000-01-01 00:00:00+00'::timestamptz THEN
                    RAISE EXCEPTION 'configuration_import_tenant_identity_shape' USING ERRCODE = '22023';
                END IF;
                PERFORM {FunctionSql(context, "fence")}(ARRAY[p_tenant]);
                UPDATE {Table(context, typeof(Tenant))} SET
                    {T("FullName")} = p_name, {T("UpdatedAt")} = p_at, {T("UpdatedBy")} = p_actor
                    WHERE {T("Id")} = p_tenant;
                RETURN FOUND;
            END;
            """;
    }

    private static string FinalizeBody(ExploreDbContext context)
    {
        string R(string property) => Column(context, typeof(EventDiscoveryRevision), property);
        string revisions = Table(context, typeof(EventDiscoveryRevision));
        return $"""
            DECLARE ordinal integer; tenant uuid;
            BEGIN
                PERFORM {FunctionSql(context, "assert_authority")}();
                IF p_tenants IS NULL OR cardinality(p_tenants) > {EventDiscoveryDisclosureMutationScope.MaximumAffectedTenants}
                    OR cardinality(p_ids) IS DISTINCT FROM cardinality(p_tenants)
                    OR cardinality(p_disclosure) IS DISTINCT FROM cardinality(p_tenants)
                    OR cardinality(p_expected) IS DISTINCT FROM cardinality(p_tenants)
                    OR cardinality(p_advances) IS DISTINCT FROM cardinality(p_tenants)
                    OR array_position(p_tenants, NULL) IS NOT NULL
                    OR array_position(p_ids, NULL) IS NOT NULL
                    OR array_position(p_disclosure, NULL) IS NOT NULL
                    OR array_position(p_advances, NULL) IS NOT NULL
                    OR '{Guid.Empty}'::uuid = ANY(p_tenants) OR '{Guid.Empty}'::uuid = ANY(p_ids)
                    OR EXISTS (SELECT 1 FROM unnest(p_advances) value WHERE value < 0)
                    OR (SELECT count(DISTINCT value) FROM unnest(p_tenants) value) <> cardinality(p_tenants) THEN
                    RAISE EXCEPTION 'invalid discovery finalization bounds' USING ERRCODE = '22023';
                END IF;
                PERFORM {FunctionSql(context, "fence")}(p_tenants);
                FOR ordinal IN SELECT i FROM generate_subscripts(p_tenants, 1) i ORDER BY p_tenants[i] LOOP
                    tenant := p_tenants[ordinal];
                    INSERT INTO {revisions} ({R("Id")}, {R("TenantId")}, {R("IdentityEpoch")}, {R("DisclosureEpoch")})
                    VALUES (p_ids[ordinal], tenant, 0, 0) ON CONFLICT ({R("TenantId")}) DO NOTHING;
                    UPDATE {revisions}
                        SET {R("DisclosureEpoch")} = {R("DisclosureEpoch")} + CASE WHEN p_disclosure[ordinal] THEN 1 ELSE 0 END,
                            {R("IdentityEpoch")} = {R("IdentityEpoch")} + p_advances[ordinal]
                        WHERE {R("TenantId")} = tenant
                            AND (p_expected[ordinal] IS NULL OR {R("IdentityEpoch")} = p_expected[ordinal]);
                    IF NOT FOUND THEN
                        RAISE EXCEPTION 'discovery_revision_conflict' USING ERRCODE = '40001';
                    END IF;
                END LOOP;
                RETURN;
            END;
            """;
    }
}
