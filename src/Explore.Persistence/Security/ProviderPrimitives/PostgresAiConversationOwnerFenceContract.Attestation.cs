namespace Explore.Persistence.Security;

public static partial class PostgresAiConversationOwnerFenceContract
{
    private static string ExactBodies(ExploreDbContext context, Definition definition) =>
        ExactBody(context, definition.Body, FunctionSql(context), Arguments,
            "'p_tenant','p_principal','p_actor','p_admitted','p_privacy','p_organizer'")
        + ExactBody(context, definition.PrivacyBody, PrivacyFunctionSql(context), "uuid,uuid",
            "'p_tenant','p_principal'");

    private static string ExactBody(ExploreDbContext context, string body, string function, string arguments,
        string argumentNames) => $"""
        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
            WHERE f.oid = pg_catalog.to_regprocedure({Text(function + "(" + arguments + ")")})
                AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof AND NOT f.proisstrict
                AND NOT f.proretset AND f.prorettype = 'pg_catalog.bool'::regtype
                AND f.proargdefaults IS NULL AND f.pronargdefaults = 0 AND f.provariadic = 0
                AND f.proargnames = ARRAY[{argumentNames}]::text[]
                AND f.proargmodes IS NULL AND f.proallargtypes IS NULL
                AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(Hash(body))})
        THEN RAISE EXCEPTION 'ai_conversation_owner_body_drift' USING ERRCODE = '55000'; END IF;
        """;

    private static string CallerPrivilegeGuard(SourceSql sql, PostgresNativeQueueEnrollment enrollment) =>
        ActorPublisherCatalogProof.CallerPrivilegeGuard(sql.Names, sql.Sources.Keys, enrollment, "ai_conversation_owner",
            allowRuntimeCatalogueRoleRead: true);

    private static string PolicyGuard(SourceSql sql, IReadOnlyDictionary<Type, string> policies) =>
        string.Join("\n", policies.SelectMany(pair => PolicyModes.Select(permissive =>
        {
            string name = permissive ? Policy + "_allow" : Policy;
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = {Text(sql.Names.Table(pair.Key))}::regclass AND p.polname = {Text(name)}
                        AND p.polcmd = '*' AND p.polpermissive = {Boolean(permissive)}
                        AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                        AND p.polqual IS NOT NULL AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid) = 'false'
                        AND pg_catalog.obj_description(p.oid,'pg_policy') = {Text(Stamp + Hash(pair.Value) + ":")}
                            || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                THEN RAISE EXCEPTION 'ai_conversation_owner_policy_drift' USING ERRCODE = '55000'; END IF;
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = {Text(sql.Names.Table(pair.Key))}::regclass
                        AND p.polname NOT IN ('tenant_isolation','{Policy}','{Policy}_allow')
                        AND EXISTS (SELECT 1 FROM unnest(p.polroles) role_id
                            WHERE role_id = 0 OR pg_catalog.pg_has_role('{OwnerRole}',role_id,'USAGE')))
                THEN RAISE EXCEPTION 'ai_conversation_owner_unexpected_policy' USING ERRCODE = '55000'; END IF;
                """;
        })));

    private static string SourceGuard(ExploreDbContext context, SourceSql sql, PostgresNativeQueueEnrollment enrollment) =>
        ActorPublisherCatalogProof.SourceGuard(context, sql.Names, sql.Sources.Keys, enrollment, "ai_conversation_owner");
}
