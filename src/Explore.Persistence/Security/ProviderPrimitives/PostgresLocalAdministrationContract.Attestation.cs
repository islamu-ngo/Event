namespace Explore.Persistence.Security;

public static partial class PostgresLocalAdministrationContract
{
    private const string CatalogueGuardName = "assert_catalogue";

    // Stored entries run as OwnerRole. Independent callers retain the inline
    // assertion and exact bodies because the helper has no application grant.
    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) => $"""
        {CatalogueGuardWitness(context, enrollment)}
        PERFORM {FunctionSql(context, CatalogueGuardName)}();
        """;

    private static string CatalogueGuardWitness(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        string function = FunctionSql(context, CatalogueGuardName);
        string fingerprint = Hash(CatalogueGuardBody(context, enrollment));
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(function + "()")})
                    AND f.proowner = pg_catalog.to_regrole('{OwnerRole}')::oid
                    AND f.prokind = 'f' AND f.prosecdef AND f.provolatile = 'v' AND f.proparallel = 'u'
                    AND NOT f.proleakproof AND NOT f.proisstrict AND NOT f.proretset
                    AND f.prorettype = 'pg_catalog.void'::regtype AND f.pronargs = 0
                    AND f.proargtypes = ''::pg_catalog.oidvector AND f.pronargdefaults = 0
                    AND f.proargdefaults IS NULL AND f.provariadic = 0 AND f.prosupport = 0
                    AND f.proallargtypes IS NULL AND f.proargmodes IS NULL AND f.proargnames IS NULL
                    AND f.proconfig = ARRAY['search_path=pg_catalog']::text[]
                    AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                    AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(fingerprint)}
                    AND pg_catalog.obj_description(f.oid,'pg_proc') = {Text(Stamp + fingerprint)}
                    AND pg_catalog.has_function_privilege(f.proowner,f.oid,'EXECUTE')
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(
                        COALESCE(f.proacl,pg_catalog.acldefault('f',f.proowner))) a
                        WHERE a.privilege_type <> 'EXECUTE' OR a.grantee <> f.proowner))
            THEN RAISE EXCEPTION 'local_administration_catalogue_guard_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    private static string CatalogueGuardBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) => $"""
        BEGIN
            {CatalogueGuard(context, enrollment)}
        END;
        """;

    private static string ExactBodies(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        string.Join("\n", Functions(context, enrollment).Select(function => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context, function.Name) + "(" + function.Signature + ")")})
                    AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(Hash(function.Body))})
            THEN RAISE EXCEPTION 'local_administration_exact_body_drift' USING ERRCODE = '55000'; END IF;
            """));
}
