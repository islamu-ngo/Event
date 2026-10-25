using Explore.Domain;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresPublicationFanoutExecutionContract
{
    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var definition = Define(context, enrollment);
        var sql = definition.Sql;
        string function = sql.Names.Name(Routine);
        string roles = string.Join(",", Roles.Select(Text));
        string schema = sql.Names.Quote(sql.Names.Schema);
        string grants = string.Join("\n", sql.Sources.SelectMany(source =>
            new[] { "SELECT", "INSERT", "UPDATE" }.Select(privilege =>
            {
                string[] columns = source.Value.Where(pair => pair.Value.Contains(privilege))
                    .Select(pair => sql.Names.Column(source.Key, pair.Key)).ToArray();
                return columns.Length == 0 ? "" :
                    $"GRANT {privilege} ({string.Join(",", columns)}) ON {sql.Names.Table(source.Key)} TO {OwnerRole};";
            })));
        string policies = string.Join("\n", definition.Policies.SelectMany(pair => new[] { false, true }.Select(permissive =>
        {
            string name = permissive ? Policy + "_allow" : Policy;
            string check = WritePolicy(pair.Key, pair.Value);
            return $"""
                CREATE POLICY {name} ON {sql.Names.Table(pair.Key)} AS {(permissive ? "PERMISSIVE" : "RESTRICTIVE")}
                    FOR ALL TO {OwnerRole} USING ({pair.Value}) WITH CHECK ({check});
                EXECUTE (SELECT pg_catalog.format('COMMENT ON POLICY %I ON %s IS %L',p.polname,
                    {Text(sql.Names.Table(pair.Key))},{Text(PolicyStamp + Hash(pair.Value + "|" + check) + ":")} ||
                        pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                    FROM pg_catalog.pg_policy p WHERE p.polrelid = {Text(sql.Names.Table(pair.Key))}::regclass AND p.polname = '{name}');
                """;
        })));
        string memberships = string.Join("\n", definition.Privileges.Memberships.Select(edge => $"""
            EXECUTE pg_catalog.format('GRANT %I TO %I WITH INHERIT {Boolean(edge.Inherit)}, SET {Boolean(edge.Set)}, ADMIN FALSE',
                {Text(edge.Parent)},{Text(edge.Member)});
            """));
        return $"""
            DO $bootstrap$ DECLARE role_name text; existing integer; BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                THEN RAISE EXCEPTION 'publication_fanout_migrator_required' USING ERRCODE = '42501'; END IF;
                {SourceGuard(context, sql, enrollment)}
                SELECT count(*) INTO existing FROM pg_catalog.pg_roles WHERE rolname IN ({roles});
                IF existing = 0 THEN
                    IF pg_catalog.to_regprocedure({Text(function + "(" + Signature + ")")}) IS NOT NULL
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                            WHERE p.polrelid IN ({string.Join(",", sql.Sources.Keys.Select(type => Text(sql.Names.Table(type)) + "::regclass"))})
                                AND p.polname IN ('{Policy}','{Policy}_allow'))
                    THEN RAISE EXCEPTION 'publication_fanout_partial_installation' USING ERRCODE = '55000'; END IF;
                    FOREACH role_name IN ARRAY ARRAY[{roles}] LOOP
                        EXECUTE pg_catalog.format('CREATE ROLE %I NOLOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS',role_name);
                    END LOOP;
                    {memberships}
                    FOREACH role_name IN ARRAY ARRAY[{roles}] LOOP
                        IF role_name <> '{MigratorRole}' AND EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members m
                            WHERE m.roleid = pg_catalog.to_regrole(role_name)::oid AND m.member = pg_catalog.to_regrole(session_user)::oid)
                        THEN EXECUTE pg_catalog.format('REVOKE %I FROM %I',role_name,session_user); END IF;
                    END LOOP;
                    {policies}
                ELSE
                    IF existing <> 3 THEN RAISE EXCEPTION 'publication_fanout_partial_roles' USING ERRCODE = '55000'; END IF;
                    {definition.Guard} {ExactBody(definition)}
                END IF;
            END $bootstrap$;
            GRANT USAGE ON SCHEMA {schema} TO {string.Join(",", Roles)};
            GRANT CREATE ON SCHEMA {schema} TO {OwnerRole};
            {grants}
            GRANT EXECUTE ON FUNCTION {PostgresNotificationOutboxAdmissionContract.ReadFunctionSql(context)}(uuid,timestamp with time zone) TO {OwnerRole};
            DO $install$ DECLARE previous_role text := pg_catalog.current_setting('role'); BEGIN
                PERFORM pg_catalog.set_config('role','{OwnerRole}',true);
                EXECUTE $ddl$
                    CREATE OR REPLACE FUNCTION {function}(p_claim jsonb,p_operation text,p_request jsonb)
                    RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                    AS $body${definition.Body}$body$;
                    REVOKE ALL ON FUNCTION {function}({Signature}) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION {function}({Signature}) TO {RuntimeRole};
                $ddl$;
                PERFORM pg_catalog.set_config('role',previous_role,true);
            END $install$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $installed$ BEGIN {definition.Guard} {ExactBody(definition)} END $installed$;
            """;
    }

    private static string WritePolicy(Type type, string predicate) =>
        type == typeof(NotificationFanoutRun) || type == typeof(Notification) || type == typeof(WebPushDispatchOutbox)
            ? predicate : "false";

    private static string ExactBody(Definition definition) => $"""
        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
            WHERE f.oid = pg_catalog.to_regprocedure({Text(definition.Sql.Names.Name(Routine) + "(" + Signature + ")")})
                AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof AND NOT f.proisstrict
                AND NOT f.proretset AND f.prorettype = 'pg_catalog.jsonb'::regtype
                AND f.proargdefaults IS NULL AND f.pronargdefaults = 0 AND f.provariadic = 0
                AND f.proargnames = ARRAY['p_claim','p_operation','p_request']::text[]
                AND f.proargmodes IS NULL AND f.proallargtypes IS NULL
                AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(Hash(definition.Body))})
        THEN RAISE EXCEPTION 'publication_fanout_body_drift' USING ERRCODE = '55000'; END IF;
        """;

    private static string Guard(ExploreDbContext context, SourceSql sql, PostgresPrivilegeGuard.Contract privileges,
        IReadOnlyDictionary<Type, string> policies, PostgresNativeQueueEnrollment enrollment)
    {
        string nested = PostgresNotificationOutboxAdmissionContract.ReadFunctionSql(context) + "(uuid,timestamp with time zone)";
        string own = sql.Names.Name(Routine) + "(" + Signature + ")";
        return PostgresPrivilegeGuard.BuildAssertSql(privileges)
            + SourceGuard(context, sql, enrollment)
            + PostgresPrivilegeGuard.BuildRoutineAclAssertSql([nested],
                PostgresNotificationOutboxAdmissionContract.OwnerRole, PostgresNotificationOutboxAdmissionContract.GlobalRole, true,
                new Dictionary<string, IReadOnlyList<string>> { [nested] = [PostgresOutboxExecutionContract.OwnerRole, OwnerRole] })
            + string.Join("\n", policies.SelectMany(pair => new[] { false, true }.Select(permissive =>
            {
                string name = permissive ? Policy + "_allow" : Policy;
                string check = WritePolicy(pair.Key, pair.Value);
                return $"""
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                        WHERE p.polrelid = {Text(sql.Names.Table(pair.Key))}::regclass AND p.polname = {Text(name)}
                            AND p.polcmd = '*' AND p.polpermissive = {Boolean(permissive)}
                            AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                            AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                            AND pg_catalog.obj_description(p.oid,'pg_policy') = {Text(PolicyStamp + Hash(pair.Value + "|" + check) + ":")}
                                || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                    THEN RAISE EXCEPTION 'publication_fanout_policy_drift' USING ERRCODE = '55000'; END IF;
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                        WHERE p.polrelid = {Text(sql.Names.Table(pair.Key))}::regclass
                            AND p.polname NOT IN ('tenant_isolation','{Policy}','{Policy}_allow')
                            AND EXISTS (SELECT 1 FROM unnest(p.polroles) role_id
                                WHERE role_id = 0 OR pg_catalog.pg_has_role('{OwnerRole}',role_id,'USAGE')))
                    THEN RAISE EXCEPTION 'publication_fanout_unexpected_policy' USING ERRCODE = '55000'; END IF;
                    """;
            }))) + $"""
                IF NOT pg_catalog.has_function_privilege('{OwnerRole}',{Text(nested)},'EXECUTE')
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                        JOIN pg_catalog.pg_namespace n ON n.oid = f.pronamespace
                        CROSS JOIN LATERAL pg_catalog.aclexplode(COALESCE(f.proacl,pg_catalog.acldefault('f',f.proowner))) a
                        WHERE n.nspname = {Text(sql.Names.Schema)}
                            AND a.grantee IN ('{OwnerRole}'::regrole,'{MigratorRole}'::regrole,'{RuntimeRole}'::regrole)
                            AND NOT (f.oid = pg_catalog.to_regprocedure({Text(own)})
                                AND a.grantee IN ('{OwnerRole}'::regrole,'{RuntimeRole}'::regrole)
                                OR f.oid = pg_catalog.to_regprocedure({Text(nested)}) AND a.grantee = '{OwnerRole}'::regrole))
                THEN RAISE EXCEPTION 'publication_fanout_nested_authority_drift' USING ERRCODE = '55000'; END IF;
                """;
    }

    private static string SourceGuard(ExploreDbContext context, SourceSql sql, PostgresNativeQueueEnrollment enrollment)
    {
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n WHERE n.nspname = {Text(sql.Names.Schema)}
                AND n.nspowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
            THEN RAISE EXCEPTION 'publication_fanout_namespace_drift' USING ERRCODE = '55000'; END IF;
            """ + string.Join("\n", sql.Sources.Select(source =>
            {
                var entity = context.Model.FindEntityType(source.Key)!;
                var tenant = tenantTables.SingleOrDefault(table => table.TableName == entity.GetTableName());
                string table = Text(sql.Names.Table(source.Key));
                return $"""
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({table})
                        AND t.relkind = 'r' AND t.relpersistence = 'p' AND NOT t.relispartition
                        AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid
                        AND t.relrowsecurity = {Boolean(tenant is not null)} AND t.relforcerowsecurity = {Boolean(tenant is not null)})
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = {table}::regclass)
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i WHERE i.inhrelid = {table}::regclass OR i.inhparent = {table}::regclass)
                    THEN RAISE EXCEPTION 'publication_fanout_source_catalog_drift' USING ERRCODE = '55000'; END IF;
                    {(tenant is null ? "" : $"""
                        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {table}::regclass
                            AND p.polname = 'tenant_isolation' AND p.polcmd = '*' AND p.polpermissive
                            AND p.polroles = ARRAY[0::oid] AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                            AND pg_catalog.obj_description(p.oid,'pg_policy') =
                                {Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenant, sql.Names.Schema))}
                                || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                        THEN RAISE EXCEPTION 'publication_fanout_tenant_policy_drift' USING ERRCODE = '55000'; END IF;
                        """)}
                    """;
            }))
            + ActorPublisherCatalogProof.CallerPrivilegeGuard(context, sql.Names, sql.Sources.Keys,
                enrollment, "publication_fanout");
    }
}
