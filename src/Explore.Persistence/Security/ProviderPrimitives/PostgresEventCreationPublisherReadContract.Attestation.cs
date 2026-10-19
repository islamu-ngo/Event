namespace Explore.Persistence.Security;

public static partial class PostgresEventCreationPublisherReadContract
{
    private static string ExactBody(ExploreDbContext context, string body) => $"""
        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
            WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context) + "(uuid,uuid)")})
                AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof AND NOT f.proisstrict
                AND f.proretset AND f.prorettype = 'pg_catalog.record'::regtype
                AND f.proargdefaults IS NULL AND f.pronargdefaults = 0 AND f.provariadic = 0
                AND f.proargnames = ARRAY['p_tenant','p_principal','is_organization','membership_id',
                    'placement_id','publisher_id','publisher_role_id','display_name','event_create_permission_id']::text[]
                AND f.proargmodes = ARRAY['i','i','t','t','t','t','t','t','t']::"char"[]
                AND f.proallargtypes = ARRAY['uuid'::regtype,'uuid'::regtype,'boolean'::regtype,'uuid'::regtype,
                    'uuid'::regtype,'uuid'::regtype,'integer'::regtype,'text'::regtype,'integer'::regtype]::oid[]
                AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(Hash(body))})
        THEN RAISE EXCEPTION 'event_creation_publisher_body_drift' USING ERRCODE = '55000'; END IF;
        """;

    private static string CallerPrivilegeGuard(SourceSql sql, PostgresNativeQueueEnrollment enrollment) =>
        ActorPublisherCatalogProof.CallerPrivilegeGuard(sql.Names, sql.Sources.Keys, enrollment, "event_creation_publisher");

    private static string PolicyGuard(SourceSql sql, IReadOnlyDictionary<Type, string> policies) =>
        string.Join("\n", policies.SelectMany(pair => new[] { false, true }.Select(permissive =>
        {
            string name = permissive ? Policy + "_allow" : Policy;
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = {Text(sql.Names.Table(pair.Key))}::regclass AND p.polname = {Text(name)}
                        AND p.polcmd = 'r' AND p.polpermissive = {Boolean(permissive)}
                        AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                        AND p.polqual IS NOT NULL AND p.polwithcheck IS NULL
                        AND pg_catalog.obj_description(p.oid,'pg_policy') = {Text(Stamp + Hash(pair.Value) + ":")}
                            || pg_catalog.md5(p.polqual::text))
                THEN RAISE EXCEPTION 'event_creation_publisher_policy_drift' USING ERRCODE = '55000'; END IF;
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = {Text(sql.Names.Table(pair.Key))}::regclass
                        AND p.polname NOT IN ('tenant_isolation','{Policy}','{Policy}_allow')
                        AND EXISTS (SELECT 1 FROM unnest(p.polroles) role_id
                            WHERE role_id = 0 OR pg_catalog.pg_has_role('{OwnerRole}',role_id,'USAGE')))
                THEN RAISE EXCEPTION 'event_creation_publisher_unexpected_policy' USING ERRCODE = '55000'; END IF;
                """;
        })));

    private static string SourceGuard(ExploreDbContext context, SourceSql sql, PostgresNativeQueueEnrollment enrollment) =>
        ActorPublisherCatalogProof.SourceGuard(context, sql.Names, sql.Sources.Keys, enrollment, "event_creation_publisher");
}
